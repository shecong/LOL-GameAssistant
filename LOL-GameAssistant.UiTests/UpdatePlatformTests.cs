using System.Runtime.InteropServices;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class UpdatePlatformTests
{
    [Theory]
    [InlineData(Architecture.X64, "win-x64")]
    [InlineData(Architecture.X86, "win-x86")]
    [InlineData(Architecture.Arm64, "win-arm64")]
    public void ProcessArchitectureSelectsMatchingPackage(Architecture architecture, string runtime)
    {
        Assert.Equal(runtime, UpdatePlatform.ForArchitecture(architecture));
        Assert.Equal("LOL-GameAssistant-" + runtime + ".zip", UpdatePlatform.Package(runtime));
    }

    [Fact]
    public void NativeExecutableHeaderMustMatchCurrentProcessArchitecture()
    {
        UpdatePlatform.ValidateExecutable(Environment.ProcessPath!);
        byte[] binary = File.ReadAllBytes(Environment.ProcessPath!);
        int peOffset = BitConverter.ToInt32(binary, 0x3c);
        ushort incorrectMachine = UpdatePlatform.Current == "win-x86" ? (ushort)0x8664 : (ushort)0x14c;
        BitConverter.GetBytes(incorrectMachine).CopyTo(binary, peOffset + 4);
        string path = Path.Combine(Path.GetTempPath(), "wrong-architecture-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllBytes(path, binary);
            Assert.Throws<InvalidDataException>(() => UpdatePlatform.ValidateExecutable(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnsupportedArchitectureIsRejected() => Assert.Throws<PlatformNotSupportedException>(
        () => UpdatePlatform.ForArchitecture(Architecture.Arm));

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-x86")]
    [InlineData("win-arm64")]
    public void ConfigCannotSelectAnotherArchitecture(string runtime)
    {
        var release = new UpdateRelease(new Version(99, 0, 0), "v99.0.0", "https://github.com", "");
        var manifest = new UpdateManifest(1, "99.0.0", runtime, UpdatePlatform.Package(runtime), 123, new string('a', 64), "sc助手.exe");
        if (runtime == UpdatePlatform.Current) UpdatePackageService.ValidateManifest(manifest, release);
        else Assert.Throws<InvalidDataException>(() => UpdatePackageService.ValidateManifest(manifest, release));
    }
}
