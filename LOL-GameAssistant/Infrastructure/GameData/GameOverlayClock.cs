using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>The v2 native protocol uses Win32 uptime, independent of the .NET runtime's clock.</summary>
internal static class GameOverlayClock
{
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ulong GetTickCount64();

    internal static long Now => (long)GetTickCount64();

    // Panel deadlines originate in the managed clock. Transfer the remaining
    // duration, never an absolute timestamp from a different clock domain.
    internal static long Deadline(long managedDeadline, long managedNow, long nativeNow)
    {
        if (managedDeadline <= 0) return 0;
        long remaining = Math.Max(0, managedDeadline - managedNow);
        return remaining > long.MaxValue - nativeNow ? long.MaxValue : nativeNow + remaining;
    }
}
