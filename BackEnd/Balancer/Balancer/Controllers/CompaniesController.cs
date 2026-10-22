using System.Security.Claims;
using Balancer.Contracts.Companies;
using Balancer.Data;
using Balancer.Enums;
using Balancer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Balancer.Controllers;

// Activează validarea automată a cererilor API.
[ApiController]

// Toate rutele încep cu /api/companies.
[Route("api/companies")]

// Toate metodele necesită un utilizator autentificat.
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly ApplicationDbContext _database;

    // ASP.NET Core furnizează contextul configurat în Program.cs.
    public CompaniesController(ApplicationDbContext database)
    {
        _database = database;
    }

    
    // Exemplu de rută disponibilă doar pentru Owner și Admin
    // în firma identificată prin companyId.
    [HttpGet("{companyId:int}/management")]
    public async Task<IActionResult> GetManagementAccess(
        int companyId,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        // Căutăm apartenența la firma cerută.
        // AsNoTracking este potrivit deoarece doar citim.
        var membership = await _database.CompanyMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => member.CompanyId == companyId
                    && member.UserId == userId
                    && member.IsActive
                    && member.User.IsActive,
                cancellationToken);

        // Autentificarea singură nu oferă acces la orice firmă.
        if (membership is null
            || (membership.Role != CompanyRole.Owner
                && membership.Role != CompanyRole.Admin))
        {
            return Forbid();
        }

        return Ok(new
        {
            companyId,
            role = membership.Role.ToString(),
            message = "You have access to company management."
        });
    }
    // Metodă internă folosită de endpointurile de mai jos.
    // Caută apartenența utilizatorului autentificat la firma cerută.
    private async Task<CompanyMember?> GetCurrentMembershipAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return null;
        }

        return await _database.CompanyMembers
            .AsNoTracking()

            // Încărcăm și firma pentru a putea citi numele ei.
            .Include(member => member.Company)

            .SingleOrDefaultAsync(
                member => member.CompanyId == companyId
                    && member.UserId == userId
                    && member.IsActive
                    && member.User.IsActive,
                cancellationToken);
    }

    // GET /api/companies/1
    // Orice membru activ poate vedea informațiile de bază ale firmei sale.
    [HttpGet("{companyId:int}")]
    public async Task<IActionResult> GetCompany(
        int companyId,
        CancellationToken cancellationToken)
    {
        var membership = await GetCurrentMembershipAsync(
            companyId,
            cancellationToken);

        if (membership is null)
        {
            return Forbid();
        }

        return Ok(new
        {
            CompanyId = membership.CompanyId,
            CompanyName = membership.Company.Name,
            Role = membership.Role.ToString()
        });
    }

    // GET /api/companies/1/members
    // Numai Owner și Admin pot vedea lista membrilor.
    [HttpGet("{companyId:int}/members")]
    public async Task<IActionResult> GetMembers(
        int companyId,
        CancellationToken cancellationToken)
    {
        var membership = await GetCurrentMembershipAsync(
            companyId,
            cancellationToken);

        if (membership is null
            || (membership.Role != CompanyRole.Owner
                && membership.Role != CompanyRole.Admin))
        {
            return Forbid();
        }

        var members = await _database.CompanyMembers
            .AsNoTracking()
            .Where(member =>
                member.CompanyId == companyId
                && member.IsActive
                && member.User.IsActive)
            .OrderBy(member => member.User.FullName)
            .Select(member => new
            {
                member.UserId,
                member.User.FullName,
                member.User.Email,
                member.Role
            })
            .ToListAsync(cancellationToken);

        // Convertim rolurile în text după citirea datelor din SQL Server.
        return Ok(members.Select(member => new
        {
            member.UserId,
            member.FullName,
            member.Email,
            Role = member.Role.ToString()
        }).ToArray());
    }

    // POST /api/companies/1/members
    // Numai Owner poate adăuga membri în această etapă.
    [HttpPost("{companyId:int}/members")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> AddMember(
        int companyId,
        AddCompanyMemberRequest request,
        CancellationToken cancellationToken)
    {
        var membership = await GetCurrentMembershipAsync(
            companyId,
            cancellationToken);

        // Verificăm drepturile în firma specificată prin adresă.
        // Un Owner din altă firmă nu primește acces aici.
        if (membership is null || membership.Role != CompanyRole.Owner)
        {
            return Forbid();
        }

        // Acceptăm strict cele patru roluri prevăzute.
        // Valorile numerice și Owner sunt respinse.
        CompanyRole? assignedRole = request.Role switch
        {
            "Admin" => CompanyRole.Admin,
            "Accountant" => CompanyRole.Accountant,
            "Manager" => CompanyRole.Manager,
            "Employee" => CompanyRole.Employee,
            _ => null
        };

        if (assignedRole is null)
        {
            return BadRequest(new
            {
                message = "Select Admin, Accountant, Manager or Employee."
            });
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // Adăugăm doar un utilizator existent și activ.
        var targetUser = await _database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail
                    && user.IsActive,
                cancellationToken);

        if (targetUser is null)
        {
            return NotFound(new
            {
                message = "No active account was found for this email."
            });
        }

        // Verificăm inclusiv apartenențele inactive.
        // Reactivarea va fi o operație separată.
        var alreadyExists = await _database.CompanyMembers.AnyAsync(
            member => member.CompanyId == companyId
                && member.UserId == targetUser.Id,
            cancellationToken);

        if (alreadyExists)
        {
            return Conflict(new
            {
                message = "This user already has a membership in this company."
            });
        }

        var newMember = new CompanyMember
        {
            CompanyId = companyId,
            UserId = targetUser.Id,
            Role = assignedRole.Value,
            IsActive = true
        };

        _database.CompanyMembers.Add(newMember);

        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                  && sqlException.Number is 2601 or 2627)
        {
            // Cheia compusă oprește și două adăugări simultane
            // ale aceluiași utilizator în aceeași firmă.
            return Conflict(new
            {
                message = "This user already has a membership in this company."
            });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            UserId = targetUser.Id,
            targetUser.FullName,
            targetUser.Email,
            Role = newMember.Role.ToString()
        });
    }
}