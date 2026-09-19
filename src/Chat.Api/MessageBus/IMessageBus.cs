using Chat.Api.Entities;

namespace Chat.Api.MessageBus;

public interface IMessageBus
{
    Task PublishAsync(Message message);
}
