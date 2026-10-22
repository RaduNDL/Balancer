using Balancer.Enums;

namespace Balancer.Models;

// Reprezintă apartenența unui utilizator la o anumită firmă.
public class CompanyMember
{
    // Identificatorul utilizatorului.
    // Este o cheie externă către tabelul Users.
    public int UserId { get; set; }

    // Proprietate de navigare către utilizator.
    // null! indică faptul că relația va fi completată de EF Core.
    // Nu încarcă automat utilizatorul din baza de date.
    public User User { get; set; } = null!;

    // Identificatorul firmei.
    // Este o cheie externă către tabelul Companies.
    public int CompanyId { get; set; }

    // Proprietate de navigare către firmă.
    public Company Company { get; set; } = null!;

    // Rolul utilizatorului în această firmă.
    // Alegem implicit rolul cu acces limitat.
    public CompanyRole Role { get; set; } = CompanyRole.Employee;

    // Permite suspendarea accesului utilizatorului în această firmă.
    public bool IsActive { get; set; } = true;

    // Momentul în care utilizatorul a devenit membru.
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}