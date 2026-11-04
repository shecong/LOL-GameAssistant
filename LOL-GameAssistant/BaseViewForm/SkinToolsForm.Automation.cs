using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private bool _champSelect, _retryCatalogRead;
    private int _selectionChampion;

    internal static int LockedChampionId(ChampionSelectionSnapshot? selection)
    {
        if (selection == null) return 0;
        int champion = selection.MyTeam.FirstOrDefault(member => member.CellId == selection.LocalPlayerCellId)?.ChampionId ?? 0;
        if (champion <= 0) return 0;
        var picks = selection.Actions.SelectMany(round => round)
            .Where(action => action.ActorCellId == selection.LocalPlayerCellId && action.Type == "pick").ToArray();
        // Random-pick modes have a team selection without a local pick action.
        return picks.Length == 0 || picks.Any(action => action.Completed) ? champion : 0;
    }

    public async Task ObserveChampionSelectionAsync(ChampionSelectionSnapshot? selection, CancellationToken token = default)
    {
        if (_disposing || !_champSelect || !_core.Enabled) return;
        int champion = LockedChampionId(selection);
        if (champion == _selectionChampion) return;
        _selectionChampion = champion;
        ResetSession();
        if (champion <= 0) return;
        long revision = Interlocked.Read(ref _revision);
        try
        {
            string? model = await _skinResources.GetModelAsync(champion, token);
            if (_disposing || IsDisposed || token.IsCancellationRequested || !_champSelect || revision != Interlocked.Read(ref _revision)) return;
            if (string.IsNullOrWhiteSpace(model)) { _selectionChampion = 0; return; }
            var resources = await _skinResources.GetCatalogAsync(model, token);
            if (_disposing || IsDisposed || token.IsCancellationRequested || !_champSelect || revision != Interlocked.Read(ref _revision)) return;
            if (resources.Count == 0) { _selectionChampion = 0; return; }
            foreach (var skin in resources) _clientSkins[skin.SkinNumber] = skin;
            var entries = resources.Select((skin, index) => new SkinEntry {
                EntryId = $"{index}:-1", Index = index, Gear = -1, Model = model, SkinNum = skin.SkinNumber, Name = skin.Name
            }).ToArray();
            // Client entries only support preselection. Native entry IDs are resolved again in-game.
            _catalog = new SkinCoreReply { Model = model, Session = $"selection:{champion}:{revision}", Entries = entries };
            var remembered = _settings.Load().RememberedHeroSkins.GetValueOrDefault(model);
            SelectSkinInControls(entries.FirstOrDefault(entry => entry.SkinNum == remembered?.SkinNum) ?? entries.FirstOrDefault(), true);
            _current.Text = $"{model} · 英雄已锁定 · {entries.Length} 个皮肤";
            _status.Text = "可提前选择皮肤，进入游戏后自动应用；未选择时沿用已记住的皮肤。";
            _ = UpdatePreviewAsync();
            _ = LoadSkinPopularityAsync();
            UpdateButtons();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_disposing && !IsDisposed && revision == Interlocked.Read(ref _revision))
            {
                _selectionChampion = 0;
                ShowError(error);
            }
        }
    }

    private async Task ReadUntilReadyAsync(CancellationToken token)
    {
        try
        {
            do
            {
                while (_refreshing || _busy) await Task.Delay(100, token);
                token.ThrowIfCancellationRequested();
                await RefreshAsync();
                if (!_retryCatalogRead || token.IsCancellationRequested || !_inProgress || !_core.Enabled) return;
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            } while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
