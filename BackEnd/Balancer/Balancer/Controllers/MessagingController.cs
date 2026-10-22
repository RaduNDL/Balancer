using System.Data;
using System.Globalization;
using System.Security.Claims;
using Balancer.Contracts.Messaging;
using Balancer.Data;
using Balancer.Hubs;
using Balancer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Balancer.Controllers;

[ApiController]
[Route("api/messaging")]
[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MessagingController(
    ApplicationDbContext database,
    IHubContext<MessagingHub> hub,
    ILogger<MessagingController> logger) : ControllerBase
{
    private int? CurrentUserId =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
                ? id
                : null;

    // Rolul din firmă nu este folosit pentru accesul la conversație.
    private static bool IsParticipant(
        DirectConversation conversation,
        int userId)
    {
        return conversation.LowUserId == userId
            || conversation.HighUserId == userId;
    }

    private static ChatMessageResponse MessageDto(
        DirectMessage message)
    {
        return new ChatMessageResponse(
            message.Id.ToString(CultureInfo.InvariantCulture),
            message.ConversationId,
            message.SenderUserId,
            message.ClientMessageId,
            message.Body,
            message.CreatedAtUtc);
    }

    private async Task Notify(DirectConversation conversation)
    {
        try
        {
            var users = await database.Users
                .AsNoTracking()
                .Where(item => item.IsActive
                    && (item.Id == conversation.LowUserId
                        || item.Id == conversation.HighUserId))
                .Select(item => new
                {
                    item.Id,
                    item.SessionKey
                })
                .ToListAsync();

            var keys = users.Select(item =>
                MessagingUserIdProvider.Key(
                    item.Id,
                    item.SessionKey)).ToArray();

            // Notificăm numai conturile celor doi participanți.
            // Corpul mesajului este recitit prin API-ul autorizat.
            await hub.Clients.Users(keys)
                .SendAsync("MessagingChanged");
        }
        catch (Exception error)
        {
            // Mesajul este deja salvat.
            // Un eșec al notificării nu anulează salvarea.
            logger.LogWarning(
                error,
                "Messaging notification could not be delivered.");
        }
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var items = await database.DirectConversations
            .AsNoTracking()
            .Where(item => item.LowUserId == userId
                || item.HighUserId == userId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(200)
            .Select(item => new
            {
                item.Id,
                item.State,
                item.RequestedByUserId,
                item.UpdatedAtUtc,
                item.AcceptedAtUtc,

                OtherName = item.LowUserId == userId
                    ? item.HighUser.FullName
                    : item.LowUser.FullName,

                // Respectăm alegerea persoanei și în lista conversațiilor.
                OtherEmail = item.LowUserId == userId
                    ? (item.HighUser.ShowEmail ? item.HighUser.Email : "")
                    : (item.LowUser.ShowEmail ? item.LowUser.Email : ""),

                OtherActive = item.LowUserId == userId
                    ? item.HighUser.IsActive
                    : item.LowUser.IsActive
            })
            .ToListAsync(ct);

        return Ok(items.Select(item =>
            new ConversationResponse(
                item.Id,
                item.OtherName,
                item.OtherEmail,
                item.State.ToString(),
                item.RequestedByUserId == userId,
                item.State == ConversationState.Pending
                    && item.RequestedByUserId != userId
                    && item.OtherActive,
                item.State == ConversationState.Accepted
                    && item.OtherActive,
                item.AcceptedAtUtc.HasValue,
                item.UpdatedAtUtc)).ToArray());
    }

    [HttpPost("requests")]
    [EnableRateLimiting("chat-requests")]
    public async Task<IActionResult> RequestConversation(
        ConversationRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        // Acceptăm exact o modalitate de identificare a destinatarului.
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var hasUserId = request.RecipientUserId.HasValue;

        if (hasEmail == hasUserId)
        {
            return BadRequest(new
            {
                message = "Choose one recipient."
            });
        }

        var email = request.Email?.Trim().ToUpperInvariant();

        var candidates = database.Users
            .AsNoTracking()
            .Where(item => item.IsActive);

        var other = hasUserId
            ? await candidates.SingleOrDefaultAsync(
                item => item.Id == request.RecipientUserId
                    && item.IsDiscoverable,
                ct)
            : await candidates.SingleOrDefaultAsync(
                item => item.NormalizedEmail == email,
                ct);

        if (other is null)
        {
            return NotFound(new
            {
                message =
                    "The selected account is not available."
            });
        }

        if (other.Id == userId)
        {
            return BadRequest(new
            {
                message = "Choose another person's account."
            });
        }

        var low = Math.Min(userId, other.Id);
        var high = Math.Max(userId, other.Id);

        var existing = await database.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.LowUserId == low
                    && item.HighUserId == high,
                ct);

        if (existing is not null)
        {
            // Refuzul sau blocarea nu pot fi ocolite prin cereri noi.
            if (existing.State is ConversationState.Declined
                or ConversationState.Blocked)
            {
                return Conflict(new
                {
                    message =
                        "This conversation is not available for new requests."
                });
            }

            return Ok(new
            {
                conversationId = existing.Id
            });
        }

        var conversation = new DirectConversation
        {
            LowUserId = low,
            HighUserId = high,
            RequestedByUserId = userId
        };

        database.DirectConversations.Add(conversation);

        try
        {
            await database.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error)
            when (error.InnerException is SqlException sql
                && sql.Number is 2601 or 2627)
        {
            return Conflict(new
            {
                message =
                    "A conversation request already exists. Reload the conversation list."
            });
        }

        await Notify(conversation);

        return StatusCode(201, new
        {
            conversationId = conversation.Id
        });
    }

    [HttpPost("conversations/{id:guid}/decision")]
    [EnableRateLimiting("chat-actions")]
    public async Task<IActionResult> Decide(
        Guid id,
        ConversationDecision request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var conversation = await database.DirectConversations
            .SingleOrDefaultAsync(item => item.Id == id, ct);

        if (conversation is null
            || !IsParticipant(conversation, userId))
        {
            return Forbid();
        }

        if (request.Action == "block")
        {
            // Oricare participant poate opri conversația.
            if (conversation.State == ConversationState.Blocked)
            {
                return Ok(new
                {
                    message = "Conversation blocked."
                });
            }

            conversation.State = ConversationState.Blocked;
            conversation.BlockedByUserId = userId;
        }
        else
        {
            // Expeditorul nu își poate accepta singur cererea.
            if (conversation.RequestedByUserId == userId)
            {
                return Forbid();
            }

            if (conversation.State != ConversationState.Pending)
            {
                return Conflict(new
                {
                    message =
                        "This request has already been handled."
                });
            }

            if (request.Action == "accept")
            {
                var otherId = conversation.LowUserId == userId
                    ? conversation.HighUserId
                    : conversation.LowUserId;

                var otherIsActive = await database.Users.AnyAsync(
                    item => item.Id == otherId && item.IsActive,
                    ct);

                if (!otherIsActive)
                {
                    return Forbid();
                }

                conversation.State = ConversationState.Accepted;
                conversation.AcceptedAtUtc = DateTime.UtcNow;
            }
            else if (request.Action == "decline")
            {
                conversation.State = ConversationState.Declined;
            }
            else
            {
                return BadRequest();
            }
        }

        conversation.UpdatedAtUtc = DateTime.UtcNow;
        conversation.Version = Guid.NewGuid();

        try
        {
            await database.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                message =
                    "The conversation changed. Please try again."
            });
        }

        await Notify(conversation);

        return Ok(new
        {
            message = "Conversation updated."
        });
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<IActionResult> Messages(
        Guid id,
        long? beforeId,
        long? afterId,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        if ((beforeId.HasValue && afterId.HasValue)
            || beforeId <= 0
            || afterId < 0)
        {
            return BadRequest();
        }

        var conversation = await database.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, ct);

        if (conversation is null
            || !IsParticipant(conversation, userId)
            || conversation.AcceptedAtUtc is null)
        {
            return Forbid();
        }

        var query = database.DirectMessages
            .AsNoTracking()
            .Where(item => item.ConversationId == id);

        List<DirectMessage> messages;

        var size = afterId.HasValue ? 100 : 50;

        if (afterId.HasValue)
        {
            // Mesajele noi sunt returnate în ordine crescătoare.
            messages = await query
                .Where(item => item.Id > afterId.Value)
                .OrderBy(item => item.Id)
                .Take(size + 1)
                .ToListAsync(ct);
        }
        else
        {
            if (beforeId.HasValue)
            {
                query = query.Where(
                    item => item.Id < beforeId.Value);
            }

            messages = await query
                .OrderByDescending(item => item.Id)
                .Take(size + 1)
                .ToListAsync(ct);
        }

        var hasMore = messages.Count > size;
        var selected = messages.Take(size).ToList();

        if (!afterId.HasValue)
        {
            selected.Reverse();
        }

        // Blocarea oprește mesajele noi.
        // Istoricul acceptat anterior rămâne accesibil participanților.
        return Ok(new MessagePage(
            selected.Select(MessageDto).ToArray(),
            hasMore));
    }

    [HttpPost("conversations/{id:guid}/messages")]
    [EnableRateLimiting("chat-send")]
    [RequestSizeLimit(12000)]
    public async Task<IActionResult> Send(
        Guid id,
        SendMessageRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId is not int userId)
        {
            return Unauthorized();
        }

        var body = request.Body.Trim();

        if (request.ClientMessageId == Guid.Empty
            || body.Length is < 1 or > 2000)
        {
            return BadRequest(new
            {
                message =
                    "Write a message containing 1 to 2000 characters."
            });
        }

        try
        {
            DirectConversation conversation;
            DirectMessage message;

            // Acordul și salvarea sunt verificate în aceeași tranzacție.
            await using (
                var transaction =
                    await database.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        ct))
            {
                conversation = await database.DirectConversations
                    .SingleOrDefaultAsync(item => item.Id == id, ct)
                    ?? new DirectConversation();

                if (!IsParticipant(conversation, userId)
                    || conversation.State != ConversationState.Accepted)
                {
                    return Forbid();
                }

                var otherId = conversation.LowUserId == userId
                    ? conversation.HighUserId
                    : conversation.LowUserId;

                var otherIsActive = await database.Users.AnyAsync(
                    item => item.Id == otherId && item.IsActive,
                    ct);

                if (!otherIsActive)
                {
                    return Forbid();
                }

                var existing = await database.DirectMessages
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.ConversationId == id
                            && item.SenderUserId == userId
                            && item.ClientMessageId
                                == request.ClientMessageId,
                        ct);

                if (existing is not null)
                {
                    if (existing.Body != body)
                    {
                        return Conflict(new
                        {
                            message =
                                "This message identifier was already used for different text."
                        });
                    }

                    return Ok(MessageDto(existing));
                }

                message = new DirectMessage
                {
                    ConversationId = id,
                    SenderUserId = userId,
                    ClientMessageId = request.ClientMessageId,
                    Body = body
                };

                database.DirectMessages.Add(message);

                conversation.UpdatedAtUtc = DateTime.UtcNow;
                conversation.Version = Guid.NewGuid();

                await database.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }

            await Notify(conversation);

            return Ok(MessageDto(message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                message =
                    "The conversation changed. Please retry your message."
            });
        }
        catch (DbUpdateException error)
            when (error.InnerException is SqlException sql
                && sql.Number is 2601 or 2627 or 1205)
        {
            return Conflict(new
            {
                message =
                    "A concurrent request was processed. Please retry your message."
            });
        }
        catch (SqlException error) when (error.Number == 1205)
        {
            return Conflict(new
            {
                message =
                    "The conversation is busy. Please retry your message."
            });
        }
    }
}