using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Switches only between requests. Never falls back or replays a failed write.</summary>
public sealed class SelectableSkinCoreTransport(ISkinCoreTransport classic, ISkinCoreTransport kernelRelay,
    SkinCoreMode initialMode = SkinCoreMode.Classic) : ISkinCoreTransport, ISkinCoreModeSelector
{
    private readonly object _sync = new();
    private SkinCoreMode _mode = Enum.IsDefined(initialMode) ? initialMode : SkinCoreMode.Classic;
    private int _active;
    public SkinCoreMode Mode { get { lock (_sync) return _mode; } }
    public void SetMode(SkinCoreMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new SkinCoreException("invalid_core_mode");
        lock (_sync)
        {
            if (_active != 0) throw new SkinCoreException("core_mode_busy");
            _mode = mode;
        }
    }
    public async Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ISkinCoreTransport target;
        lock (_sync) { target = _mode == SkinCoreMode.KernelRelay ? kernelRelay : classic; _active++; }
        try { return await target.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false); }
        finally { lock (_sync) _active--; }
    }
}
