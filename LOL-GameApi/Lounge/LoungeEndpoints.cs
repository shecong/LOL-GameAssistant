using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LOL_GameApi.Lounge;

public static class LoungeEndpoints
{
    public static void AddLounge(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<LoungeOptions>().Bind(builder.Configuration.GetSection("Lounge"))
            .Validate(o => o.RetentionDays is >= 1 and <= 90 && o.MaxConnections is >= 1 and <= 10000 &&
                o.MaxConnectionsPerIp is >= 1 and <= 10000 &&
                !string.IsNullOrWhiteSpace(o.DatabasePath), "Invalid lounge configuration").ValidateOnStart();
        builder.Services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            LoungeAuthentication>(LoungeAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 8192;
            o.EnableDetailedErrors = false;
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
            o.KeepAliveInterval = TimeSpan.FromSeconds(15);
            o.MaximumParallelInvocationsPerClient = 1;
        });
        builder.Services.AddSingleton<LoungeStore>();
        builder.Services.AddSingleton<LoungeRoom>();
        builder.Services.AddHostedService<LoungePresenceReaper>();
    }

    public static void MapLounge(this WebApplication app)
    {
        app.MapHub<LoungeHub>("/hubs/lounge").RequireRateLimiting("lounge-connect");
        app.MapDelete("/api/lounge/messages/{id:long}", async (long id, HttpContext context,
            IOptionsMonitor<LoungeOptions> settings, LoungeStore store, IHubContext<LoungeHub> hub) =>
        {
            string expected = settings.CurrentValue.AdminKey;
            string actual = context.Request.Headers["X-Lounge-Admin-Key"].ToString();
            if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual))))
                return Results.Unauthorized();
            await store.DeleteAsync(id, context.RequestAborted);
            await hub.Clients.All.SendAsync("Deleted", id, context.RequestAborted);
            return Results.NoContent();
        }).RequireRateLimiting("lounge-connect");
    }
}
