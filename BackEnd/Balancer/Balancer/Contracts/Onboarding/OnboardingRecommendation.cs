namespace Balancer.Contracts.Onboarding;

// Contractul recomandării primite de la Python.
public class OnboardingRecommendation
{
    public string RecommendedRole { get; set; } = string.Empty;

    public string RawPrediction { get; set; } = string.Empty;

    public bool NeedsClarification { get; set; }

    public string? ClarificationQuestion { get; set; }

    public string[] SuggestedModules { get; set; } = [];

    public string Explanation { get; set; } = string.Empty;

    public string NextAction { get; set; } = string.Empty;

    public string PermissionEffect { get; set; } = string.Empty;

    public string ModelVersion { get; set; } = string.Empty;
}