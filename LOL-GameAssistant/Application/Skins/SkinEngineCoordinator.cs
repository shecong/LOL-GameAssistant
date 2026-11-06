using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Application.Skins;

/// <summary>Skin pages share engine ownership, never catalog or selection state.</summary>
public sealed class SkinEngineCoordinator(ISkinCoreService memory, ICslolSkinService files,
    IApplicationSettingsStore settings, Func<bool> gameRunning, IResourceSkinService? resources = null,
    IResourceSkinLoader? resourceLoader = null) : ISkinEngineCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event Action? Changed;
    public bool IsEnabled(SkinEngineMode mode) => mode != SkinEngineMode.None && settings.Load().SkinEngineMode == mode
        && (mode != SkinEngineMode.Memory || memory.Enabled);
    public async Task SelectAsync(SkinEngineMode mode, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await SelectCoreAsync(mode, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); Notify(); }
    }
    public async Task SetEnabledAsync(SkinEngineMode mode, bool enabled, CancellationToken token = default)
    {
        if (mode == SkinEngineMode.None || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (enabled)
            {
                await SelectCoreAsync(mode, token).ConfigureAwait(false);
                if (mode == SkinEngineMode.Memory)
                {
                    memory.SetEnabled(true);
                    var config = settings.Load(); config.SkinCoreEnabled = true; settings.Save(config);
                }
            }
            else
            {
                // Turning an inactive page off must never disable the active page.
                if (settings.Load().SkinEngineMode != mode) return;
                // Stopping CSLOL only stops our overlay process; allow recovery even when the game has no window.
                if (gameRunning() && mode == SkinEngineMode.Resource) throw new InvalidOperationException("请先退出对局，再关闭此换肤功能");
                if (mode == SkinEngineMode.Resource) await StopResourcesAsync(token).ConfigureAwait(false);
                if (mode == SkinEngineMode.File) await StopFilesAsync().ConfigureAwait(false);
                await memory.SuspendAsync(token).ConfigureAwait(false);
                SaveMode(SkinEngineMode.None);
            }
        }
        finally { _gate.Release(); Notify(); }
    }
    private async Task SelectCoreAsync(SkinEngineMode mode, CancellationToken token)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var current = settings.Load().SkinEngineMode;
        bool otherEnginesOff = (mode == SkinEngineMode.Memory || !memory.Enabled)
            && (mode == SkinEngineMode.File || !files.IsRunning)
            && (mode == SkinEngineMode.Resource || resources?.State.Files.Count is not > 0);
        if (current == mode && otherEnginesOff) return;
        // The native feature can be toggled during a match when neither file engine owns resources.
        bool nativeFromOff = mode == SkinEngineMode.Memory && current == SkinEngineMode.None && otherEnginesOff;
        if (gameRunning() && !nativeFromOff) throw new InvalidOperationException("请先退出对局，再切换换肤模式");
        if (mode == SkinEngineMode.File && !files.IsAvailable) throw new InvalidOperationException("游戏外换肤组件或资源缺失");
        if (mode != SkinEngineMode.Resource)
        {
            if (nativeFromOff && gameRunning())
            {
                // Preserve native toggling during a match without touching installed files.
                if (resources != null) await resources.ClearAsync(token).ConfigureAwait(false);
            }
            else await StopResourcesAsync(token).ConfigureAwait(false);
        }
        if (mode != SkinEngineMode.File) await StopFilesAsync().ConfigureAwait(false);
        await memory.SuspendAsync(token).ConfigureAwait(false);
        SaveMode(mode);
    }
    private async Task StopResourcesAsync(CancellationToken token)
    {
        var games = new[] { resources?.State.GameDirectory, settings.Load().CslolGamePath }
            .Where(game => !string.IsNullOrWhiteSpace(game)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (resources != null) await resources.ClearAsync(token).ConfigureAwait(false);
        if (resourceLoader != null)
            foreach (var game in games) await resourceLoader.DisableAsync(game!, token).ConfigureAwait(false);
    }
    private async Task StopFilesAsync()
    {
        var stopped = await files.StopAsync().ConfigureAwait(false);
        if (!stopped.Ok || files.IsRunning) throw new InvalidOperationException("游戏外换肤尚未停止：" + stopped.Message);
    }
    private void SaveMode(SkinEngineMode mode)
    {
        var config = settings.Load(); config.SkinEngineMode = mode;
        config.SkinCoreEnabled = false; config.UnlimitedZoomEnabled = false; config.RankBorderAutoApplyEnabled = false;
        settings.Save(config);
    }
    private void Notify()
    {
        if (Changed == null) return;
        foreach (Action listener in Changed.GetInvocationList()) { try { listener(); } catch { /* A disposed view must not interrupt engine cleanup. */ } }
    }
}

public interface ISkinEngineCoordinator
{
    event Action? Changed;
    bool IsEnabled(SkinEngineMode mode);
    Task SetEnabledAsync(SkinEngineMode mode, bool enabled, CancellationToken token = default);
    Task SelectAsync(SkinEngineMode mode, CancellationToken token = default);
}
