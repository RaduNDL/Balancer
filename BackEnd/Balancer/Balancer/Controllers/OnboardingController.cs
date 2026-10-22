using System.Data;
using System.Security.Claims;
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
[Route("api/onboarding")]
[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OnboardingController(
    ApplicationDbContext database,
    OnboardingFlowService flow) : ControllerBase
{
    // Identitatea vine din cookie-ul verificat de ASP.NET Core.
    private int? CurrentUserId =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
                ? id
                : null;

    private async Task<WorkspaceRoleResponse?> ReadResult(
        int userId,
        CancellationToken ct)
    {
        var record = await database.OnboardingRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                ct);

        if (record is null)
        {
            return null;
        }

        // Citim rolul actual din apartenență, nu doar recomandarea istorică.
        var member = await database.CompanyMembers
            .AsNoTracking()
            .Include(item => item.Company)
            .SingleOrDefaultAsync(
                item => item.UserId == userId
                    && item.CompanyId == record.CompanyId
                    && item.IsActive,
                ct);

        return member is null
            ? null
            : new WorkspaceRoleResponse(
                member.CompanyId,
                member.Company.Name,
                member.Role.ToString());
    }

    // GET /api/onboarding/status
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var hasResult = await database.OnboardingRecords
            .AnyAsync(item => item.UserId == userId, ct);

        // Conturile vechi care au deja firme își păstrează accesul.
        var completed = hasResult
            || await database.CompanyMembers.AnyAsync(
                item => item.UserId == userId && item.IsActive,
                ct);

        return Ok(new
        {
            completed,
            hasResult
        });
    }

    // GET /api/onboarding/result
    [HttpGet("result")]
    public async Task<IActionResult> Result(CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var result = await ReadResult(userId, ct);

        if (result is null)
        {
            return NotFound(new
            {
                message = "No active onboarding workspace was found."
            });
        }

        return Ok(result);
    }

    // Păstrăm și endpointul simplu de recomandare.
    [HttpPost("recommend")]
    [EnableRateLimiting("onboarding")]
    [RequestSizeLimit(30000)]
    public async Task<IActionResult> Recommend(
        OnboardingRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await flow.PredictAsync(request, ct));
        }
        catch (AiServiceException error)
        {
            return StatusCode(
                error.StatusCode,
                new { message = error.Message });
        }
    }

    // Ruta absolută asociază invitația firmei pentru care este emisă.
    [HttpPost("/api/companies/{companyId:int}/onboarding-invitations")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> CreateInvitation(
        int companyId,
        CreateOnboardingInvitationRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var isOwner = await database.CompanyMembers.AnyAsync(
            item => item.CompanyId == companyId
                && item.UserId == userId
                && item.IsActive
                && item.Role == CompanyRole.Owner,
            ct);

        if (!isOwner)
        {
            return Forbid();
        }

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
                message =
                    "Select only Admin, Accountant, Manager or Employee."
            });
        }

        var email = request.Email.Trim().ToUpperInvariant();
        var expiresAtUtc = DateTime.UtcNow.AddDays(7);

        var issuerSessionKey = await database.Users
        .Where(item => item.Id == userId)
        .Select(item => item.SessionKey)
        .SingleAsync(ct);

        var code = flow.IssueInvitation(
            new InvitationPayload(
                companyId,
                userId,
                email,
                roles,
                expiresAtUtc)
            {
                IssuerSessionKey = issuerSessionKey
            });

        // Afișăm codul proprietarului.
        // Această metodă nu trimite emailuri.
        return Ok(new
        {
            code,
            expiresAtUtc,
            allowedRoles = roles
        });
    }

    // Verifică invitația fără să creeze apartenența.
    [HttpPost("invitation-preview")]
    [EnableRateLimiting("onboarding")]
    [RequestSizeLimit(12000)]
    public async Task<IActionResult> PreviewInvitation(
        PreviewOnboardingInvitationRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId && item.IsActive,
                ct);

        if (user is null)
        {
            return Unauthorized();
        }

        var invitation = flow.ReadInvitation(
            request.InvitationCode);

        // Codul trebuie să fie valid și emis pentru emailul contului curent.
        if (invitation is null
            || invitation.NormalizedEmail != user.NormalizedEmail)
        {
            return BadRequest(new
            {
                message = "Enter a valid, unexpired invitation issued for your account email."
            });
        }

        var owner = await ReadInvitationOwner(invitation, ct);

        if (owner is null)
        {
            return BadRequest(new
            {
                message = "This invitation is no longer valid."
            });
        }

        // Afișăm numai rolurile recunoscute și permise pentru invitații.
        var allowedRoles = invitation.AllowedRoles
            .Where(role => OnboardingFlowService.AssignableRoles.Contains(
                role,
                StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (allowedRoles.Length == 0)
        {
            return BadRequest(new
            {
                message = "This invitation does not contain a permitted role."
            });
        }

        return Ok(new
        {
            companyId = owner.CompanyId,
            companyName = owner.Company.Name,
            allowedRoles,
            expiresAtUtc = invitation.ExpiresAtUtc
        });
    }

    // Emitentul invitației trebuie să fie încă Owner activ.
    // SessionKey împiedică reutilizarea invitației după recrearea contului.
    private Task<CompanyMember?> ReadInvitationOwner(
        InvitationPayload invitation,
        CancellationToken ct)
    {
        return database.CompanyMembers
            .AsNoTracking()
            .Include(item => item.Company)
            .SingleOrDefaultAsync(
                item => item.CompanyId == invitation.CompanyId
                    && item.UserId == invitation.IssuedByUserId
                    && item.User.SessionKey == invitation.IssuerSessionKey
                    && item.User.IsActive
                    && item.IsActive
                    && item.Role == CompanyRole.Owner,
                ct);
    }

    // Finalizarea folosește reguli explicite.
    // Disponibilitatea sau scorul modelului AI nu blochează configurarea.
    [HttpPost("complete")]
    [EnableRateLimiting("onboarding")]
    [RequestSizeLimit(12000)]
    public async Task<IActionResult> Complete(
        CompleteOnboardingRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId && item.IsActive,
                ct);

        if (user is null)
        {
            return Unauthorized();
        }

        // O retrimitere returnează rezultatul deja salvat.
        if (await database.OnboardingRecords.AnyAsync(
            item => item.UserId == userId,
            ct))
        {
            return await SavedResponse(userId, ct);
        }

        var intent = request.CompanyIntent?.Trim();
        var name = request.CompanyName?.Trim() ?? "";

        if (intent is not ("create_workspace" or "join_workspace"))
        {
            return BadRequest(new
            {
                message = "Choose how you want to get started."
            });
        }

        InvitationPayload? invitation = null;
        CompanyRole? assignedRole;

        if (intent == "create_workspace")
        {
            if (!request.ConfirmOwnership
                || name.Length < 2
                || name.Length > 200)
            {
                return BadRequest(new
                {
                    message = "Enter a company name of 2 to 200 characters and confirm responsibility for the new workspace."
                });
            }

            // Creatorul administrează noul workspace ca Owner.
            assignedRole = CompanyRole.Owner;
        }
        else
        {
            if (!request.AcceptInvitation)
            {
                return BadRequest(new
                {
                    message = "Confirm that you want to join this workspace."
                });
            }

            invitation = flow.ReadInvitation(
                request.InvitationCode);

            if (invitation is null
                || invitation.NormalizedEmail != user.NormalizedEmail)
            {
                return BadRequest(new
                {
                    message = "Enter a valid, unexpired invitation issued for your account email."
                });
            }

            // Backendul transformă activitatea aleasă într-un rol.
            // Alegerea este validă numai dacă invitația permite acel rol.
            assignedRole = request.WorkFocus switch
            {
                "workspace_administration" => CompanyRole.Admin,
                "accounting" => CompanyRole.Accountant,
                "project_coordination" => CompanyRole.Manager,
                "assigned_work" => CompanyRole.Employee,
                _ => null
            };

            if (assignedRole is null)
            {
                return BadRequest(new
                {
                    message = "Choose your main responsibility in this workspace."
                });
            }

            if (!OnboardingFlowService.CanAssign(
                assignedRole.Value.ToString(),
                invitation.AllowedRoles))
            {
                return Ok(new OnboardingFlowResponse(
                    "ApprovalNeeded",
                    null,
                    null,
                    "Your invitation does not permit this role. Ask the Owner to review its approved roles."));
            }
        }

        try
        {
            // Firma, apartenența și finalizarea sunt salvate împreună.
            await using var transaction = await database.Database
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // Protecție pentru două cereri trimise aproape simultan.
            if (await database.OnboardingRecords.AnyAsync(
                item => item.UserId == userId,
                ct))
            {
                return await SavedResponse(userId, ct);
            }

            // Acest flux este pentru configurarea inițială.
            // Nu modificăm rolurile membrilor existenți.
            if (await database.CompanyMembers.AnyAsync(
                item => item.UserId == userId,
                ct))
            {
                return Conflict(new
                {
                    message = "Your account already has a workspace membership. Existing roles were not changed."
                });
            }

            var record = new OnboardingRecord
            {
                UserId = userId,
                RecommendedRole = assignedRole.Value.ToString(),

                // Păstrăm versiunea regulilor în câmpul existent.
                // Valoarea nu pretinde că a fost folosit un model AI.
                ModelVersion = "structured-onboarding-v2"
            };

            Company company;

            if (intent == "create_workspace")
            {
                company = new Company
                {
                    Name = name
                };

                company.Members.Add(new CompanyMember
                {
                    UserId = userId,
                    Role = CompanyRole.Owner
                });

                database.Companies.Add(company);

                // EF Core completează identificatorul după inserare.
                record.Company = company;
            }
            else
            {
                // Revalidăm invitația înainte de salvare.
                if (invitation!.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    return BadRequest(new
                    {
                        message = "This invitation has expired."
                    });
                }

                var owner = await ReadInvitationOwner(
                    invitation,
                    ct);

                if (owner is null)
                {
                    return BadRequest(new
                    {
                        message = "This invitation is no longer valid."
                    });
                }

                company = owner.Company;

                database.CompanyMembers.Add(new CompanyMember
                {
                    UserId = userId,
                    CompanyId = company.Id,
                    Role = assignedRole.Value
                });

                record.CompanyId = company.Id;
            }

            database.OnboardingRecords.Add(record);

            await database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Ok(new OnboardingFlowResponse(
                "Completed",
                new WorkspaceRoleResponse(
                    company.Id,
                    company.Name,
                    assignedRole.Value.ToString()),
                null,
                "Your workspace role has been saved."));
        }
        catch (DbUpdateException error)
            when (error.InnerException is SqlException sql
                && sql.Number is 2601 or 2627 or 1205)
        {
            return Conflict(new
            {
                message = "Another request completed at the same time. Submit again to load the saved result."
            });
        }
        catch (SqlException error) when (error.Number == 1205)
        {
            return Conflict(new
            {
                message = "Another request is being processed. Please try again."
            });
        }
    }


    private async Task<IActionResult> SavedResponse(
        int userId,
        CancellationToken ct)
    {
        var result = await ReadResult(userId, ct);

        if (result is null)
        {
            return Conflict(new
            {
                message =
                    "Onboarding was already completed, but its membership is no longer active. Contact the company Owner."
            });
        }

        return Ok(new OnboardingFlowResponse(
            "Completed",
            result,
            null,
            "Your role was already saved."));
    }
}