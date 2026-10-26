using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.LeagueClient;

public interface ITclsAccountStore
{
    IReadOnlyList<TclsAccount> Load();
    TclsAccount Save(string? id, string name, string username, string region, string password);
    void Delete(string id);
    string ReadPassword(string id);
}
