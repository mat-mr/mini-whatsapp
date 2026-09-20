using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Testcontainers.PostgreSql;
using Xunit;

namespace Chat.Api.Tests;

public class ChatIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithUsername("chat")
        .WithPassword("chat")
        .WithDatabase("chat")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d =>
                        d.ServiceType.Name == "DbContextOptions`1");
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<ChatDb>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));
                });
            });

        // Ensure DB schema
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDb>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.StopAsync();
    }

    [Fact]
    public async Task Alice_sends_message_Bob_receives_it()
    {
        using var client = _factory.CreateClient();

        // Connect Alice
        var aliceConnection = new HubConnectionBuilder()
            .WithUrl($"{client.BaseAddress}hub/chat?user=alice", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.SkipNegotiation = false;
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect()
            .Build();

        // Connect Bob
        var bobConnection = new HubConnectionBuilder()
            .WithUrl($"{client.BaseAddress}hub/chat?user=bob", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.SkipNegotiation = false;
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect()
            .Build();

        // Set up Bob to receive messages
        var receivedMessage = new TaskCompletionSource<dynamic>();
        bobConnection.On<dynamic>("message", msg =>
        {
            receivedMessage.SetResult(msg);
        });

        // Connect both
        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();

        // Alice sends "hello" to Bob
        await aliceConnection.InvokeAsync("Send", "bob", "hello");

        // Wait for Bob to receive
        var result = await receivedMessage.Task;
        Assert.NotNull(result);

        // Result is a JsonElement, access properties safely
        var resultJson = ((System.Text.Json.JsonElement)result);
        Assert.Equal("alice", resultJson.GetProperty("from").GetString());
        Assert.Equal("bob", resultJson.GetProperty("to").GetString());
        Assert.Equal("hello", resultJson.GetProperty("body").GetString());

        // Check history
        var response = await client.GetAsync("/api/messages?user=alice&with=bob");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var messages = System.Text.Json.JsonDocument.Parse(content).RootElement;

        Assert.Single(messages.EnumerateArray());
        var msg = messages[0];
        Assert.Equal("alice", msg.GetProperty("from").GetString());
        Assert.Equal("bob", msg.GetProperty("to").GetString());
        Assert.Equal("hello", msg.GetProperty("body").GetString());

        await aliceConnection.StopAsync();
        await bobConnection.StopAsync();
    }
}
