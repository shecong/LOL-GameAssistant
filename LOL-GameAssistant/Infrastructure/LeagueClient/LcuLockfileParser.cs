using System.Globalization;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Parses name:pid:port:password:protocol without exposing credentials in diagnostics.</summary>
internal static class LcuLockfileParser
{
    internal static bool TryParse(string? content, out string? port, out string? token)
    {
        port = token = null;
        string[] parts = (content ?? "").Trim().Split(':');
        if (parts.Length != 5 || string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(parts[3]) || !parts[4].Equals("https", StringComparison.OrdinalIgnoreCase))
            return false;
        port = number.ToString(CultureInfo.InvariantCulture);
        token = parts[3];
        return true;
    }
}
