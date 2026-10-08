using System.Reflection;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.LiveGame;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class BattleOverlayTests
{
    [Fact]
    public void CurrentLoadoutParsesLocalizedNamesAndKeepsSpellSlotsInOrder()
    {
        var player = Assert.Single(LocalLiveClientDataReader.ParseScoreboard("""
            [{"championName":"寒冰射手","runes":{
              "keystone":{"id":8005,"displayName":"强攻"},
              "primaryRuneTree":{"id":8000,"displayName":"精密"},
              "secondaryRuneTree":{"id":8300,"displayName":"启迪"}},
              "summonerSpells":{
                "summonerSpellOne":{"displayName":"闪现","rawDisplayName":"GeneratedTip_SummonerSpell_SummonerFlash_DisplayName"},
                "summonerSpellTwo":{"displayName":"引燃","rawDisplayName":"GeneratedTip_SummonerSpell_SummonerDot_DisplayName"}}}]
            """));
        Assert.Equal("寒冰射手", player.ChampionName);
        Assert.Equal(new LiveGameAbility(8005, "强攻"), player.Loadout!.Keystone);
        Assert.Equal(8000, player.Loadout.PrimaryRuneTree!.Id);
        Assert.Equal(8300, player.Loadout.SecondaryRuneTree!.Id);
        Assert.Equal(new LiveGameAbility(4, "闪现"), player.Loadout.SpellOne);
        Assert.Equal(new LiveGameAbility(14, "引燃"), player.Loadout.SpellTwo);
    }

    [Fact]
    public void NullRunesInMayhemDoNotLoseRealSummonerSpells()
    {
        var player = Assert.Single(LocalLiveClientDataReader.ParseScoreboard("""
            [{"runes":null,"summonerSpells":{
              "summonerSpellOne":{"displayName":"治疗术","rawDisplayName":"GeneratedTip_SummonerSpell_SummonerHeal_DisplayName"},
              "summonerSpellTwo":{"displayName":"闪现","rawDisplayName":"GeneratedTip_SummonerSpell_SummonerFlash_DisplayName"}}}]
            """));
        Assert.Null(player.Loadout!.Keystone);
        Assert.Null(player.Loadout.PrimaryRuneTree);
        Assert.Null(player.Loadout.SecondaryRuneTree);
        Assert.Equal(7, player.Loadout.SpellOne!.Id);
        Assert.Equal(4, player.Loadout.SpellTwo!.Id);
        var missing = Assert.Single(LocalLiveClientDataReader.ParseScoreboard("[{\"summonerSpells\":null}]"));
        Assert.Null(missing.Loadout!.SpellOne);
        Assert.Null(missing.Loadout.SpellTwo);
    }

    [Fact]
    public void UnknownSpellRetainsNameWithoutGuessingAnotherIcon()
    {
        var player = Assert.Single(LocalLiveClientDataReader.ParseScoreboard("""
            [{"summonerSpells":{"summonerSpellOne":{"displayName":"特殊闪现","rawDisplayName":"GeneratedTip_SummonerSpell_SummonerFlashSpecial_DisplayName"}}}]
            """));
        Assert.Equal(new LiveGameAbility(0, "特殊闪现"), player.Loadout!.SpellOne);
    }

    [Fact]
    public void LoadoutIconsAreSharedAcrossPlayersWithRuneAndSpellIdsSeparate() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bitmap = new Bitmap(20, 20);
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
        var requested = new List<string>();
        var assets = Proxy<IGameAssetService>((method, args) =>
        {
            requested.Add($"{method.Name}:{args[0]}");
            return Task.FromResult<GameAsset?>(new(bytes.ToArray()));
        });
        using var overlay = CreateOverlay(assets);
        var score = new LiveScoreboardPlayer("test#A", "test", "ORDER", 0, 0, 0, 0, null)
        {
            Loadout = new(new(4, "Rune"), new(8000, "精密"), new(8300, "启迪"), new(4, "闪现"), new(14, "引燃"))
        };
        var load = typeof(BattleOverlayForm).GetMethod("EnsureLoadoutIcons", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int refresh = 0; refresh < 2; refresh++)
            load.Invoke(overlay, [Enumerable.Repeat(score, 10).ToArray(), CancellationToken.None]);
        Assert.Equal(5, requested.Count);
        Assert.Contains("GetRuneIconAsync:4", requested);
        Assert.Contains("GetSummonerSpellIconAsync:4", requested);
    });

    [Fact]
    public void PendingLcuRosterDoesNotBlockCurrentSpellsAndLocalRoster() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<ActiveGameSnapshot?>();
        var lobby = Proxy<ILobbyService>((_, _) => pending.Task);
        var scores = new[] { new LiveScoreboardPlayer("test#A", "test", "ORDER", 0, 0, 0, 0, null)
        {
            ChampionName = "寒冰射手", Loadout = new(null, null, null, new(4, "闪现"), new(7, "治疗术"))
        } };
        var live = Proxy<ILiveClientGameStateService>((_, _) => Task.FromResult<IReadOnlyList<LiveScoreboardPlayer>?>(scores));
        var assets = Proxy<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        var champions = Proxy<IChampionCatalog>((method, _) => method.Name == "FindIdByDisplayName" ? 22 : "寒冰射手");
        using var overlay = new BattleOverlayForm(lobby, null!, live, assets, champions);
        overlay.ShowForGame(new Rectangle(0, 0, 1280, 720), "·");
        overlay.StartTracking();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var session = (ActiveGameSnapshot)typeof(BattleOverlayForm).GetField("_session", flags)!.GetValue(overlay)!;
        Assert.Equal("test#A", Assert.Single(session.TeamOne).SummonerName);
        Assert.Equal(22, session.TeamOne[0].ChampionId);
        Assert.Same(scores, typeof(BattleOverlayForm).GetField("_scores", flags)!.GetValue(overlay));
        Assert.False(pending.Task.IsCompleted);
        overlay.StopTracking();
    });

    [Fact]
    public void PollingShowsOnceWithoutWindowsHotkeyMessageAndHidesOnReleaseOrFocusLoss() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form();
        using var controller = new WindowHoldController(owner);
        int shown = 0, hidden = 0;
        controller.ConfigureBattleOverlay(() => true, _ => shown++, () => hidden++);
        controller.Apply(new AssistantSettings());
        var down = new HashSet<int> { (int)Keys.Oem3 };
        // No native handle and no WM_HOTKEY: reproduces the missed-message path.
        Assert.False(owner.IsHandleCreated);
        for (int tick = 0; tick < 5; tick++) controller.ObserveHotkeyState(true, true, false, Keys.None, down.Contains);
        Assert.Equal(1, shown);
        Assert.Equal(0, hidden);
        down.Clear();
        controller.ObserveHotkeyState(true, true, false, Keys.None, down.Contains);
        Assert.Equal(1, hidden);
        down.Add((int)Keys.Oem3);
        controller.ObserveHotkeyState(true, true, false, Keys.None, down.Contains);
        Assert.Equal(2, shown);
        controller.ObserveHotkeyState(false, false, false, Keys.None, down.Contains);
        Assert.Equal(2, hidden);
    });

    [Fact]
    public void RecordingOnlyPausesTheForegroundEditorAndCombinationsRequireExactModifiers() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form();
        using var controller = new WindowHoldController(owner);
        int shown = 0, hidden = 0;
        controller.ConfigureBattleOverlay(() => true, _ => shown++, () => hidden++);
        controller.Apply(new AssistantSettings { BattleOverlayHotkey = "Ctrl+F10" });
        controller.SetCapturePaused(true);
        var down = new HashSet<int> { (int)Keys.F10, (int)Keys.ControlKey };
        controller.ObserveHotkeyState(true, true, true, Keys.Control, down.Contains);
        Assert.Equal(0, shown);
        controller.ObserveHotkeyState(true, true, false, Keys.Control | Keys.Shift, down.Contains);
        Assert.Equal(0, shown);
        // The editor can still remember its focus, but switching to the game resumes input.
        controller.ObserveHotkeyState(true, true, false, Keys.Control, down.Contains);
        Assert.Equal(1, shown);
        down.Remove((int)Keys.ControlKey);
        controller.ObserveHotkeyState(true, true, false, Keys.None, down.Contains);
        Assert.Equal(1, hidden);
    });

    [Theory]
    [InlineData(false, "Oem3")]
    [InlineData(true, "F6")]
    public void PollingCannotBypassDisabledOrConflictingSettings(bool enabled, string key) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form();
        using var controller = new WindowHoldController(owner);
        int shown = 0;
        controller.ConfigureBattleOverlay(() => true, _ => shown++, () => { });
        controller.Apply(new AssistantSettings { BattleOverlayEnabled = enabled, BattleOverlayHotkey = key });
        controller.ObserveHotkeyState(true, true, false, Keys.None, _ => true);
        Assert.Equal(0, shown);
    });

    [Fact]
    public void OldSettingsAndSavedCombinationKeysRoundTrip()
    {
        var old = JsonConvert.DeserializeObject<SettingConfig>("{}")!;
        Assert.True(old.BattleOverlayEnabled);
        Assert.Equal("Oem3", old.BattleOverlayHotkey);
        var mapper = typeof(GameMain).Assembly.GetType("LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper")!;
        var settings = new AssistantSettings { BattleOverlayEnabled = false, BattleOverlayHotkey = "Ctrl+F10" };
        var saved = mapper.GetMethod("ToLegacy")!.Invoke(null, [settings]);
        var reloaded = JsonConvert.DeserializeObject<SettingConfig>(JsonConvert.SerializeObject(saved));
        var restored = (AssistantSettings)mapper.GetMethod("ToDomain")!.Invoke(null, [reloaded])!;
        Assert.False(restored.BattleOverlayEnabled);
        Assert.Equal("Ctrl+F10", restored.BattleOverlayHotkey);
    }

    [Theory]
    [InlineData("·")]
    [InlineData("`")]
    [InlineData("~")]
    public void PhysicalBacktickKeyAliasesResolve(string text)
    {
        Assert.True(WindowHoldController.TryParseGameHotkey(text, out Keys key));
        Assert.Equal(Keys.Oem3, key);
    }

    [Theory]
    [InlineData("F6", true)]
    [InlineData("F9", true)]
    [InlineData("NumPad0", true)]
    [InlineData("Ctrl+F6", false)]
    [InlineData("Oem3", false)]
    [InlineData("Ctrl", true)]
    public void OverlayRejectsEnabledActionsButAllowsModifiersAndSharedMainKey(string key, bool conflicts) =>
        Assert.Equal(conflicts, WindowHoldController.HasBattleOverlayHotkeyConflict(new AssistantSettings { BattleOverlayHotkey = key }));

    [Fact]
    public void DisabledActionsReleaseTheirKeys()
    {
        var settings = new AssistantSettings { BattleOverlayHotkey = "F6", QuickShoutHotkeysEnabled = false };
        Assert.False(WindowHoldController.HasBattleOverlayHotkeyConflict(settings));
        settings.QuickShoutHotkeysEnabled = true;
        settings.BattleOverlayEnabled = false;
        Assert.False(WindowHoldController.HasBattleOverlayHotkeyConflict(settings));
    }

    [Fact]
    public void ReleasingAnyRequiredKeyEndsCombinationHold()
    {
        var pressed = new HashSet<int> { (int)Keys.F10, (int)Keys.ControlKey, (int)Keys.ShiftKey };
        var key = Keys.Control | Keys.Shift | Keys.F10;
        Assert.True(WindowHoldController.IsHotkeyHeld(key, pressed.Contains));
        pressed.Remove((int)Keys.ControlKey);
        Assert.False(WindowHoldController.IsHotkeyHeld(key, pressed.Contains));
        pressed.Add((int)Keys.ControlKey);
        pressed.Remove((int)Keys.F10);
        Assert.False(WindowHoldController.IsHotkeyHeld(key, pressed.Contains));
    }

    [Fact]
    public void HistoryExcludesOtherModesRemakesAndMissingStatsAndCountsTheActualPlayer()
    {
        var member = new GameTeamMember { Puuid = "me", ChampionId = 22 };
        var win = Game(true, 22, 8, 2, 4);
        var loss = Game(false, 99, 2, 4, 2);
        var otherMode = Game(false, 22, 0, 9, 0); otherMode.QueueId = 430;
        var remake = Game(false, 22, 0, 9, 0); remake.GameDuration = 100;
        var missing = Game(false, 22, 0, 9, 0); missing.Participants[0].stats = null;
        var differentPlayer = Game(false, 22, 0, 9, 0);
        differentPlayer.ParticipantIdentities = [new() { ParticipantId = 1, Player = new() { Puuid = "other" } }];
        var result = BattleOverlayPlayer.Create(member, "名字#CN1", null,
            [win, loss, otherMode, remake, missing, differentPlayer], 450, "ARAM");
        Assert.Equal(2, result.RecentResults.Count);
        Assert.Equal(1, result.ChampionGames);
        Assert.Equal(100d, result.ChampionWinRate);
        Assert.Equal(16d / 6, result.RecentKda);
        var empty = BattleOverlayPlayer.Create(member, "名字", null, [], 450, "ARAM");
        Assert.Null(empty.RecentKda);
        Assert.Null(empty.ChampionWinRate);
    }

    [Fact]
    public void ScoreboardKeepsVisionAsScoreAndDoesNotInventMissingVision()
    {
        var result = LocalLiveClientDataReader.ParseScoreboard("""
            [{"riotIdGameName":"名字","riotIdTagLine":"CN1","team":"ORDER","scores":{"kills":3,"deaths":2,"assists":7,"creepScore":80,"wardScore":12.5}},
             {"summonerName":"Other","team":"CHAOS","scores":{}}]
            """);
        Assert.Equal("名字#CN1", result[0].RiotId);
        Assert.Equal(12.5, result[0].VisionScore);
        Assert.Null(result[1].VisionScore);
        Assert.Equal(80, result[0].CreepScore);
    }

    [Fact]
    public void AmbiguousLegacyNamesCannotAssignAnotherPlayersScores()
    {
        var scores = new[]
        {
            new LiveScoreboardPlayer("same#A", "same", "ORDER", 1, 0, 0, 0, null),
            new LiveScoreboardPlayer("same#B", "same", "ORDER", 9, 0, 0, 0, null)
        };
        Assert.Null(BattleOverlayForm.FindScore(scores, "same", "same", "ORDER"));
        Assert.Equal(9, BattleOverlayForm.FindScore(scores, "same#B", "same", "ORDER")!.Kills);
        Assert.Null(BattleOverlayForm.FindScore(scores, "same#B", "same", "CHAOS"));
    }

    [Fact]
    public async Task OneFailedSourceStillReturnsOtherPlayerInformation()
    {
        var profiles = Proxy<IPlayerProfileService>((_, _) => Task.FromException<PlayerProfile?>(new IOException("offline")));
        var ranks = Proxy<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null));
        var history = Proxy<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [Game(true, 22, 3, 0, 7)] } }));
        var service = new BattleOverlayService(profiles, ranks, history);
        var result = await service.GetPlayerAsync(new() { Puuid = "me", SummonerName = "Fallback", ChampionId = 22 },
            new() { QueueId = 450, GameMode = "ARAM" }, CancellationToken.None);
        Assert.Equal("Fallback", result.Name);
        Assert.Null(result.Rank);
        Assert.Equal(10d, result.RecentKda);
    }

    [Fact]
    public async Task CancelledPlayerLoadCannotBePublished()
    {
        var token = new CancellationToken(true);
        var profiles = Proxy<IPlayerProfileService>((_, _) => new TaskCompletionSource<PlayerProfile?>().Task);
        var ranks = Proxy<IRankedStatsService>((_, _) => new TaskCompletionSource<RankedOverview?>().Task);
        var history = Proxy<IMatchHistoryService>((_, _) => new TaskCompletionSource<MatchHistoryResponse?>().Task);
        var service = new BattleOverlayService(profiles, ranks, history);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetPlayerAsync(new() { Puuid = "me" }, new(), token));
    }

    [Fact]
    public void OverlayUsesNativeNonActivationAndMousePassThroughAndFitsGameBounds() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var overlay = CreateOverlay();
        overlay.ShowForGame(new Rectangle(10, 10, 1000, 600), "·");
        int style = GetWindowLong(overlay.Handle, -20);
        Assert.Equal(0x08000000, style & 0x08000000);
        Assert.Equal(0x20, style & 0x20);
        Assert.Equal(0x80000, style & 0x80000);
        Assert.Equal((nint)3, SendMessage(overlay.Handle, 0x0021, 0, 0));
        Assert.Equal((nint)(-1), SendMessage(overlay.Handle, 0x0084, 0, 0));
        Assert.False(overlay.ShowInTaskbar);
        Assert.True(overlay.Width <= 900);
        Assert.True(overlay.Height <= 510);
        Assert.True(overlay.Visible);
        overlay.StopTracking();
        Assert.False(overlay.Visible);
    });

    [Fact]
    public void RenderBothTeamsAtMultipleSizesAndExportOptionalPreview() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var overlay = CreateOverlay();
        var members = Enumerable.Range(0, 10).Select(i => new GameTeamMember
        {
            Puuid = $"player-{i}", SummonerName = $"玩家{i + 1}#CN1", ChampionId = i % 2 == 0 ? 22 : 99
        }).ToArray();
        var session = new ActiveGameSnapshot { QueueId = 450, GameMode = "ARAM", TeamOne = members[..5], TeamTwo = members[5..] };
        var players = members.Select((m, i) => BattleOverlayPlayer.Create(m, m.SummonerName,
            new RankedQueue { Tier = "DIAMOND", Division = "II", Wins = 60, Losses = 40 },
            Enumerable.Range(0, 10).Select(n => Game(n % 3 != 0, m.ChampionId, 8, 3, 12)), 450, "ARAM")).ToArray();
        var scores = members.Select((m, i) => new LiveScoreboardPlayer(m.SummonerName, m.SummonerName,
            i < 5 ? "ORDER" : "CHAOS", 5 + i, 3, 8, 120, 12.5)
            { Loadout = new(new(8005, "强攻"), new(8000, "精密"), new(8300, "启迪"), new(4, "闪现"), new(7, "治疗术")) }).ToArray();
        overlay.SetPreviewData(session, players, scores);
        foreach (var size in new[] { new Size(1120, 680), new Size(840, 510), new Size(1288, 782) })
        {
            overlay.ClientSize = size;
            using var bitmap = new Bitmap(size.Width, size.Height);
            overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            Assert.NotEqual(bitmap.GetPixel(40, 40).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            string? directory = Environment.GetEnvironmentVariable("BATTLE_OVERLAY_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, $"battle-overlay-{size.Width}.png"));
            }
        }
    });

    [Fact]
    public void WinRatesKeepDecimalPrecisionAndExposeTheirActualDenominators()
    {
        var member = new GameTeamMember { Puuid = "me", ChampionId = 22 };
        var win = Game(true, 22, 1, 1, 1); win.GameId = 1;
        var winTwo = Game(true, 22, 1, 1, 1); winTwo.GameId = 2;
        var loss = Game(false, 22, 1, 1, 1); loss.GameId = 3;
        var result = BattleOverlayPlayer.Create(member, "me", null, [win, winTwo, loss, loss], 450, "ARAM");
        Assert.Equal(3, result.ChampionGames);
        Assert.Equal(2, result.ChampionWins);
        Assert.Equal("样本 66.7%", BattleOverlayForm.ChampionRateText(result, false));
        Assert.Equal("2胜1负 / 3场", BattleOverlayForm.RecordCounts(result.ChampionWins, result.ChampionLosses, false));
        var rank = new RankedQueue { QueueType = RankedQueues.Solo5x5, Wins = 1, Losses = 6 };
        Assert.Equal("单双 14.3%", BattleOverlayForm.RankRateText(rank, false));
        Assert.Equal("1胜6负 / 7场", BattleOverlayForm.RecordCounts(rank.Wins, rank.Losses, false));
        Assert.Equal("灵活 50%", BattleOverlayForm.RankRateText(new() { QueueType = RankedQueues.Flex5x5, Wins = 1, Losses = 1 }, false));
    }

    [Fact]
    public void MissingWinFieldIsNotCountedAsALoss()
    {
        var raw = Newtonsoft.Json.JsonConvert.DeserializeObject<LOL_GameAssistant.Entity.GameHeadModel.MatchHistoryResponse>("""
            {"games":{"games":[
              {"gameId":1,"queueId":450,"gameDuration":1200,"participants":[{"championId":22,"participantId":1,"stats":{"kills":3}}]},
              {"gameId":2,"queueId":450,"gameDuration":1200,"participants":[{"championId":22,"participantId":1,"stats":{"win":true}}]},
              {"gameId":3,"queueId":450,"gameDuration":1200,"participants":[{"championId":22,"participantId":1,"stats":{"win":false}}]}
            ]}}
            """);
        var mapped = LOL_GameAssistant.Infrastructure.LeagueClient.LegacyMatchReadModelMapper.ToDomain(raw)!;
        Assert.False(mapped.Games!.Games[0].Participants[0].stats!.HasWinResult);
        var result = BattleOverlayPlayer.Create(new() { Puuid = "me", ChampionId = 22 }, "me", null,
            mapped.Games.Games, 450, "ARAM");
        Assert.Equal(2, result.ChampionGames);
        Assert.Equal(50, result.ChampionWinRate);
        Assert.Equal(new[] { true, false }, result.RecentResults);
    }

    [Fact]
    public void SeasonRecordUsesQueueMapWithoutBeingOverwrittenByDuplicateArray()
    {
        var source = new LOL_GameAssistant.Entity.LolRankedDataParser.RankedData
        {
            QueueMap = new() { [RankedQueues.Solo5x5] = new() { Wins = 3, Losses = 1 } },
            Queues = [new() { QueueType = RankedQueues.Solo5x5, Wins = 0, Losses = 1 }]
        };
        var result = LOL_GameAssistant.Infrastructure.LeagueClient.LegacyRankedStatsService.Map(source)
            .GetQueue(RankedQueues.Solo5x5)!;
        Assert.Equal(3, result.Wins);
        Assert.Equal(1, result.Losses);
        Assert.Equal("单双 75%", BattleOverlayForm.RankRateText(result, false));
    }

    private static MatchHistoryGame Game(bool win, int champion, int kills, int deaths, int assists) => new()
    {
        QueueId = 450, GameMode = "ARAM", GameDuration = 1200,
        Participants = [new() { championId = champion, participantId = 1,
            stats = new() { Win = win, kills = kills, deaths = deaths, assists = assists } }]
    };

    private static BattleOverlayForm CreateOverlay(IGameAssetService? assets = null) => new(null!, null!, null!, assets!,
        Proxy<IChampionCatalog>((method, args) => method.Name == "GetDisplayName" ? ((int)args[0]! == 22 ? "寒冰射手" : "光辉女郎") : null));

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
