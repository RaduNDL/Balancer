using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.Onboarding;

// Acceptăm numai răspunsurile chestionarului.
public class OnboardingRequest
{
    [Required]
    [RegularExpression("^(create_workspace|join_workspace|unsure)$")]
    public string CompanyIntent { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string WorkSummary { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string DecisionScope { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string TeamScope { get; set; } = string.Empty;
}