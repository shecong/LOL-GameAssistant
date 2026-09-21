namespace LOL_GameAssistant.Domain.Players;

/// <summary>用户主动收藏的召唤师标识。</summary>
public sealed class FavoritePlayer
{
    public string Puuid { get; set; } = "";
    public string GameName { get; set; } = "";
    public string TagLine { get; set; } = "";
    public string? SummonerLevel { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
