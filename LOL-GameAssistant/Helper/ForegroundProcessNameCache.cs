using System.Diagnostics;

namespace LOL_GameAssistant.Helper;

/// <summary>Reuse process identity across hotkey and overlay polls; always check the current PID first.</summary>
internal sealed class ForegroundProcessNameCache(Func<int, string?>? resolve = null, Func<long>? clock = null)
{
    private readonly object _gate = new();
    private int _pid;
    private string? _name;
    private long _expires;

    internal string? Get(int pid)
    {
        if (pid <= 0) return null;
        lock (_gate)
        {
            long now = clock?.Invoke() ?? Environment.TickCount64;
            if (_pid == pid && now < _expires) return _name;
            string? name;
            try
            {
                if (resolve != null) name = resolve(pid);
                else
                {
                    using var process = Process.GetProcessById(pid);
                    name = process.ProcessName;
                }
            }
            catch { name = null; }
            _pid = pid;
            _name = name;
            _expires = now + (name == null ? 250 : 1000);
            return name;
        }
    }
}
