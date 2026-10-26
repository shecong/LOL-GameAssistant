namespace LOL_GameAssistant.Application.Skins;

public sealed record ClientRegaliaState(string Session, string CrestType, string BannerType, int PrestigeCrest);
public sealed record ClientRegaliaResult(ClientRegaliaState State, bool Verified, string Message);

public interface IClientRegaliaService
{
    Task<ClientRegaliaState> ReadAsync(CancellationToken token = default);
    Task<ClientRegaliaResult> ApplyAsync(string session, string crestType, CancellationToken token = default);
    Task<ClientRegaliaResult> RestoreAsync(string session, CancellationToken token = default);
}
