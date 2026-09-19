using Chat.Api.Hubs;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Chat.Api.MessageBus;

public class KafkaFanOut : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<KafkaFanOut> _logger;
    private readonly string _topic;

    public KafkaFanOut(IConfiguration config, IHubContext<ChatHub> hubContext, ILogger<KafkaFanOut> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        _topic = config["Kafka:Topic"] ?? "chat-messages";

        var bootstrapServers = config["Kafka:BootstrapServers"];
        if (string.IsNullOrEmpty(bootstrapServers))
            throw new InvalidOperationException("Kafka:BootstrapServers not configured");

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"chat-fanout-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _consumer.Subscribe(_topic);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryCount = 0;
        const int maxRetries = 60;
        const int retryDelayMs = 1000;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(TimeSpan.FromSeconds(5));
                    if (result == null) continue;

                    retryCount = 0;
                    using var doc = JsonDocument.Parse(result.Message.Value);
                    var root = doc.RootElement;

                    var from = root.GetProperty("from").GetString() ?? "";
                    var to = root.GetProperty("to").GetString() ?? "";
                    var body = root.GetProperty("body").GetString() ?? "";
                    var sentAt = root.GetProperty("sentAt").GetString() ?? "";
                    var id = root.GetProperty("id").GetString() ?? "";

                    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;

                    await _hubContext.Clients.Users(to, from)
                        .SendAsync("message", new
                        {
                            id,
                            from,
                            to,
                            body,
                            sentAt
                        }, stoppingToken);

                    _logger.LogInformation($"Fanned out message from {from} to {to}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart && retryCount < maxRetries)
                {
                    retryCount++;
                    _logger.LogWarning($"Topic not ready yet ({retryCount}/{maxRetries}), retrying in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error consuming from Kafka, will retry");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KafkaFanOut fatal error");
        }
        finally
        {
            _consumer.Close();
        }
    }
}
