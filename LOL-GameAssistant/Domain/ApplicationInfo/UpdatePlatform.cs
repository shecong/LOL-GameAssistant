using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;

namespace LOL_GameAssistant.Domain.ApplicationInfo;

internal static class UpdatePlatform
{
    // 以程序进程架构为准，避免给 64 位系统中运行的 x86 程序下载 x64 包。
    internal static string Current => ForArchitecture(RuntimeInformation.ProcessArchitecture);
    internal static string ForArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => throw new PlatformNotSupportedException("自动更新只支持 Windows x64、x86 和 ARM64 程序。")
    };
    internal static string Package(string runtime) => "LOL-GameAssistant-" + runtime + ".zip";
    internal static string GithubManifest => "update-" + Current + ".json";
    internal static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        Machine expected = Current switch { "win-x64" => Machine.Amd64, "win-x86" => Machine.I386, _ => Machine.Arm64 };
        if (pe.PEHeaders.CoffHeader.Machine != expected)
            throw new InvalidDataException("更新包中的主程序架构与当前程序不一致。");
    }
}

public sealed record UpdateIndex(int SchemaVersion, string Version, string? ReleaseNotes = null);
