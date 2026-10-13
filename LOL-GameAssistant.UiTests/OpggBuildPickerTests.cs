using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using System.Reflection;
using LOL_GameAssistant.Domain.Coaching;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window resources")]
public sealed class OpggBuildPickerTests
{
    [Fact]
    public void RepeatedModalOpenAndCloseReleasesWindowHandles() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var owner = new Form { Opacity = 0, ShowInTaskbar = false };
        owner.Show();
        uint baseline = 0;
        for (int i = 0; i < 12; i++)
        {
            using (var picker = CreateForm())
            {
                picker.Shown += (_, _) => picker.BeginInvoke((Action)picker.Close);
                Assert.Equal(DialogResult.Cancel, picker.ShowDialog(owner));
            }
            System.Windows.Forms.Application.DoEvents();
            uint handles = GetGuiResources(process.Handle, 1);
            if (i == 0) baseline = handles;
            Assert.True(handles <= baseline + 8, $"Iteration {i}: USER={handles}, baseline={baseline}");
        }
    });

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(220)]
    public void FocusingVisibleRowApplyKeepsButtonUnderPointer(int offset) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm();
        form.ClientSize = new Size(800, 540);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        var list = Children(form).OfType<FlowLayoutPanel>().Single(panel => panel.FlowDirection == FlowDirection.TopDown);
        list.AutoScrollPosition = new Point(0, offset);
        var viewport = list.RectangleToScreen(list.ClientRectangle);
        var button = Children(form).OfType<Button>().First(button => button.Name.StartsWith("applyRoute") &&
            viewport.Contains(button.RectangleToScreen(button.ClientRectangle)));
        var before = button.RectangleToScreen(button.ClientRectangle);
        Assert.True(button.Focus());
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(before, button.RectangleToScreen(button.ClientRectangle));
    });

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
    public void ApplyingWaitsForClientShowsFailureAndAllowsRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        var response = new TaskCompletionSource<OpggBuildApplyResult>();
        int applications = 0;
        using var form = CreateForm(applySelection: (option, position, replace) =>
        {
            Assert.Equal(2, option.Order);
            Assert.Equal("TOP", position);
            Assert.True(replace);
            applications++;
            return applications == 1 ? response.Task : Task.FromResult(OpggBuildApplyResult.Success("已应用"));
        });
        form.Show();
        var row = Children(form).OfType<Button>().Single(button => button.Name == "applyRoute2");
        row.PerformClick();
        Assert.True(form.Visible);
        Assert.Equal(DialogResult.None, form.DialogResult);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Text.Contains("正在应用方案 2"));
        var status = Children(form).OfType<Label>().Single(label => label.Name == "applyStatus");
        Assert.True(status.Visible);
        Assert.Contains("正在应用方案 2", status.Text);
        Assert.False(row.Enabled);
        row.PerformClick();
        Assert.Equal(1, applications);
        response.SetResult(OpggBuildApplyResult.Failure("客户端拒绝写入符文页，请检查客户端。"));
        System.Windows.Forms.Application.DoEvents();
        Assert.True(form.Visible);
        Assert.True(row.Enabled);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Text.Contains("客户端拒绝写入符文页"));
        Assert.Contains("客户端拒绝写入符文页", status.Text);
        row.PerformClick();
        Assert.Equal(2, applications);
        Assert.True(form.Visible);
        Assert.Contains(Children(form).OfType<Label>(), label => label.Name == "applyStatus" &&
            label.Visible && label.Text.Contains("已应用"));
        Assert.False(row.Enabled);
        ((Button)form.AcceptButton!).PerformClick();
        Assert.Equal(2, applications);
        Assert.Equal(DialogResult.OK, form.DialogResult);
    });

    [Fact]
    public void ModalApplyReturnsOkOnlyAfterWritingSelectedRow() => MatchListScrollingTests.OnUiThread(() =>
    {
        int appliedOrder = 0;
        using var form = CreateForm(applySelection: (option, _, _) =>
        {
            appliedOrder = option.Order;
            return Task.FromResult(OpggBuildApplyResult.Success("已应用"));
        });
        form.Shown += (_, _) => form.BeginInvoke((Action)(() =>
        {
            Children(form).OfType<Button>().Single(button => button.Name == "applyRoute2").PerformClick();
            Assert.True(form.Visible);
            Assert.Equal(DialogResult.None, form.DialogResult);
            Assert.Equal("完成", ((Button)form.AcceptButton!).Text);
            ((Button)form.AcceptButton!).PerformClick();
        }));
        Assert.Equal(DialogResult.OK, form.ShowDialog());
        Assert.Equal(2, appliedOrder);
    });

    [Fact]
    public void ApplyExceptionShowsBesideButtonAndCanRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        int applications = 0;
        using var form = CreateForm(applySelection: (_, _, _) =>
        {
            applications++;
            return applications == 1
                ? Task.FromException<OpggBuildApplyResult>(new InvalidOperationException("客户端未连接"))
                : Task.FromResult(OpggBuildApplyResult.Success("符文和装备已配置"));
        });
        form.Show();
        var apply = (Button)form.AcceptButton!;
        apply.PerformClick();
        var status = Children(form).OfType<Label>().Single(label => label.Name == "applyStatus");
        Assert.True(status.Visible);
        Assert.Contains("客户端未连接", status.Text);
        Assert.True(apply.Enabled);
        apply.PerformClick();
        Assert.Contains("符文和装备已配置", status.Text);
        Assert.Equal("完成", apply.Text);
        form.Close();
        Assert.Equal(DialogResult.OK, form.DialogResult);
        Assert.Equal(2, applications);
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
        Assert.Contains(Children(form).OfType<Label>(), label => label.Name == "applyStatus" &&
            label.Visible && label.Text.Contains("与当前对局模式不一致"));
        Assert.False(Children(form).OfType<Button>().Single(button => button.Text == "应用选中方案").Enabled);
        Assert.All(Children(form).OfType<Button>().Where(button => button.Name.StartsWith("applyRoute")),
            button => Assert.False(button.Enabled));
        Assert.All(Children(form).OfType<Button>().Where(button => button.Text is "上路" or "下路" or "中路"),
            button => Assert.False(button.Enabled));
        Children(form).OfType<Button>().Single(button => button.Text == "峡谷").PerformClick();
        Assert.Equal("ranked", form.SelectedOption?.Mode);
        Assert.DoesNotContain(Children(form).OfType<Label>(), label => label.Name == "applyStatus" &&
            label.Text.Contains("与当前对局模式不一致"));
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
    public void ExplicitlyDisablingApplyDisablesEveryEntry() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(allowApply: false);
        form.Show();
        Assert.Contains(Children(form).OfType<Label>(), label => label.Name == "applyStatus" &&
            label.Visible && label.Text.Contains("仅供预览"));
        Assert.All(Children(form).OfType<Button>().Where(button => button.Name.StartsWith("applyRoute") ||
            button.Text == "应用选中方案"), button => Assert.False(button.Enabled));
        Children(form).OfType<Button>().Single(button => button.Text == "全部").PerformClick();
        Assert.False(((Button)form.AcceptButton!).Enabled);
        Assert.NotEqual(DialogResult.OK, form.DialogResult);
    });

    [Fact]
    public void WithoutMatchCanApplySelectedMode() => MatchListScrollingTests.OnUiThread(() =>
    {
        var service = BuildService(args => Task.FromResult(Choices((string)args[1]!,
            ((OpggBuildRequest)args[2]!).GameMode == "ARAM" ? "aram" : "ranked")));
        using var form = CreateForm(buildService: service, hasCurrentMatch: false);
        form.Show();
        Assert.True(((Button)form.AcceptButton!).Enabled);
        Children(form).OfType<Button>().Single(button => button.Text == "大乱斗").PerformClick();
        Assert.True(((Button)form.AcceptButton!).Enabled);
        Children(form).OfType<Button>().Single(button => button.Name == "applyRoute1").PerformClick();
        Assert.Equal(DialogResult.OK, form.DialogResult);
        Assert.Equal("aram", form.SelectedOption?.Mode);
    });

    [Theory]
    [InlineData("Lobby", 0, "", "aram", true, true)]
    [InlineData("未知", 0, "", "ranked", true, true)]
    [InlineData("ChampSelect", 122, "CLASSIC", "ranked", true, true)]
    [InlineData("ChampSelect", 22, "CLASSIC", "ranked", true, false)]
    [InlineData("ChampSelect", 122, "ARAM", "ranked", true, false)]
    [InlineData("InProgress", 122, "CLASSIC", "ranked", true, false)]
    [InlineData("Lobby", 0, "", "ranked", false, false)]
    public void ApplyRechecksCurrentContext(string phase, int championId, string mode,
        string selectedMode, bool preconfiguration, bool expected)
    {
        var original = new AiGameContext { MyChampionId = 122, MyRole = "TOP" };
        var latest = new AiGameContext { Phase = phase, MyChampionId = championId, GameMode = mode, MyRole = "BOTTOM" };
        Assert.Equal(expected, CoachForm.CanApplyBuildContext(original, latest, selectedMode, preconfiguration));
    }

    [Fact]
    public void LiveGameDataPreventsPreconfigurationWhenPhaseIsUnknown()
    {
        Assert.False(CoachForm.CanApplyBuildContext(new AiGameContext { MyChampionId = 122 },
            new AiGameContext { HasLiveClientData = true }, "ranked", true));
    }

    [Fact]
    public void PositionAliasesDoNotRejectAnUnchangedMatch()
    {
        Assert.True(CoachForm.CanApplyBuildContext(
            new AiGameContext { Phase = "ChampSelect", MyChampionId = 517, MyRole = "MIDDLE" },
            new AiGameContext { Phase = "ChampSelect", MyChampionId = 517, MyRole = "mid" }, "ranked", false));
    }

    private static OpggBuildPickerForm CreateForm(string mode = "ranked", IOpggBuildApplyService? buildService = null,
        bool allowApply = true, bool hasCurrentMatch = true,
        Func<OpggBuildOption, string, bool, Task<OpggBuildApplyResult>>? applySelection = null)
    {
        var options = Enumerable.Range(1, 4).Select(order => new OpggBuildOption(order,
            [1054, 2003], [3078, 3053, 6333], [3143, 3065, 3071, 3026, 3047, 3082],
            8000, 8200, mode == "aram_mayhem" ? [] : [8010, 9111, 9104, 8299, 8234, 8236, 5008, 5008, 5001],
            7200 / order, 3500 / order, [4, 12], Mode: mode)).ToArray();
        return new OpggBuildPickerForm(new OpggBuildChoices(true, "", "德莱厄斯", "上路", options,
            Mode: mode, ChampionId: 122), new EmptyAssets(), buildService: buildService,
            allowApply: allowApply, hasCurrentMatch: hasCurrentMatch, applySelection: applySelection) { Opacity = 0 };
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

[CollectionDefinition("Window resources", DisableParallelization = true)]
public sealed class WindowResourceCollection { }
