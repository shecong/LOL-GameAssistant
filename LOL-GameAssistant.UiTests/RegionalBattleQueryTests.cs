using System.Reflection;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class RegionalBattleQueryTests
{
    private sealed class Factory(PlayerQueryServices current, PlayerQueryServices other) : IRegionalPlayerQueryFactory
    {
        public IReadOnlyList<PlayerQueryRegion> Regions { get; } = [new("", "当前大区（自动）"), new("HN10", "黑色玫瑰 · HN10")];
        public PlayerQueryServices Create(string? platform) => string.IsNullOrWhiteSpace(platform) ? current : other;
    }
    private static PlayerQueryServices Sources(string name, Task<PlayerProfile?>? pending = null) => new(
        Service<IPlayerProfileService>((_, _) => pending ?? Task.FromResult<PlayerProfile?>(new("test-puuid", name, "", 0, 0, 0, 0))),
        Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() })),
        Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)), Service<IPremadeDetectionService>((_, _) => null));
    private static BattleQueryForm Create(PlayerQueryServices current, IRegionalPlayerQueryFactory factory) => new(current.Profiles,
        Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)), current.History, current.Ranked,
        Service<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer>()), Service<ITextExportService>((_, _) => Task.CompletedTask), regionalQueries: factory);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    [Fact]
    public void RegionSelectionChangesOnlyThisWindowAndCancelsOldResult() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<PlayerProfile?>();
        var current = Sources("旧区", pending.Task); var other = Sources("黑色玫瑰"); var factory = new Factory(current, other);
        using var query = Create(current, factory); using var unaffected = Create(current, factory);
        var old = query.PerformSearchAsync("11111111-1111-1111-1111-111111111111");
        query.SelectQueryRegion("HN10");
        pending.SetResult(new("old-player", "旧结果", "", 0, 0, 0, 0)); Pump(old);
        Assert.Null(Read<PlayerProfile?>(query, "_currentPlayer"));
        Assert.Same(other.History, Read<IMatchHistoryService>(query, "_matchHistoryService"));
        Assert.Same(current.History, Read<IMatchHistoryService>(unaffected, "_matchHistoryService"));
        Pump(query.PerformSearchAsync("22222222-2222-2222-2222-222222222222"));
        Assert.Equal("黑色玫瑰", Read<PlayerProfile>(query, "_currentPlayer").GameName);
        Pump((Task)typeof(BattleQueryForm).GetMethod("ShowGrowthAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(query, [true])!);
        var growth = Read<PlayerGrowthControl>(query, "_growthView");
        Assert.NotNull(Read<PlayerGrowthSnapshot>(growth, "_snapshot"));
        Pump((Task)typeof(BattleQueryForm).GetMethod("ShowGrowthAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(query, [false])!);
        Assert.False(Read<AntdUI.Button>(growth, "_replay").Visible);
        query.SelectQueryRegion(""); Assert.Null(Read<PlayerProfile?>(query, "_currentPlayer"));
        Assert.Same(current.History, Read<IMatchHistoryService>(query, "_matchHistoryService"));
    });

    [Theory]
    [InlineData(1600)]
    [InlineData(900)]
    [InlineData(520)]
    public void RegionToolbarRemainsVisibleAtDifferentWidths(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        var current = Sources("本区"); var factory = new Factory(current, Sources("跨区"));
        using var query = Create(current, factory);
        using var host = new Form { ClientSize = new(width, 820) }; query.Dock = DockStyle.Fill; host.Controls.Add(query); host.Show();
        System.Windows.Forms.Application.DoEvents();
        var select = Read<AntdUI.Select>(query, "_regionSelect"); select.SelectedIndex = 1;
        Assert.Equal("HN10", Read<string>(query, "_queryPlatform"));
        var toolbar = Read<FlowLayoutPanel>(query, "_searchActions");
        foreach (Control control in toolbar.Controls)
        {
            Assert.True(control.Right <= toolbar.ClientSize.Width, $"{control.GetType().Name} exceeds toolbar width at {width}");
            Assert.True(control.Bottom <= toolbar.ClientSize.Height, $"{control.GetType().Name} exceeds toolbar height at {width}: top={control.Top}, height={control.Height}, toolbar={toolbar.Height}, dpi={query.DeviceDpi}");
        }
        string path = Path.Combine(AppContext.BaseDirectory, $"regional-query-{width}.png");
        using var bitmap = new Bitmap(host.Width, host.Height); host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size)); bitmap.Save(path);
        host.Close();
    });
}
