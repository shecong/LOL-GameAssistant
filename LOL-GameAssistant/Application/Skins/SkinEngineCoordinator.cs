using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Application.Skins;

/// <summary>Skin pages share engine ownership, never catalog or selection state.</summary>
public sealed class SkinEngineCoordinator(ISkinCoreService memory, ICslolSkinService files,
    IApplicationSettingsStore settings, Func<bool> gameRunning, IResourceSkinService? resources = null) : ISkinEngineCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task SelectAsync(SkinEngineMode mode, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (settings.Load().SkinEngineMode == mode && (mode == SkinEngineMode.Memory ? !files.IsRunning : !memory.Enabled)
                && (mode == SkinEngineMode.File || !files.IsRunning)
                && (mode == SkinEngineMode.Resource || resources?.State.Files.Count is not > 0)) return;
            if (gameRunning()) throw new InvalidOperationException("请先退出对局，再切换换肤模式");
            if (mode == SkinEngineMode.File && !files.IsAvailable) throw new InvalidOperationException("游戏外换肤组件或资源缺失");
            if (mode != SkinEngineMode.Resource && resources != null) await resources.ClearAsync(token).ConfigureAwait(false);
            if (mode != SkinEngineMode.File)
            {
                var stopped = await files.StopAsync().ConfigureAwait(false);
                if (!stopped.Ok || files.IsRunning) throw new InvalidOperationException("游戏外换肤尚未停止：" + stopped.Message);
            }
            await memory.SuspendAsync(token).ConfigureAwait(false);
            var config = settings.Load(); config.SkinEngineMode = mode;
            config.SkinCoreEnabled = false; config.UnlimitedZoomEnabled = false; config.RankBorderAutoApplyEnabled = false;
            settings.Save(config);
        }
        finally { _gate.Release(); }
    }
}

public interface ISkinEngineCoordinator
{
    Task SelectAsync(SkinEngineMode mode, CancellationToken token = default);
}
