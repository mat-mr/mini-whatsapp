using Chat.Api.Entities;
using Chat.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.MessageBus;

public class LocalMessageBus : IMessageBus
{
    private readonly IHubContext<ChatHub> _hubContext;

    public LocalMessageBus(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PublishAsync(Message message)
    {
        await _hubContext.Clients.Users(message.To, message.From)
            .SendAsync("message", new
            {
                id = message.Id,
                from = message.From,
                to = message.To,
                body = message.Body,
                sentAt = message.SentAt
            });
    }
}
