namespace Balancer.Models;

// Memorează finalizarea inițială a configurării contului.
// Un utilizator are o singură astfel de înregistrare.
public class OnboardingRecord
{
    // Este și cheia primară.
    // Împiedică finalizarea de două ori pentru același utilizator.
    public int UserId { get; set; }

    // Workspace-ul în care a fost atribuit rolul inițial.
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    // Păstrăm recomandarea pentru evidența finalizării.
    // Rolul efectiv continuă să fie în CompanyMembers.
    public string RecommendedRole { get; set; } = string.Empty;

    public string ModelVersion { get; set; } = string.Empty;

    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}