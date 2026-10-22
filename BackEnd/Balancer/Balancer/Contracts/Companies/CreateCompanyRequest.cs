using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.Companies;

// Datele pe care le acceptăm de la frontend.
// Nu acceptăm UserId sau Role: acestea sunt stabilite de backend.
public class CreateCompanyRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}