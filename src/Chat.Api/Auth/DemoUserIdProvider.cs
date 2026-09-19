using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Auth;

/// <summary>
/// Demo-only user ID provider that reads from the ?user query string.
/// DO NOT USE IN PRODUCTION.
/// </summary>
public class DemoUserIdProvider : IUserIdProvider
{
    public virtual string? GetUserId(HubConnectionContext connection)
    {
        return connection.GetHttpContext()?.Request.Query["user"].ToString();
    }
}
