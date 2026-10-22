namespace Balancer.Models;

// Starea acordului pentru conversație.
public enum ConversationState
{
    Pending,
    Accepted,
    Declined,
    Blocked
}

public class DirectConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Ordonăm identificatorii pentru a avea o singură pereche posibilă.
    public int LowUserId { get; set; }
    public User LowUser { get; set; } = null!;

    public int HighUserId { get; set; }
    public User HighUser { get; set; } = null!;

    // Utilizatorul care a inițiat cererea.
    public int RequestedByUserId { get; set; }

    public ConversationState State { get; set; }
        = ConversationState.Pending;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public DateTime? AcceptedAtUtc { get; set; }

    public int? BlockedByUserId { get; set; }

    // Detectează modificările concurente ale acordului.
    public Guid Version { get; set; } = Guid.NewGuid();
}