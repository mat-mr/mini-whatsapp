using Chat.Api;
using Chat.Api.Auth;
using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Api.MessageBus;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Database=chat;Username=chat;Password=chat";

builder.Services.AddDbContext<ChatDb>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, DemoUserIdProvider>();

var kafkaBootstrapServers = builder.Configuration["Kafka:BootstrapServers"];
if (!string.IsNullOrEmpty(kafkaBootstrapServers))
{
    builder.Services.AddScoped<IMessageBus, KafkaMessageBus>();
    builder.Services.AddHostedService<KafkaFanOut>();
}
else
{
    builder.Services.AddScoped<IMessageBus, LocalMessageBus>();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// EF ensure created for demo
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDb>();
    // Migrations come later; for now just ensure the schema exists
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseStaticFiles();
app.MapHub<ChatHub>("/hub/chat");
app.MapMessagesEndpoint();

app.MapGet("/healthz/live", () => Results.Ok("OK"));
app.MapGet("/healthz/ready", (ChatDb db) =>
{
    try
    {
        db.Database.CanConnect();
        return Results.Ok("OK");
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
