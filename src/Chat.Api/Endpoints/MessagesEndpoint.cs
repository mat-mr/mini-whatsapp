using Chat.Api;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Endpoints;

public static class MessagesEndpoint
{
    public static void MapMessagesEndpoint(this WebApplication app)
    {
        app.MapGet("/api/messages", GetMessages);
    }

    private static async Task<IResult> GetMessages(
        string user,
        string with,
        ChatDb db)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(with))
            return Results.BadRequest("user and with parameters are required");

        var messages = await db.Messages
            .Where(m =>
                (m.From == user && m.To == with) ||
                (m.From == with && m.To == user))
            .OrderBy(m => m.SentAt)
            .Take(200)
            .ToListAsync();

        return Results.Ok(messages);
    }
}
