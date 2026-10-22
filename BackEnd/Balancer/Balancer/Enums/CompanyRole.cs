namespace Balancer.Enums;

// Enumeră rolurile pe care un utilizator le poate avea într-o firmă.
// Valorile sunt explicite, ca să rămână stabile în baza de date.
public enum CompanyRole
{
    Owner = 1,
    Admin = 2,
    Accountant = 3,
    Manager = 4,
    Employee = 5
}