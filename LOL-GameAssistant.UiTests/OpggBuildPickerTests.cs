using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using System.Reflection;
using LOL_GameAssistant.Domain.Coaching;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class OpggBuildPickerTests
{
    [Fact]
    public void RowApplyConfirmsThatRowInsteadOfSavedSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm();
        form.Show();
        Assert.Equal(1, form.SelectedOption?.Order);
        Children(form).OfType<Button>().Single(b => b.Name == "applyRoute2").PerformClick();
        Assert.Equal(2, form.SelectedOption?.Order);
        Assert.Equal(DialogResult.OK, form.DialogResult);
    });

    [Fact]
    public void EmptyModeCannotApplyAndReturningRestoresSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm();
        form.Show();
        Children(form).OfType<Button>().Single(b => b.Text == "大乱斗").PerformClick();
        Assert.Null(form.SelectedOption);
        Assert.False(Children(form).OfType<Button>().Single(b => b.Text == "应用选中方案").Enabled);
        Assert.Contains(Children(form).OfType<Label>(), l => l.Visible && l.Text.Contains("当前对局没有此模式"));
        Children(form).OfType<Button>().Single(b => b.Text == "全部").PerformClick();
        Assert.Equal(1, form.SelectedOption?.Order);
        Assert.NotEqual(DialogResult.OK, form.DialogResult);
    });

    [Fact]
    public void CompactWindowKeepsStatsVisibleAndListScrollable() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm();
        form.ClientSize = new Size(800, 540);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        var list = Children(form).OfType<FlowLayoutPanel>().Single(p => p.FlowDirection == FlowDirection.TopDown);
        Assert.True(list.VerticalScroll.Visible);
        Assert.False(list.HorizontalScroll.Visible);
        foreach (var button in Children(form).OfType<Button>().Where(b => b.Name.StartsWith("applyRoute")))
        {
            Assert.True(button.Visible);
            Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds));
        }
        string? preview = Environment.GetEnvironmentVariable("OPGG_PICKER_PREVIEW");
        if (!string.IsNullOrEmpty(preview))
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(preview);
        }
    });

    [Fact]
    public void MayhemRetainsAugmentMessageAndDoesNotOfferRuneReplacement() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm("aram_mayhem");
        form.Show();
        Assert.DoesNotContain(Children(form).OfType<CheckBox>(), checkbox => checkbox.Visible);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Visible && label.Text == "暂无增幅样本");
        Assert.Equal("aram_mayhem", form.SelectedOption?.Mode);
    });

    [Fact]
    public void AllLanesLoadRealRecommendationsAndBottomCanBeApplied() => MatchListScrollingTests.OnUiThread(() =>
    {
        var calls = new List<string>();
        var service = BuildService(args =>
        {
            string role = (string)args[1]!;
            calls.Add(role);
            return Task.FromResult(Choices(role));
        });
        using var form = CreateForm(buildService: service);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        foreach (string lane in new[] { "上路", "打野", "中路", "下路", "辅助" })
            Assert.True(Children(form).OfType<Label>().Any(label => label.Text.Contains($"[{lane}]")),
                lane + ": " + string.Join(";", Children(form).OfType<Label>().Where(label => label.Text.StartsWith("方案")).Select(label => label.Text)));
        Assert.Equal(new[] { "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }, calls);
        Children(form).OfType<Button>().Single(button => button.Text == "下路").PerformClick();
        Assert.Equal("BOTTOM", form.SelectedPosition);
        Assert.DoesNotContain(Children(form).OfType<Label>(), label => label.Text.Contains("[中路]"));
        Children(form).OfType<Button>().Single(button => button.Name == "applyRoute1").PerformClick();
        Assert.Equal(DialogResult.OK, form.DialogResult);
        Assert.Equal("BOTTOM", form.SelectedPosition);
    });

    [Fact]
    public void TypeSwitchLoadsAramAndPreventsApplyingItToCurrentRiftMatch() => MatchListScrollingTests.OnUiThread(() =>
    {
        var service = BuildService(args => Task.FromResult(Choices((string)args[1]!,
            ((OpggBuildRequest)args[2]!).GameMode == "ARAM" ? "aram" : "ranked")));
        using var form = CreateForm(buildService: service);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        Children(form).OfType<Button>().Single(button => button.Text == "大乱斗").PerformClick();
        Assert.Equal("aram", form.SelectedOption?.Mode);
        Assert.False(Children(form).OfType<Button>().Single(button => button.Text == "应用选中方案").Enabled);
        Assert.All(Children(form).OfType<Button>().Where(button => button.Name.StartsWith("applyRoute")),
            button => Assert.False(button.Enabled));
        Assert.All(Children(form).OfType<Button>().Where(button => button.Text is "上路" or "下路" or "中路"),
            button => Assert.False(button.Enabled));
        Children(form).OfType<Button>().Single(button => button.Text == "峡谷").PerformClick();
        Assert.Equal("ranked", form.SelectedOption?.Mode);
        Assert.True(Children(form).OfType<Button>().Single(button => button.Text == "应用选中方案").Enabled);
    });

    [Fact]
    public void SlowPreviousLaneCannotReplaceLatestSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var top = new TaskCompletionSource<OpggBuildChoices>();
        var bottom = new TaskCompletionSource<OpggBuildChoices>();
        var service = BuildService(args => (string)args[1]! == "TOP" ? top.Task : bottom.Task);
        using var form = CreateForm("aram", service);
        form.Show();
        Task first = form.ChangeCategoryAsync("ranked", "TOP");
        Assert.Null(form.SelectedOption);
        Task second = form.ChangeCategoryAsync("ranked", "BOTTOM");
        bottom.SetResult(Choices("BOTTOM"));
        System.Windows.Forms.Application.DoEvents();
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal("BOTTOM", form.SelectedPosition);
        top.SetResult(Choices("TOP"));
        System.Windows.Forms.Application.DoEvents();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal("BOTTOM", form.SelectedPosition);
        Assert.DoesNotContain(Children(form).OfType<Label>(), label => label.Text.Contains("[上路]"));
    });

    [Fact]
    public void CachedLaneIsUsableWhileOtherLanesAreStillDownloading() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new Dictionary<string, TaskCompletionSource<OpggBuildChoices>>();
        var service = BuildService(args =>
        {
            string role = (string)args[1]!;
            var response = new TaskCompletionSource<OpggBuildChoices>();
            pending.Add(role, response);
            return response.Task;
        });
        using var form = CreateForm(buildService: service);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(4, pending.Count);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Visible && label.Text.Contains("[上路]"));
        Assert.True(Children(form).OfType<Button>().Single(button => button.Text == "应用选中方案").Enabled);
        Assert.All(pending.Values, response => Assert.False(response.Task.IsCompleted));
        pending["BOTTOM"].SetResult(Choices("BOTTOM"));
        System.Windows.Forms.Application.DoEvents();
        Assert.Contains(Children(form).OfType<Label>(), label => label.Text.Contains("[下路]"));
        Assert.False(pending["JUNGLE"].Task.IsCompleted);
        foreach (var pair in pending) pair.Value.TrySetResult(Choices(pair.Key));
        System.Windows.Forms.Application.DoEvents();
    });

    [Fact]
    public void UnavailableLaneShowsFailureAndAllowsRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        int attempts = 0;
        var service = BuildService(args => Task.FromResult(++attempts == 1
            ? OpggBuildChoices.Failure("此英雄上路暂无数据") : Choices((string)args[1]!)));
        using var form = CreateForm("aram", service);
        form.Show();
        Assert.True(form.ChangeCategoryAsync("ranked", "TOP").IsCompletedSuccessfully);
        Assert.Null(form.SelectedOption);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Visible && label.Text == "此英雄上路暂无数据");
        Assert.True(form.ChangeCategoryAsync("ranked", "TOP").IsCompletedSuccessfully);
        Assert.Equal("TOP", form.SelectedPosition);
        Assert.NotNull(form.SelectedOption);
    });

    private static IOpggBuildApplyService BuildService(Func<object?[], Task<OpggBuildChoices>> fetch)
    {
        var proxy = DispatchProxy.Create<IOpggBuildApplyService, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = (method, args) =>
            method.Name == "GetBuildChoicesAsync" ? fetch(args) : throw new InvalidOperationException("No writes while browsing.");
        return proxy;
    }

    private static OpggBuildChoices Choices(string role, string mode = "ranked") => new(true, "", "德莱厄斯",
        mode == "aram" ? "极地大乱斗" : role switch
        {
            "TOP" => "上路", "JUNGLE" => "打野", "BOTTOM" => "下路", "UTILITY" => "辅助", _ => "中路"
        }, [new OpggBuildOption(1, [1054], [3078, 3053, 6333], [3026], 8000, 8200,
            [8010, 9111, 9104, 8299, 8234, 8236, 5008, 5008, 5001], 1000, 500, Mode: mode)], Mode: mode, ChampionId: 122);

    [Fact]
    public void MissingMatchDefaultsToYasuoRiftMiddle()
    {
        var (context, preview) = CoachForm.ResolveManualBuildContext(new AiGameContext { GameMode = "", QueueId = 999 });
        Assert.True(preview);
        Assert.Equal(157, context.MyChampionId);
        Assert.Equal("CLASSIC", context.GameMode);
        Assert.Equal("MIDDLE", context.MyRole);
        Assert.Equal(0, context.QueueId);
    }

    [Fact]
    public void FallbackRetainsKnownHeroAndMode()
    {
        var (context, preview) = CoachForm.ResolveManualBuildContext(new AiGameContext
            { MyChampionId = 22, GameMode = "ARAM", QueueId = 450 });
        Assert.True(preview);
        Assert.Equal(22, context.MyChampionId);
        Assert.Equal("ARAM", context.GameMode);
    }

    [Fact]
    public void KnownChampSelectKeepsLiveContext()
    {
        var original = new AiGameContext { Phase = "ChampSelect", MyChampionId = 22, MyRole = "BOTTOM" };
        var (context, preview) = CoachForm.ResolveManualBuildContext(original);
        Assert.False(preview);
        Assert.Same(original, context);
    }

    [Fact]
    public void PreviewFallbackDisablesEveryApplyEntry() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(allowApply: false);
        form.Show();
        Assert.All(Children(form).OfType<Button>().Where(button => button.Name.StartsWith("applyRoute") ||
            button.Text == "应用选中方案"), button => Assert.False(button.Enabled));
        Children(form).OfType<Button>().Single(button => button.Text == "全部").PerformClick();
        Assert.False(((Button)form.AcceptButton!).Enabled);
        Assert.NotEqual(DialogResult.OK, form.DialogResult);
    });

    private static OpggBuildPickerForm CreateForm(string mode = "ranked", IOpggBuildApplyService? buildService = null,
        bool allowApply = true)
    {
        var options = Enumerable.Range(1, 4).Select(order => new OpggBuildOption(order,
            [1054, 2003], [3078, 3053, 6333], [3143, 3065, 3071, 3026, 3047, 3082],
            8000, 8200, mode == "aram_mayhem" ? [] : [8010, 9111, 9104, 8299, 8234, 8236, 5008, 5008, 5001],
            7200 / order, 3500 / order, [4, 12], Mode: mode)).ToArray();
        return new OpggBuildPickerForm(new OpggBuildChoices(true, "", "德莱厄斯", "上路", options,
            Mode: mode, ChampionId: 122), new EmptyAssets(), buildService: buildService,
            allowApply: allowApply) { Opacity = 0 };
    }

    private static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));

    private sealed class EmptyAssets : IGameAssetService
    {
        public Task<GameAsset?> GetChampionIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetItemIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetRuneIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetSummonerSpellIconAsync(int id, CancellationToken token = default) => Task.FromResult<GameAsset?>(null);
        public Task<string?> GetItemNameAsync(int id, CancellationToken token = default) => Task.FromResult<string?>($"装备 {id}");
        public Task<string?> GetSummonerSpellNameAsync(int id, CancellationToken token = default) => Task.FromResult<string?>($"技能 {id}");
    }
}
