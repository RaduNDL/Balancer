using Balancer.Models;
using Microsoft.EntityFrameworkCore;

namespace Balancer.Data;

public static class MessagingConfiguration
{
    public static void ConfigureMessaging(this ModelBuilder builder)
    {
        builder.Entity<DirectConversation>(entity =>
        {
            entity.HasKey(item => item.Id);

            // Între doi utilizatori există o singură conversație.
            entity.HasIndex(item => new
            {
                item.LowUserId,
                item.HighUserId
            }).IsUnique();

            entity.HasIndex(item => new
            {
                item.HighUserId,
                item.UpdatedAtUtc
            });

            entity.Property(item => item.State)
                .HasConversion<string>()
                .HasMaxLength(10);

            entity.Property(item => item.Version)
                .IsConcurrencyToken();

            entity.HasOne(item => item.LowUser)
                .WithMany()
                .HasForeignKey(item => item.LowUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.HighUser)
                .WithMany()
                .HasForeignKey(item => item.HighUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("DirectConversations", table =>
            {
                table.HasCheckConstraint(
                    "CK_Conversation_Pair",
                    "[LowUserId] < [HighUserId]");

                table.HasCheckConstraint(
                    "CK_Conversation_Requester",
                    "[RequestedByUserId] = [LowUserId] OR [RequestedByUserId] = [HighUserId]");
            });
        });

        builder.Entity<DirectMessage>(entity =>
        {
            entity.HasKey(item => item.Id);

            entity.Property(item => item.Body)
                .HasMaxLength(2000)
                .IsRequired();

            entity.HasIndex(item => new
            {
                item.ConversationId,
                item.Id
            });

            // Protecție împotriva mesajelor duplicate la retry.
            entity.HasIndex(item => new
            {
                item.ConversationId,
                item.SenderUserId,
                item.ClientMessageId
            }).IsUnique();

            entity.HasOne<DirectConversation>()
                .WithMany()
                .HasForeignKey(item => item.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(item => item.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}