using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.Auth;

// Datele necesare pentru autentificare.
public class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;
}