using Chat.Api.Entities;
using Chat.Api.MessageBus;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Hubs;

public class ChatHub : Hub
{
    private readonly ChatDb _db;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ChatDb db, IMessageBus messageBus, ILogger<ChatHub> logger)
    {
        _db = db;
        _messageBus = messageBus;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"User connected: {Context.UserIdentifier} (ConnectionId: {Context.ConnectionId})");
        await base.OnConnectedAsync();
    }

    public async Task Send(string to, string body)
    {
        var from = Context.User?.FindFirst("sub")?.Value ?? Context.UserIdentifier;
        _logger.LogInformation($"Send called: from={from}, to={to}, body={body}");

        if (string.IsNullOrEmpty(from))
            throw new InvalidOperationException("User not authenticated");

        var message = new Message
        {
            From = from,
            To = to,
            Body = body,
            SentAt = DateTime.UtcNow
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        _logger.LogInformation($"Message saved to DB: {message.Id}");

        await _messageBus.PublishAsync(message);
        _logger.LogInformation($"Message published to bus");
    }
}
