using Chat.Api.Entities;
using Confluent.Kafka;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chat.Api.MessageBus;

public class KafkaMessageBus : IMessageBus, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaMessageBus(IConfiguration config)
    {
        _topic = config["Kafka:Topic"] ?? "chat-messages";

        var bootstrapServers = config["Kafka:BootstrapServers"];
        if (string.IsNullOrEmpty(bootstrapServers))
            throw new InvalidOperationException("Kafka:BootstrapServers not configured");

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    public async Task PublishAsync(Message message)
    {
        var key = GetConversationKey(message.From, message.To);
        var value = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = key,
            Value = value
        });
    }

    private static string GetConversationKey(string user1, string user2)
    {
        var sorted = new[] { user1, user2 }.OrderBy(u => u).ToArray();
        return $"{sorted[0]}|{sorted[1]}";
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
