using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class ToolNavigationTests
{
    [Fact]
    public void LogNavigationLoadsDiagnosticsOnlyWhenSelectedAndPreservesMessages() => MatchListScrollingTests.OnUiThread(() =>
    {
        int created = 0;
        using var messages = new InfoMsgForm();
        using var logs = new LogToolsForm(messages, () => { created++; return new Panel { Name = "diagnosticData" }; });
        using var host = new Form { ClientSize = new Size(1100, 700), ShowInTaskbar = false };
        host.Controls.Add(logs); host.Show();
        var navigation = Children(logs).OfType<AntdUI.Segmented>().Single();
        Assert.True(navigation.Vertical);
        Assert.Equal(0, created);
        Assert.True(messages.Width > 800);
        messages.AddMsg("切换前的日志");
        navigation.SelectIndex = 1;
        SecondPhaseRegressionTests.PumpUntil(() => created == 1);
        messages.AddMsg("诊断页中的后台日志");
        navigation.SelectIndex = 0;
        navigation.SelectIndex = 1;
        Assert.Equal(1, created);
        Assert.True(messages.IsHandleCreated);
    });

    [Fact]
    public void RuneLibraryShowsAppliedHistoryAndExistingClientPages() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Store();
        settings.Value.PersonalRunePresets.Add(new PersonalRunePreset { ChampionId = 122, Name = "已应用方案", Source = "应用记录",
            PrimaryStyleId = 8000, SubStyleId = 8200, RunePerkIds = [8010, 9111, 9104, 8299, 8234, 8236],
            SummonerSpellIds = [4, 12], LastAppliedAt = DateTimeOffset.Now });
        var builds = SecondPhaseRegressionTests.Service<IOpggBuildApplyService>((method, _) => method.Name == "GetLocalRunePagesAsync"
            ? Task.FromResult<IReadOnlyList<PersonalRunePreset>>([new() { Name = "客户端已存在的符文", Source = "客户端符文页", RunePerkIds = [8010, 9111, 9104, 8299, 8234, 8236] }])
            : throw new InvalidOperationException("Browsing cannot write to the client"));
        var context = SecondPhaseRegressionTests.Service<IAiCoachingService>((_, _) => throw new InvalidOperationException());
        using var form = new RunePresetManagerForm(settings, builds, context, new Assets());
        form.Show();
        SecondPhaseRegressionTests.PumpUntil(() => Children(form).OfType<Label>().Any(label => label.Text == "征服者"));
        Assert.Contains(Children(form).OfType<AntdUI.Label>(), label => label.Text?.Contains("最近应用") == true);
        var navigation = Children(form).OfType<AntdUI.Segmented>().Single();
        navigation.SelectIndex = 3;
        var name = Children(form).OfType<AntdUI.Input>().Single(input => input.Name == "presetName");
        Assert.Equal("客户端已存在的符文", name.Text);
        navigation.SelectIndex = 1;
        Assert.Equal("已应用方案", name.Text);
        string? directory = Environment.GetEnvironmentVariable("TOOLS_UI_PREVIEWS");
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, "rune-library.png"));
        }
    });

    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));
    private sealed class Store : IApplicationSettingsStore
    {
        public AssistantSettings Value = new();
        public AssistantSettings Load() => Value;
        public void Save(AssistantSettings settings) => Value = settings;
        public string GetStoragePath() => "unused";
    }
    private sealed class Assets : IGameAssetService
    {
        public Task<GameAsset?> GetChampionIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetItemIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetRuneIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetSummonerSpellIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<string?> GetRuneDetailsAsync(int id, CancellationToken token = default) => Task.FromResult<string?>(id == 8010 ? "征服者\n符文效果" : "副系符文\n作用说明");
        public Task<string?> GetItemNameAsync(int id, CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task<string?> GetSummonerSpellNameAsync(int id, CancellationToken token = default) => Task.FromResult<string?>(null);
    }
}
