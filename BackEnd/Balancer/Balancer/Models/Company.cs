namespace Balancer.Models;

// Reprezintă firma ale cărei date sunt gestionate în aplicație.
public class Company
{
    // Identificatorul unic al firmei.
    public int Id { get; set; }

    // Numele firmei.
    public string Name { get; set; } = string.Empty;

    // Momentul creării firmei în aplicație.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Legăturile dintre această firmă și utilizatorii ei.
    // Nu reprezintă automat toți angajații din evidența de personal.
    public ICollection<CompanyMember> Members { get; set; }
        = new List<CompanyMember>();
}