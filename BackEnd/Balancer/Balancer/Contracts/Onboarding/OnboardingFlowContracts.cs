using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.Onboarding;

// Datele configurării inițiale.
// Backendul calculează și verifică rolul efectiv.
public class CompleteOnboardingRequest
{
    [Required]
    [RegularExpression("^(create_workspace|join_workspace)$")]
    public string CompanyIntent { get; set; } = string.Empty;

    [StringLength(200)]
    public string? CompanyName { get; set; }

    [StringLength(4096)]
    public string? InvitationCode { get; set; }

    // Confirmă responsabilitatea pentru workspace-ul din Balancer.
    // Nu reprezintă verificarea juridică a proprietății unei firme.
    public bool ConfirmOwnership { get; set; }

    // Utilizatorul acceptă explicit invitația.
    public bool AcceptInvitation { get; set; }

    [RegularExpression(
        "^(workspace_administration|accounting|project_coordination|assigned_work|unsure)$")]
    public string WorkFocus { get; set; } = "unsure";
}

// Codul este trimis în corpul POST, nu în adresa URL.
public class PreviewOnboardingInvitationRequest
{
    [Required]
    [StringLength(4096)]
    public string InvitationCode { get; set; } = string.Empty;
}

// Proprietarul aprobă rolurile permise prin invitație.
public class CreateOnboardingInvitationRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(4)]
    public string[] AllowedRoles { get; set; } = [];
}

// Apartenența reală confirmată de backend.
public record WorkspaceRoleResponse(
    int CompanyId,
    string CompanyName,
    string Role);

// Recomandarea poate lipsi când finalizarea folosește reguli explicite.
public record OnboardingFlowResponse(
    string Status,
    WorkspaceRoleResponse? Membership,
    OnboardingRecommendation? Recommendation,
    string Message);