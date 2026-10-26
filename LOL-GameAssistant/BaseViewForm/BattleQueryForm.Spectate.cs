using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class BattleQueryForm
{
    private readonly IPlayerSpectateService _playerSpectateService;
    private readonly UiRequestScope _spectateScope = new();
    private bool _spectatePending;
    private readonly AntdUI.Button _spectateButton = new()
    {
        Name = "TrySpectatePlayer", Text = "尝试观战", Dock = DockStyle.Right, Width = 130, Enabled = false
    };

    private void UpdateSpectateButton()
    {
        _spectateButton.Enabled = !_spectatePending && _queryPlatform.Length == 0 &&
            !string.IsNullOrWhiteSpace(_currentPlayer?.Puuid);
    }

    private async Task TrySpectatePlayerAsync()
    {
        if (_spectatePending || _queryPlatform.Length > 0 || _currentPlayer is not { } player ||
            string.IsNullOrWhiteSpace(player.Puuid)) return;
        using var request = _spectateScope.Begin();
        _spectatePending = true;
        UpdateSpectateButton();
        lblStatus.Text = $"正在请求观战 {player.RiotId}…";
        try
        {
            var result = await _playerSpectateService.LaunchAsync(player.Puuid, request.Token);
            if (!request.IsCurrent || IsDisposed) return;
            string message = $"{player.RiotId}：{result.Message}";
            if (_currentPlayer == player && _queryPlatform.Length == 0) lblStatus.Text = message;
            UiMessage.info(FindForm() ?? Program.GameMain, message);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RuntimeDiagnostics.WriteException(ex);
            if (!IsDisposed) lblStatus.Text = "观战请求异常，请查看诊断日志。";
        }
        finally
        {
            _spectatePending = false;
            if (!IsDisposed) UpdateSpectateButton();
        }
    }
}
