using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameClientExecutablePathsTests
{
    [Fact]
    public void TencentInstallationUsesTclsEvenWhenLeagueClientExists()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string leagueDirectory = Path.Combine(root, "LeagueClient");
            string tclsDirectory = Path.Combine(root, "TCLS");
            Directory.CreateDirectory(leagueDirectory);
            Directory.CreateDirectory(tclsDirectory);
            string league = Path.Combine(leagueDirectory, "LeagueClient.exe");
            string launcher = Path.Combine(tclsDirectory, "client.exe");
            File.WriteAllText(league, "");
            File.WriteAllText(launcher, "");

            Assert.Equal(launcher, GameClientExecutablePaths.FindDirectlyIn(root));
            Assert.Equal(launcher, GameClientExecutablePaths.PreferTencentLauncher(league));
            Assert.Equal(launcher, GameClientExecutablePaths.FindDirectlyIn(tclsDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void RiotInstallationKeepsLeagueClientExecutable()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string leagueDirectory = Path.Combine(root, "League of Legends");
            Directory.CreateDirectory(leagueDirectory);
            string league = Path.Combine(leagueDirectory, "LeagueClient.exe");
            File.WriteAllText(league, "");

            Assert.Equal(league, GameClientExecutablePaths.FindDirectlyIn(leagueDirectory));
            Assert.Equal(league, GameClientExecutablePaths.PreferTencentLauncher(league));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void TencentInstallationWithLeagueClientAtRootUsesAdjacentTcls()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string tclsDirectory = Path.Combine(root, "TCLS");
            Directory.CreateDirectory(tclsDirectory);
            string league = Path.Combine(root, "LeagueClient.exe");
            string launcher = Path.Combine(tclsDirectory, "client.exe");
            File.WriteAllText(league, "");
            File.WriteAllText(launcher, "");

            Assert.Equal(launcher, GameClientExecutablePaths.PreferTencentLauncher(league));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lol-launch-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("lol-launch-path-", StringComparison.Ordinal))
            throw new InvalidOperationException("拒绝清理非测试临时目录。");

        Directory.Delete(fullPath, recursive: true);
    }
}
