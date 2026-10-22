using Balancer.Models;
using Microsoft.EntityFrameworkCore;

namespace Balancer.Data;

// DbContext reprezintă legătura cu baza de date.
public class ApplicationDbContext : DbContext
{
    // Configurarea conexiunii va fi primită din Program.cs.
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options
    ) : base(options)
    {
    }

    // Reprezintă tabelul Users.
    public DbSet<User> Users => Set<User>();

    // Tabelul firmelor.
    public DbSet<Company> Companies => Set<Company>();

    // Tabelul care leagă utilizatorii de firme și roluri.
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    // Finalizările chestionarului inițial.
    public DbSet<OnboardingRecord> OnboardingRecords
        => Set<OnboardingRecord>();
    // Configurează coloanele și regulile tabelului.
    public DbSet<DirectConversation> DirectConversations
    => Set<DirectConversation>();

    public DbSet<DirectMessage> DirectMessages
        => Set<DirectMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureMessaging();
        modelBuilder.Entity<OnboardingRecord>(entity =>
        {
            // Un utilizator poate finaliza onboardingul o singură dată.
            entity.HasKey(record => record.UserId);

            entity.Property(record => record.RecommendedRole)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(record => record.ModelVersion)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<OnboardingRecord>(record => record.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(record => record.Company)
                .WithMany()
                .HasForeignKey(record => record.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);

            // Limitele câmpurilor profilului public.
            entity.Property(user => user.JobTitle)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.Bio)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(user => user.Skills)
                .HasMaxLength(200)
                .IsRequired();

            // Conturile existente vor putea apărea în People.
            // Fiecare utilizator poate dezactiva ulterior vizibilitatea.
            entity.Property(user => user.IsDiscoverable)
                .HasDefaultValue(true);

            entity.Property(user => user.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(user => user.Email)
                .HasMaxLength(254)
                .IsRequired();

            entity.Property(user => user.NormalizedEmail)
                .HasMaxLength(254)
                .IsRequired();

            // Indexul unic protejează inclusiv împotriva a două
            // înregistrări simultane cu același email.
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique();

            entity.Property(user => user.PasswordHash)
                .IsRequired();
        });
        // Configurăm tabelul firmelor.
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);

            entity.Property(company => company.Name)
                .HasMaxLength(200)
                .IsRequired();
        });

        // Configurăm relația dintre utilizatori și firme.
        modelBuilder.Entity<CompanyMember>(entity =>
        {
            // Cheie primară compusă:
            // aceeași pereche utilizator-firmă poate apărea o singură dată.
            entity.HasKey(member => new
            {
                member.UserId,
                member.CompanyId
            });

            entity.HasOne(member => member.User)
    // Un utilizator poate avea mai multe apartenențe la firme.
             .WithMany(user => user.Memberships)
            .HasForeignKey(member => member.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // O firmă poate avea mai mulți membri.
            entity.HasOne(member => member.Company)
                .WithMany(company => company.Members)
                .HasForeignKey(member => member.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Salvăm rolul ca text: Owner, Admin, Accountant etc.
            // Astfel este ușor de citit direct în SQL Server.
            entity.Property(member => member.Role)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
        });
    }
}