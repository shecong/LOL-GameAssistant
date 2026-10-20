namespace LOL_GameAssistant.Application.LeagueClient;

public interface ICurrentRegionService
{
    Task<string> GetCurrentRegionAsync(CancellationToken token = default);
}
