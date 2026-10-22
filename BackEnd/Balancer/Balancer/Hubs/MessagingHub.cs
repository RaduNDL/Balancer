using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Balancer.Hubs;

// Hubul trimite notificări despre modificări.
// Mesajele și permisiunile sunt procesate prin API-ul HTTP.
[Authorize]
public sealed class MessagingHub : Hub
{
}

public sealed class MessagingUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var id = connection.User?.FindFirstValue(
            ClaimTypes.NameIdentifier);

        var key = connection.User?.FindFirstValue(
            "balancer_session");

        return int.TryParse(id, out var userId)
            && Guid.TryParse(key, out var sessionKey)
                ? Key(userId, sessionKey)
                : null;
    }

    // Identificatorul conexiunii include cheia contului.
    public static string Key(int userId, Guid sessionKey)
    {
        return $"{userId}:{sessionKey:N}";
    }
}