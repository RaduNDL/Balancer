using System.Security.Claims;
using Balancer.Contracts.Auth;
using Balancer.Data;
using Balancer.Enums;
using Balancer.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Balancer.Controllers;

// Activează comportamentul API și validarea automată a DTO-urilor.
[ApiController]

// Toate rutele acestui controller încep cu /api/auth.
[Route("api/auth")]

// Verifică tokenul CSRF pentru POST și celelalte metode nesigure.
[AutoValidateAntiforgeryToken]

// Împiedică memorarea răspunsurilor în cache.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]

// Folosește politica definită în Program.cs.
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _database;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IAntiforgery _antiforgery;

    // Serviciile sunt furnizate prin injectarea dependențelor.
    public AuthController(
        ApplicationDbContext database,
        IPasswordHasher<User> passwordHasher,
        IAntiforgery antiforgery)
    {
        _database = database;
        _passwordHasher = passwordHasher;
        _antiforgery = antiforgery;
    }

    // GET /api/auth/csrf
    // Poate fi apelat și înainte de autentificare.
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        // Creează tokenul pentru cerere și cookie-ul CSRF asociat.
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        // Angular trimite acest token în antetul X-CSRF-TOKEN.
        return Ok(new
        {
            token = tokens.RequestToken
        });
    }

    // POST /api/auth/register
    // Creează numai contul personal.
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        if (fullName.Length < 2)
        {
            return BadRequest(new
            {
                message = "Enter a valid full name."
            });
        }

        var emailExists = await _database.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (emailExists)
        {
            return Conflict(new
            {
                message = "An account with this email already exists."
            });
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            NormalizedEmail = normalizedEmail
        };

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.Password);

        _database.Users.Add(user);

        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                  && sqlException.Number is 2601 or 2627)
        {
            return Conflict(new
            {
                message = "An account with this email already exists."
            });
        }

        return StatusCode(201, new
        {
            message = "Account created. Sign in to set up your profile."
        });
    }

    // POST /api/auth/login
    // Verifică datele și emite cookie-ul de autentificare.
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var user = await _database.Users.SingleOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

        // Nu diferențiem în mesaj emailul necunoscut de parola greșită.
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        // Verificarea folosește informațiile incluse în hashul salvat.
        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        // Actualizăm hashul dacă parametrii săi au devenit învechiți.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            await _database.SaveChangesAsync(cancellationToken);
        }

        // Cookie-ul identifică utilizatorul.
        // Rolurile rămân asociate firmelor și sunt citite din baza de date.
        var claims = new[]
                  {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        user.Id.ToString()),

                    new Claim(
                        "balancer_session",
                        user.SessionKey.ToString("N"))
                };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        // Generează biletul protejat și îl trimite prin Set-Cookie.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // Nu solicităm un cookie persistent.
                // Durata biletului este configurată în Program.cs.
                IsPersistent = false
            });

        return Ok(new
        {
            message = "Signed in successfully."
        });
    }

    // GET /api/auth/me
    // Returnează profilul și toate apartenențele active.
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        // Citim identificatorul din cookie-ul validat.
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        // TryParse evită o excepție dacă identificatorul este invalid.
        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        // Selectăm doar câmpurile permise în răspuns.
        // PasswordHash nu ajunge niciodată în browser.
        var user = await _database.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new
            {
                user.Id,
                user.FullName,
                user.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        // Citim firmele și rolurile utilizatorului.
        var memberships = await _database.CompanyMembers
            .AsNoTracking()
            .Where(member =>
                member.UserId == userId &&
                member.IsActive)
            .OrderBy(member => member.Company.Name)
            .Select(member => new
            {
                member.CompanyId,
                CompanyName = member.Company.Name,
                member.Role
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            user.Id,
            user.FullName,
            user.Email,

            // Transformăm rolul în text după citirea din SQL Server.
            // Lista este returnată și atunci când este goală.
            Memberships = memberships.Select(member => new
            {
                member.CompanyId,
                member.CompanyName,
                Role = member.Role.ToString()
            }).ToArray()
        });
    }

    // POST /api/auth/logout
    // Elimină cookie-ul din browserul care trimite cererea.
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return Ok(new
        {
            message = "Signed out successfully."
        });
    }
}