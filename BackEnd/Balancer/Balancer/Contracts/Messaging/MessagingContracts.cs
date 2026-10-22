using System.ComponentModel.DataAnnotations;

namespace Balancer.Contracts.Messaging;

// Datele trimise pentru solicitarea unei conversații.
// Destinatarul este căutat după adresa de email.
// Destinatarul poate fi indicat prin email sau selectat din People.
public class ConversationRequest
{
    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }

    [Range(1, int.MaxValue)]
    public int? RecipientUserId { get; set; }
}

// Acțiunea efectuată asupra solicitării sau conversației.
// Controllerul verifică separat cine are dreptul să o execute.
public class ConversationDecision
{
    [Required]
    [RegularExpression("^(accept|decline|block)$")]
    public string Action { get; set; } = string.Empty;
}

// Datele trimise când utilizatorul expediază un mesaj.
public class SendMessageRequest
{
    // Identificator generat de frontend.
    // Permite recunoașterea unei retrimiteri a aceluiași mesaj.
    public Guid ClientMessageId { get; set; }

    // Textul mesajului, limitat la 2.000 de caractere.
    [Required]
    [StringLength(2000)]
    public string Body { get; set; } = string.Empty;
}

// Informațiile afișate pentru o conversație.
// Permisiunile sunt calculate de backend pentru utilizatorul autentificat.
public record ConversationResponse(
    Guid Id,
    string OtherUserName,
    string OtherUserEmail,
    string State,
    bool IsRequester,
    bool CanRespond,
    bool CanSend,
    bool HasHistory,
    DateTime UpdatedAtUtc
);

// Mesajul returnat după salvare sau la încărcarea istoricului.
// Id este text pentru a evita pierderea preciziei numerice în JavaScript.
public record ChatMessageResponse(
    string Id,
    Guid ConversationId,
    int SenderUserId,
    Guid ClientMessageId,
    string Body,
    DateTime CreatedAtUtc
);

// O pagină de mesaje.
// HasMore indică dacă mai există mesaje în direcția cerută.
public record MessagePage(
    ChatMessageResponse[] Items,
    bool HasMore
);