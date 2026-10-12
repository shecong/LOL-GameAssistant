using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Friends;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

/// <summary>显式设置 LOL_DPI_ARTIFACTS 后在当前真实 Windows 缩放下执行；不模拟 DPI。</summary>
[Collection("Window theme")]
public sealed class RealDpiAcceptanceTests
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [Fact]
    public void CaptureCurrentMonitorPagesInBothThemesAndLanguages()
    {
        string? output = Environment.GetEnvironmentVariable("LOL_DPI_ARTIFACTS");
        if (string.IsNullOrEmpty(output)) return;
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        MatchListScrollingTests.OnUiThread(() =>
        {
            var observations = new List<object>();
            var matches = Service<IMatchHistoryService>((m, _) => m.Name == "GetDetailAsync"
                ? Task.FromResult<MatchDetail?>(Detail()) : Task.FromResult<MatchHistoryResponse?>(new() { Games = new() }));
            var assets = Service<IGameAssetService>((m, _) => m.ReturnType == typeof(Task<GameAsset>)
                ? Task.FromResult<GameAsset?>(null) : Task.FromResult<string?>(null));
            var profiles = Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(Player("验收玩家")));
            var icons = Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null));
            var ranked = Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null));
            var factories = new Dictionary<string, Func<Control>>
            {
                ["home"] = () => CreateHome(profiles),
                ["battle-query"] = () => new BattleQueryForm(profiles, icons, matches, ranked,
                    Service<IFavoritePlayerStore>((m, _) => m.Name == "Load" ? new List<FavoritePlayer>() : null), Service<ITextExportService>((_, _) => Task.CompletedTask)),
                ["friends"] = () => new FriendsForm(Service<IFriendDirectoryService>((_, _) => Task.FromResult<IReadOnlyList<FriendProfile>>([
                    new("friend", "很长的好友名称用于验证文字省略和按钮布局#CN", FriendPresence.InGame, "测试", 0, "极地大乱斗", 1),
                    new("other", "离线好友#测试", FriendPresence.Offline, null, 0, null, null)])),
                    Service<IFriendSpectateService>((_, _) => throw new InvalidOperationException()), icons),
                ["settings"] = () => new SettingForm(Service<IGameClientLauncher>((m, _) => m.Name == "NormalizeConfiguredDirectory" ? "" : null),
                    Service<IApplicationSettingsStore>((m, _) => m.Name == "Load" ? new AssistantSettings() : m.Name == "GetStoragePath" ? "acceptance-settings.json" : null),
                    Service<ISettingsSecretProtector>((_, _) => ""), Service<IClientFeatureService>((_, _) => Task.FromResult(ClientFeatureResult.Success("验收")))),
                ["match-detail"] = () => new MatchDetailForm(Detail(), "验收玩家", assets, matches,
                    Service<IPremadeDetectionService>((_, _) => Task.FromResult(new PremadeDetectionResult([]))))
            };
            string previousTheme = UiTheme.Mode;
            try
            {
                foreach (var (name, factory) in factories)
                {
                    UiTheme.SetMode("Light");
                    AntdUI.Config.IsDark = false;
                    UiLanguage.SetMode("zh-CN");
                    using Control page = factory();
                    using var container = page is Form ? null : new AntdUI.Window { Text = "DPI acceptance · " + name, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new(96, 96), ClientSize = new Size(1100, 760) };
                    Form window = page as Form ?? container!;
                    if (container != null) { page.Dock = DockStyle.Fill; container.Controls.Add(page); }
                    if (container != null) container.ClientSize = new Size(1100 * page.DeviceDpi / 96, 760 * page.DeviceDpi / 96);
                    window.StartPosition = FormStartPosition.CenterScreen;
                    AntdWindowChrome.Configure((AntdUI.Window)window);
                    window.Show();
                    if (container != null)
                        container.ClientSize = new Size(1100 * window.DeviceDpi / 96, 760 * window.DeviceDpi / 96);
                    System.Windows.Forms.Application.DoEvents();
                    uint dpi = GetDpiForWindow(window.Handle);
                    if (int.TryParse(Environment.GetEnvironmentVariable("LOL_DPI_EXPECTED"), out int expected)) Assert.Equal(expected, (int)dpi);
                    Assert.Equal((int)dpi, page.DeviceDpi);
                    Assert.True(dpi >= 96);
                    Assert.True(Screen.FromControl(window).WorkingArea.Contains(window.Bounds), $"{name}: bounds {window.Bounds}, working area {Screen.FromControl(window).WorkingArea}");
                    string directory = Path.Combine(output, dpi.ToString());
                    Directory.CreateDirectory(directory);
                    foreach (string theme in new[] { "Light", "Dark" })
                    foreach (string language in new[] { "zh-CN", "en-US" })
                    {
                        UiTheme.SetMode(theme);
                        AntdUI.Config.IsDark = UiTheme.Palette.IsDark;
                        UiTheme.Apply(window);
                        UiLanguage.SetMode(language);
                        UiLanguage.Refresh();
                        window.PerformLayout();
                        System.Windows.Forms.Application.DoEvents();
                        if (page is BattleQueryForm)
                        {
                            var filter = Field<AntdUI.Select>(page, "_modeSelect");
                            Assert.True(filter.Parent!.Parent!.ClientRectangle.Contains(filter.Parent.Bounds), "模式筛选器必须位于标题区域内");
                            Assert.True(filter.Parent.ClientRectangle.Contains(filter.Bounds), "模式筛选器不能被容器裁剪");
                        }
                        using var bitmap = new Bitmap(window.Width, window.Height);
                        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                        bitmap.Save(Path.Combine(directory, $"{name}-{theme}-{language}.png"), ImageFormat.Png);
                        observations.Add(new { Page = name, Dpi = dpi, Scale = dpi / 96d, Theme = theme, Language = language,
                            Bounds = window.Bounds.ToString(), WorkingArea = Screen.FromControl(window).WorkingArea.ToString(), ControlDpi = page.DeviceDpi });
                    }
                    window.Close();
                }
                string dpiFolder = Path.Combine(output, observations.Count > 0 ? ((JsonElement)JsonSerializer.SerializeToElement(observations[0])).GetProperty("Dpi").GetUInt32().ToString() : "unknown");
                File.WriteAllText(Path.Combine(dpiFolder, "observations.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { UiTheme.SetMode(previousTheme); UiLanguage.SetMode("zh-CN"); }
        });
    }
    private static MatchDetail Detail() => new()
    {
        GameId = 1, gameDuration = 1200, gameMode = "ARAM", queueId = "450",
        participants = [new() { participantId = 1, teamId = 100, championId = 1, stats = new() { champLevel = 18, kills = 10, assists = 20, deaths = 5, Win = true } }],
        participantIdentities = [new() { participantId = 1, player = new() { Puuid = "验收玩家", GameName = "验收玩家", TagLine = "测试" } }]
    };
}
