using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Balancer.Contracts.People;
using Balancer.Contracts.Onboarding;
using Balancer.Data;
using Balancer.Enums;
using Balancer.Models;
using Balancer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Balancer.Controllers;

[ApiController]
[Route("api/people")]
[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class PeopleController(
    ApplicationDbContext database,
    OnboardingFlowService flow) : ControllerBase
{
    // Identitatea contului este extrasă din cookie-ul validat.
    private int? CurrentUserId =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
                ? id
                : null;

    // Returnăm numai conturile active și vizibile.
    // Persoanele care s-au blocat reciproc nu se găsesc prin People.
    private IQueryable<User> VisibleUsers(int userId) =>
        database.Users.AsNoTracking().Where(person =>
            person.IsActive
            && person.IsDiscoverable
            && !database.DirectConversations.Any(chat =>
                chat.State == ConversationState.Blocked
                && (
                    (chat.LowUserId == userId
                        && chat.HighUserId == person.Id)
                    || (chat.HighUserId == userId
                        && chat.LowUserId == person.Id)
                )));

    // Construim explicit răspunsul public.
    // Emailul este inclus numai dacă persoana a ales asta.
    private static PersonResponse PublicProfile(User person) =>
        new(
            person.Id,
            person.FullName,
            person.JobTitle,
            person.Bio,
            person.Skills,
            person.ShowEmail ? person.Email : null);

    // GET /api/people?search=Alex&page=1
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, StringLength(80)] string? search = null,
        [FromQuery, Range(1, 10000)] int page = 1,
        CancellationToken ct = default)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        const int pageSize = 12;

        var query = VisibleUsers(userId);
        var term = search?.Trim();

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(person =>
                person.FullName.Contains(term)
                || person.JobTitle.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(person => person.FullName)
            .ThenBy(person => person.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new
        {
            items = items.Select(PublicProfile),
            page,
            pageSize,
            total
        });
    }

    // Profilul propriu poate fi editat și când este ascuns din director.
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var person = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == userId && p.IsActive,
                ct);

        if (person is null)
            return Unauthorized();

        return Ok(new
        {
            person.FullName,
            person.JobTitle,
            person.Bio,
            person.Skills,
            person.IsDiscoverable,
            person.ShowEmail
        });
    }

    // PUT /api/people/me
    // Nu primim un UserId modificabil din frontend.
    [HttpPut("me")]
    [EnableRateLimiting("auth")]
    [RequestSizeLimit(12000)]
    public async Task<IActionResult> UpdateMe(
        UpdateProfileRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (request.FullName.Trim().Length < 2)
        {
            return BadRequest(new
            {
                message = "Enter a valid full name."
            });
        }

        var person = await database.Users
            .SingleOrDefaultAsync(
                p => p.Id == userId && p.IsActive,
                ct);

        if (person is null)
            return Unauthorized();

        person.FullName = request.FullName.Trim();
        person.JobTitle = request.JobTitle.Trim();
        person.Bio = request.Bio.Trim();
        person.Skills = request.Skills.Trim();
        person.IsDiscoverable = request.IsDiscoverable;
        person.ShowEmail = request.ShowEmail;

        await database.SaveChangesAsync(ct);

        return Ok(new
        {
            message = "Your profile was saved."
        });
    }

    // GET /api/people/2
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(
        int id,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var person = await VisibleUsers(userId)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

        if (person is null)
            return NotFound();

        return Ok(PublicProfile(person));
    }

    // Generează o invitație fără să expună emailul destinatarului.
    [HttpPost("{id:int}/invite")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Invite(
        int id,
        InvitePersonRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (id == userId)
        {
            return BadRequest(new
            {
                message = "Choose another person."
            });
        }

        // Ownerul trebuie să aparțină firmei selectate.
        var owner = await database.CompanyMembers
            .AsNoTracking()
            .Include(m => m.User)
            .SingleOrDefaultAsync(m =>
                m.CompanyId == request.CompanyId
                && m.UserId == userId
                && m.IsActive
                && m.User.IsActive
                && m.Role == CompanyRole.Owner,
                ct);

        if (owner is null)
            return Forbid();

        var target = await VisibleUsers(userId)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

        if (target is null)
            return NotFound();

        var roles = request.AllowedRoles
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0
            || roles.Any(role =>
                !OnboardingFlowService.AssignableRoles.Contains(
                    role,
                    StringComparer.Ordinal)))
        {
            return BadRequest(new
            {
                message = "Select permitted company roles."
            });
        }

        if (await database.CompanyMembers.AnyAsync(
            m => m.CompanyId == request.CompanyId
                && m.UserId == id,
            ct))
        {
            return Conflict(new
            {
                message = "This person already has a membership in that workspace."
            });
        }

        var expiresAtUtc = DateTime.UtcNow.AddDays(7);

        var code = flow.IssueInvitation(
            new InvitationPayload(
                request.CompanyId,
                userId,
                target.NormalizedEmail,
                roles,
                expiresAtUtc)
            {
                IssuerSessionKey = owner.User.SessionKey
            });

        return Ok(new
        {
            code,
            expiresAtUtc,
            allowedRoles = roles
        });
    }

    // Permite acceptarea unei invitații și după onboardingul inițial.
    [HttpPost("accept-invitation")]
    [EnableRateLimiting("onboarding")]
    [RequestSizeLimit(12000)]
    public async Task<IActionResult> Accept(
        AcceptPeopleInvitationRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!request.AcceptInvitation)
        {
            return BadRequest(new
            {
                message = "Confirm that you want to join."
            });
        }

        var person = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == userId && p.IsActive,
                ct);

        if (person is null)
            return Unauthorized();

        var invite = flow.ReadInvitation(
            request.InvitationCode);

        if (invite is null
            || invite.NormalizedEmail != person.NormalizedEmail)
        {
            return BadRequest(new
            {
                message = "This invitation is invalid, expired or issued for another account."
            });
        }

        if (!OnboardingFlowService.CanAssign(
            request.Role,
            invite.AllowedRoles))
        {
            return Forbid();
        }

        try
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // Invitația trebuie să provină de la un Owner încă activ.
            var owner = await database.CompanyMembers
                .AsNoTracking()
                .Include(m => m.Company)
                .SingleOrDefaultAsync(m =>
                    m.CompanyId == invite.CompanyId
                    && m.UserId == invite.IssuedByUserId
                    && m.IsActive
                    && m.User.IsActive
                    && m.Role == CompanyRole.Owner
                    && m.User.SessionKey == invite.IssuerSessionKey,
                    ct);

            if (owner is null
                || invite.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    message = "This invitation is no longer valid."
                });
            }

            var existing = await database.CompanyMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(m =>
                    m.CompanyId == invite.CompanyId
                    && m.UserId == userId,
                    ct);

            if (existing is not null)
            {
                // Un retry poate returna apartenența existentă.
                // Invitația nu poate modifica un rol deja atribuit.
                if (!existing.IsActive
                    || existing.Role.ToString() != request.Role)
                {
                    return Conflict(new
                    {
                        message = "Existing membership was not changed. Contact the Owner."
                    });
                }

                return Ok(new WorkspaceRoleResponse(
                    owner.CompanyId,
                    owner.Company.Name,
                    existing.Role.ToString()));
            }

            var role = Enum.Parse<CompanyRole>(request.Role);

            database.CompanyMembers.Add(new CompanyMember
            {
                UserId = userId,
                CompanyId = invite.CompanyId,
                Role = role
            });

            // Pentru un cont nou, acceptarea finalizează configurarea.
            // Pentru unul existent, păstrăm înregistrarea anterioară.
            if (!await database.OnboardingRecords.AnyAsync(
                r => r.UserId == userId,
                ct))
            {
                database.OnboardingRecords.Add(
                    new OnboardingRecord
                    {
                        UserId = userId,
                        CompanyId = invite.CompanyId,
                        RecommendedRole = request.Role,
                        ModelVersion = "people-invitation-v1"
                    });
            }

            await database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Ok(new WorkspaceRoleResponse(
                owner.CompanyId,
                owner.Company.Name,
                request.Role));
        }
        catch (DbUpdateException error)
            when (error.InnerException is SqlException sql
                && sql.Number is 2601 or 2627 or 1205)
        {
            return Conflict(new
            {
                message = "Another request was processed. Try again."
            });
        }
        catch (SqlException error)
            when (error.Number == 1205)
        {
            return Conflict(new
            {
                message = "Another request is being processed. Try again."
            });
        }
    }
}