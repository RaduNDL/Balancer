using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;

namespace Balancer.Contracts.Companies;

// Datele necesare pentru adăugarea unui utilizator existent.
public class AddCompanyMemberRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    // Rolul este verificat explicit în controller.
    // Nu acceptăm atribuirea rolului Owner prin această operație.
    [Required]
    [StringLength(20)]
    public string Role { get; set; } = "Employee";
}