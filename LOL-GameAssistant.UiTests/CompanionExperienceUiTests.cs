using System.Reflection;
using System.Text.Json;
using LOL.Lounge;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CompanionExperienceUiTests
{
    private sealed class MemoryStore : ICompanionExperienceStore
    {
        internal readonly Dictionary<string, PlayerExperience> Players = new();
        public Task<PlayerExperience> LoadAsync(string player, CancellationToken token = default) =>
            Task.FromResult(JsonSerializer.Deserialize<PlayerExperience>(JsonSerializer.Serialize(Players.GetValueOrDefault(player) ?? new()))!);
        public async Task UpdateAsync(string player, Action<PlayerExperience> update, CancellationToken token = default)
        { var data = await LoadAsync(player, token); update(data); Players[player] = data; }
    }
    private static IChampionCatalog Catalog() => Service<IChampionCatalog>((m, a) => m.Name == "GetDisplayName" ? "安妮" : null);
    private static PlayerGrowthSnapshot Snapshot(string player)
    {
        var games = Enumerable.Range(1, 12).Reverse().Select(i => new GrowthMatch(i, i * 1000, 1, "召唤师峡谷", 1200, i % 2 == 0, 2, 5 + i / 10d, 400, .8) { Deaths = 5 }).ToArray();
        var report = new GrowthReport(games, [], 0, "最近12场 · 测试数据");
        return new(player, report, [new(1, "安妮", true, 32000, 7, true, null)], ["召唤师峡谷"], DateTimeOffset.Now, false, "预览样本，非真实账号");
    }
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    private static void Capture(Control control, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("EXPERIENCE_PREVIEWS"); if (folder == null) return;
        Directory.CreateDirectory(folder); using var image = new Bitmap(control.Width, control.Height); control.DrawToBitmap(image, control.ClientRectangle); image.Save(Path.Combine(folder, name + ".png"));
    }

    [Theory]
    [InlineData(760, "Dark")]
    [InlineData(1200, "Light")]
    public void GrowthActionsFitAndPracticeTargetSavesToQueriedPlayer(int width, string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string old = UiTheme.Mode; try
        {
            UiTheme.SetMode(theme); var storage = new MemoryStore();
            using var view = new PlayerGrowthControl(Service<IPlayerGrowthService>((_, a) => Task.FromResult(Snapshot((string)a[0]!))),
                Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(null)), Catalog(), storage);
            using var host = new Form { ClientSize = new(width, 760), ShowInTaskbar = false, Opacity = 0 };
            host.Controls.Add(view); host.Show(); Pump(view.ShowAsync("player-one", false, "召唤师峡谷")); UiTheme.Apply(view);
            var buttons = Children(view).OfType<AntdUI.Button>().ToArray();
            Assert.Contains("样本总览", Children(view).OfType<AntdUI.Input>().Single(c => c.Name == "growthAnalysis").Text);
            Assert.True(Children(view).OfType<SplitContainer>().Single(c => c.Name == "growthWorkspace").Panel2.Height >= 100);
            Assert.Contains(buttons, b => b.Name == "minuteReview"); Assert.Contains(buttons, b => b.Name == "patchWatch");
            foreach (var button in buttons) Assert.True(button.Right <= button.Parent!.ClientSize.Width);
            var progress = Field<AntdUI.Label>(view, "_goalProgress");
            Assert.True(progress.Visible && progress.Top >= Field<AntdUI.Table>(view, "_table").Bottom);
            Capture(view, $"growth-{width}-{theme}");
            host.BeginInvoke(() =>
            {
                var goal = System.Windows.Forms.Application.OpenForms.OfType<PracticeGoalForm>().Single();
                ((AntdUI.InputNumber)goal.Controls.Find("goalTarget", true).Single()).Value = 6;
                Capture(goal, $"goal-{width}-{theme}");
                ((AntdUI.Button)goal.Controls.Find("saveGoal", true).Single()).PerformClick();
            });
            var edit = (Task)typeof(PlayerGrowthControl).GetMethod("ShowPracticeGoalAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null)!;
            Pump(edit);
            Assert.Equal(6d, storage.Players["player-one"].Goal!.Target); Assert.False(storage.Players.ContainsKey("player-two"));
            host.BeginInvoke(() =>
            {
                var review = System.Windows.Forms.Application.OpenForms.Cast<Form>().Single(f => f.Text.StartsWith("一分钟复盘"));
                Assert.Contains("值得保持", ((AntdUI.Input)review.Controls.Find("minuteReviewText", true).Single()).Text);
                Capture(review, $"minute-{width}-{theme}"); review.Close();
            });
            buttons.Single(b => b.Name == "minuteReview").PerformClick();
            view.ResetPlayer(); Assert.Empty(Field<AntdUI.Label>(view, "_goalProgress").Text ?? "");
        }
        finally { UiTheme.SetMode(old); }
    });

    [Fact]
    public void SwitchingPlayerIgnoresUncooperativePreviousLoadAndClearsGoal() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<PlayerGrowthSnapshot>(); var store = new MemoryStore();
        store.Players["one"] = new() { Goal = new(PracticeMetric.Cs, 5, "召唤师峡谷", null, DateTimeOffset.UtcNow) };
        using var view = new PlayerGrowthControl(Service<IPlayerGrowthService>((_, a) => (string)a[0]! == "one" ? pending.Task : Task.FromResult(Snapshot("two"))),
            Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(null)), Catalog(), store);
        Task first = view.ShowAsync("one", false, "全部"); Pump(view.ShowAsync("two", false, "全部"));
        pending.SetResult(Snapshot("one")); Pump(first);
        Assert.Equal("two", Field<PlayerGrowthSnapshot>(view, "_snapshot").Puuid);
        Assert.DoesNotContain("≥ 5", Field<AntdUI.Label>(view, "_goalProgress").Text);
    });

    [Fact]
    public void ApplyingPresetSavesOnceAndPreservesUnsavedAiConnection() => MatchListScrollingTests.OnUiThread(() =>
    {
        int saves = 0; var settings = new AssistantSettings { Ai = new() { EncryptedApiKey = "secret" } };
        var store = Service<IApplicationSettingsStore>((m, a) =>
        {
            if (m.Name == "GetStoragePath") return "test-settings.json";
            if (m.Name == "Load") return JsonSerializer.Deserialize<AssistantSettings>(JsonSerializer.Serialize(settings));
            if (m.Name == "Save") { settings = (AssistantSettings)a[0]!; saves++; } return null;
        });
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        Field<AntdUI.Input>(form, "_apiKey").Text = "unsaved-key"; Field<AntdUI.Select>(form, "_model").Text = "unsaved-model";
        form.ApplyPreset(AssistantPreset.Aram);
        Assert.Equal(1, saves); Assert.True(settings.MayhemOverlayEnabled); Assert.Equal("secret", settings.Ai.EncryptedApiKey);
        Assert.Equal("unsaved-key", Field<AntdUI.Input>(form, "_apiKey").Text); Assert.Equal("unsaved-model", Field<AntdUI.Select>(form, "_model").Text);
    });

    [Fact]
    public void RecruitmentFiltersExcludeClosedAndExpiredAndRespectStyle() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
        var receive = typeof(LoungeClient).GetMethod("ReceiveRecruitment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 1; i <= 4; i++) receive.Invoke(client, [new LoungeRecruitmentState(i, "owner" + i, "队友" + i,
            new("艾欧尼亚", 420, "不限段位", "辅助", 2, RecruitmentPreferences.Compose(i == 1 ? "认真上分" : "轻松娱乐", false, "", "")), [], i == 3,
            i == 4 ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(20))]);
        using var view = new LoungeForm(client); using var dialog = view.CreateRecruitmentListDialog(); dialog.Show();
        var table = (AntdUI.Table)dialog.Controls.Find("recruitmentResults", true).Single();
        Assert.Equal(2, ((Array)table.DataSource!).Length);
        var filter = (AntdUI.Select)dialog.Controls.Find("recruitmentStyle", true).Single(); filter.SelectedValue = "认真上分";
        Assert.Single(((Array)table.DataSource!).Cast<object>());
        Assert.Contains("队友1", ((Array)table.DataSource!).GetValue(0)!.ToString());
        Capture(dialog, "recruitment-filter");
    });
}
