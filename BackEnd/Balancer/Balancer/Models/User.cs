namespace Balancer.Models;

// Entitatea care va fi salvată în tabelul Users.
// Nu moștenește IdentityUser.
public class User
{
    // Cheia primară; SQL Server va genera valoarea.
    public int Id { get; set; }
    // Diferă și atunci când baza este recreată și Id-ul numeric se repetă.
    public Guid SessionKey { get; set; } = Guid.NewGuid();

    // Numele afișat în aplicație.
    public string FullName { get; set; } = string.Empty;
    // Informațiile profesionale afișate în People.
    // Ocupația este o descriere personală, nu un rol de acces.
    public string JobTitle { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    // Competențele sunt introduse ca text separat prin virgule.
    public string Skills { get; set; } = string.Empty;

    // Profilul poate fi găsit de utilizatorii autentificați.
    public bool IsDiscoverable { get; set; } = true;

    // Adresa de email este ascunsă implicit.
    public bool ShowEmail { get; set; }

    // Adresa de email în forma introdusă de utilizator.
    public string Email { get; set; } = string.Empty;

    // Varianta folosită pentru căutare și verificarea unicității.
    public string NormalizedEmail { get; set; } = string.Empty;

    // Salvăm doar hashul parolei, niciodată parola originală.
    public string PasswordHash { get; set; } = string.Empty;

    // Permite dezactivarea unui cont.
    public bool IsActive { get; set; } = true;

    // Momentul creării contului, exprimat în UTC.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    // Apartenențele utilizatorului la firme.
    // Fiecare element conține firma, rolul și starea accesului.
    public ICollection<CompanyMember> Memberships { get; set; }
        = new List<CompanyMember>();
}