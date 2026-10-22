using System.Security.Cryptography;
using System.Text.Json;
using Balancer.Contracts.Onboarding;
using Microsoft.AspNetCore.DataProtection;

namespace Balancer.Services;

// Aceste informații sunt incluse în codul protejat al invitației.
// Modificarea codului în browser îi invalidează protecția.
public record InvitationPayload(
    int CompanyId,
    int IssuedByUserId,
    string NormalizedEmail,
    string[] AllowedRoles,
    DateTime ExpiresAtUtc)
{
    // Leagă invitația de contul concret care a emis-o.
    public Guid IssuerSessionKey { get; init; }
}

// Permite controllerului să returneze o eroare clară pentru serviciul AI.
public class AiServiceException(
    int statusCode,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class OnboardingFlowService
{
    // Owner nu poate fi atribuit prin invitația de alăturare.
    public static readonly string[] AssignableRoles =
    [
        "Admin",
        "Accountant",
        "Manager",
        "Employee"
    ];

    private readonly IHttpClientFactory _clients;
    private readonly IDataProtector _protector;

    // Python primește aceleași denumiri folosite la antrenare.
    private static readonly JsonSerializerOptions PythonJson =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

    public OnboardingFlowService(
        IHttpClientFactory clients,
        IDataProtectionProvider protection)
    {
        _clients = clients;

        // Scopul separă invitațiile noastre de alte date protejate.
        _protector = protection.CreateProtector(
            "Balancer.OnboardingInvitation.v1");
    }

    public string IssueInvitation(InvitationPayload invitation)
    {
        // Protejăm conținutul și integritatea invitației.
        var json = JsonSerializer.Serialize(invitation);

        return _protector.Protect(json);
    }

    public InvitationPayload? ReadInvitation(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(code.Trim());

            var invitation =
                JsonSerializer.Deserialize<InvitationPayload>(json);

            // Invitația trebuie să fie încă valabilă.
            return invitation is not null
                && invitation.ExpiresAtUtc > DateTime.UtcNow
                    ? invitation
                    : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool CanAssign(
        string role,
        string[] allowedRoles)
    {
        // Sunt necesare ambele condiții:
        // rol recunoscut și aprobare explicită prin invitație.
        return AssignableRoles.Contains(
                role,
                StringComparer.Ordinal)
            && allowedRoles.Contains(
                role,
                StringComparer.Ordinal);
    }

    public async Task<OnboardingRecommendation> PredictAsync(
        OnboardingRequest answers,
        CancellationToken cancellationToken)
    {
        answers.CompanyIntent = answers.CompanyIntent.Trim();
        answers.WorkSummary = answers.WorkSummary.Trim();
        answers.DecisionScope = answers.DecisionScope.Trim();
        answers.TeamScope = answers.TeamScope.Trim();

        if (answers.WorkSummary.Length < 5
            || answers.DecisionScope.Length < 5
            || answers.TeamScope.Length < 5)
        {
            throw new AiServiceException(
                400,
                "Please provide more detail in every answer.");
        }

        try
        {
            var client = _clients.CreateClient("BalancerAI");

            // Trimitem numai caracteristicile folosite de model.
            // Cookie-ul și codul invitației nu ajung la Python.
            using var response = await client.PostAsJsonAsync(
                "predict",
                answers,
                PythonJson,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiServiceException(
                    502,
                    "The AI service could not process these answers.");
            }

            var result = await response.Content
                .ReadFromJsonAsync<OnboardingRecommendation>(
                    PythonJson,
                    cancellationToken);

            string[] labels =
            [
                "Owner",
                "Admin",
                "Accountant",
                "Manager",
                "Employee",
                "NeedsReview"
            ];

            // Validăm răspunsul înainte să îl folosim pentru atribuirea rolului.
            if (result is null
                || !labels.Contains(result.RecommendedRole)
                || !labels.Contains(result.RawPrediction)
                || result.PermissionEffect != "recommendation_only"
                || result.SuggestedModules is null
                || string.IsNullOrWhiteSpace(result.ModelVersion)
                || result.ModelVersion.Length > 100
                || result.NeedsClarification
                    != (result.RecommendedRole == "NeedsReview")
                || result.NextAction != (
                    result.NeedsClarification
                        ? "Clarify"
                        : result.RecommendedRole == "Owner"
                            ? "CreateWorkspace"
                            : "RequestApproval")
                || (result.RecommendedRole == "Owner"
                    && answers.CompanyIntent != "create_workspace"))
            {
                throw new AiServiceException(
                    502,
                    "The AI service returned an invalid response.");
            }

            return result;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceException(
                504,
                "The AI service took too long. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new AiServiceException(
                503,
                "The AI service is unavailable. Please try again later.");
        }
        catch (JsonException)
        {
            throw new AiServiceException(
                502,
                "The AI service returned unreadable data.");
        }
    }
}