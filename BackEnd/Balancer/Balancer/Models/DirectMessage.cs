namespace Balancer.Models;

public class DirectMessage
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    // Este extras din autentificare, niciodată ales de frontend.
    public int SenderUserId { get; set; }

    // Este reutilizat când retrimitem aceeași cerere după o eroare.
    // Indexul unic împiedică salvarea mesajului de două ori.
    public Guid ClientMessageId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;
}