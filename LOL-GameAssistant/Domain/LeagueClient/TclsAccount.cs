namespace LOL_GameAssistant.Domain.LeagueClient;

public sealed class TclsAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string Region { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public override string ToString() => $"{Name} · {Region}";
}
