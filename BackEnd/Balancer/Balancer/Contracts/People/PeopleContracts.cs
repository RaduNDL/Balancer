using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.People;

// Profilul vizibil celorlalți utilizatori.
// Nu include parole, sesiuni sau apartenențele interne la firme.
public record PersonResponse(
    int Id,
    string FullName,
    string JobTitle,
    string Bio,
    string Skills,
    string? Email);

// Datele pe care utilizatorul le poate modifica despre sine.
public class UpdateProfileRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100)]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Bio { get; set; } = string.Empty;

    [StringLength(200)]
    public string Skills { get; set; } = string.Empty;

    public bool IsDiscoverable { get; set; }

    public bool ShowEmail { get; set; }
}

// Ownerul alege firma și rolurile aprobate pentru persoana selectată.
public class InvitePersonRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(4)]
    public string[] AllowedRoles { get; set; } = [];
}

// Acceptarea invitației necesită o confirmare explicită.
public class AcceptPeopleInvitationRequest
{
    [Required]
    [StringLength(4096)]
    public string InvitationCode { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Admin|Accountant|Manager|Employee)$")]
    public string Role { get; set; } = string.Empty;

    public bool AcceptInvitation { get; set; }
}