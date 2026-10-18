using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Infrastructure.GameData;

internal static class GameDisplayModeDetector
{
    private static readonly object Gate = new();
    private static long _nextCheck;
    private static bool _native = true;
    internal static bool UseNativeOverlay()
    {
        lock (Gate)
        {
            if (Environment.TickCount64 < _nextCheck) return _native;
            _nextCheck = Environment.TickCount64 + 300;
            _native = true;
            foreach (var process in Process.GetProcessesByName("League of Legends"))
            using (process)
            {
                try
                {
                    if (process.HasExited || process.MainWindowHandle == 0) continue;
                    string? game = Path.GetDirectoryName(process.MainModule?.FileName);
                    if (game != null)
                        foreach (string directory in new[] { game, Path.GetDirectoryName(game) ?? game })
                        {
                            string file = Path.Combine(directory,"Config","game.cfg");
                            if (File.Exists(file) && ParseWindowMode(File.ReadAllText(file)) is { } mode)
                                return _native = mode == 0;
                        }
                    // A decorated window is unambiguously windowed. A monitor-sized
                    // popup alone cannot distinguish borderless from exclusive mode.
                    return _native = (GetWindowLongW(process.MainWindowHandle,-16) & 0x00C00000) == 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            return _native;
        }
    }
    internal static int? ParseWindowMode(string configuration)
    {
        bool general = false;
        foreach (string raw in configuration.Split('\n'))
        {
            string line=raw.Trim().TrimStart('\uFEFF');
            if(line.StartsWith('[')) { general=line.Equals("[General]",StringComparison.OrdinalIgnoreCase); continue; }
            if(!general || line.StartsWith(';') || line.StartsWith('#')) continue;
            string[] pair=line.Split('=',2,StringSplitOptions.TrimEntries);
            if(pair.Length==2 && pair[0].Equals("WindowMode",StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(pair[1].Split(';','#')[0].Trim(),out int mode) && mode is >=0 and <=2) return mode;
        }
        return null;
    }
    [DllImport("user32.dll",ExactSpelling=true)]
    private static extern int GetWindowLongW(nint window,int index);
}
