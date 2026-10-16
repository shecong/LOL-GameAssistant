using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LOL_GameApi.Lounge;

/// <summary>Anonymous installation credentials, NOT proof of Riot account ownership.</summary>
public sealed class LoungeAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger, UrlEncoder encoder, IOptionsMonitor<LoungeOptions> settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    public const string SchemeName = "LoungeInstallation";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        string token = header[7..];
        if (token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid installation credential"));
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (settings.CurrentValue.BannedSessions.Contains(id, StringComparer.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Session banned"));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
