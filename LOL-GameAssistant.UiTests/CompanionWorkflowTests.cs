using System.Net;
using LOL_GameAssistant.Application.Automation;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json.Linq;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CompanionWorkflowTests
{
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "companion-tests", Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private static MatchHistoryGame Game(long id, bool? win, int duration = 1200) => new()
    {
        GameId = id, GameCreation = id, QueueId = 420, GameMode = "CLASSIC", GameDuration = duration,
        Participants = [new() { championId = 1, participantId = 1, stats = new() { Win = win ?? false, HasWinResult = win.HasValue,
            HasKdaResult = true, kills = 4, deaths = 2, assists = 6, totalMinionsKilled = 100 } }]
    };
    private static IChampionCatalog Catalog() => Service<IChampionCatalog>((m, a) => m.Name switch
    {
        "GetAll" => (IReadOnlyList<ChampionReference>)new[] { new ChampionReference(1, "安妮"), new ChampionReference(2, "奥拉夫") },
        "GetDisplayName" => (int)a[0]! == 1 ? "安妮" : "奥拉夫", _ => null
    });
    private static ILcuRequestSender Sender(Func<string, string?> read, Action<string, string, string?>? write = null) =>
        Service<ILcuRequestSender>((m, a) =>
        {
            if (m.Name == "GetStringAsync") return Task.FromResult(read((string)a[0]!));
            if (m.Name == "GetBytesAsync") return Task.FromResult<byte[]?>(null);
            write?.Invoke(m.Name, (string)a[0]!, a.Length > 2 ? a[1] as string : null); return Task.FromResult(true);
        });

    [Fact]
    public void GrowthUsesKnownDenominatorsAndDoesNotInventMissingMetrics()
    {
        var known = Game(1, true); known.Participants[0].stats!.HasFarmResult = true;
        var remake = Game(3, false, 100); var other = Game(4, false); other.QueueId = 450; other.GameMode = "ARAM";
        var ambiguous = Game(5, false); ambiguous.Participants.Add(new() { championId = 2 });
        var report = PlayerGrowthCalculator.Calculate([known, known, Game(2, null), remake, other, ambiguous], "target", known.GetModeText());
        Assert.Equal(2, report.Matches.Count); Assert.Equal(2, report.ExcludedGames);
        Assert.Equal(1, report.ResultSamples); Assert.Equal(100d, report.WinRate);
        Assert.Equal(5d, report.Champions.Single().CsPerMinute);
        Assert.Null(report.Matches.Single(m => m.GameId == 2).CsPerMinute);
        Assert.All(report.Matches, m => Assert.Null(m.DamagePerMinute));
    }

    [Fact]
    public async Task CapabilitiesMatchParametersAndNeverProbeByWriting()
    {
        int writes = 0;
        var lcu = Sender(p => p.Contains("openapi") ? "{\"paths\":{\"/pages/{id}\":{\"get\":{},\"patch\":{}}}}" : null, (_, _, _) => writes++);
        var service = new LcuClientCapabilityService(lcu);
        Assert.Equal(ClientCapabilityState.Advertised, (await service.CheckAsync("PATCH", "/pages/42?x=1")).State);
        Assert.Equal(ClientCapabilityState.MethodUnavailable, (await service.CheckAsync("PUT", "/pages/42")).State);
        Assert.Equal(ClientCapabilityState.Unknown, (await service.CheckAsync("GET", "/different")).State);
        service.Observe("GET", "/different", HttpStatusCode.OK, LcuFailure.None);
        Assert.Equal(ClientCapabilityState.Unknown, (await service.CheckAsync("PUT", "/different")).State);
        service.Observe("GET", "/pages/42", HttpStatusCode.NotFound, LcuFailure.HttpStatus);
        Assert.Equal(ClientCapabilityState.ContextUnavailable, (await service.CheckAsync("GET", "/pages/42")).State);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task SharedSelectReadCannotOverwriteNewEventOrDelete()
    {
        var cache = new ChampionSelectSessionCache(); var pending = new TaskCompletionSource<ChampSelectSession?>();
        using var cancelled = new CancellationTokenSource();
        var first = cache.GetAsync(_ => pending.Task, cancelled.Token);
        var second = cache.GetAsync(_ => throw new Exception("duplicate read"), default);
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        cache.Observe("{\"localPlayerCellId\":7}", "Update"); pending.SetResult(new());
        Assert.Equal(7, (await second)!.LocalPlayerCellId);
        cache.Observe("null", "Delete"); Assert.Null(await cache.GetAsync(_ => Task.FromResult<ChampSelectSession?>(null), default));
        int reads = 0;
        Task<HashSet<int>?> Load(CancellationToken _) { reads++; return Task.FromResult<HashSet<int>?>([1, 2]); }
        await cache.GetChampionSetAsync("pickable", Load, default); await cache.GetChampionSetAsync("pickable", Load, default);
        Assert.Equal(1, reads); cache.Clear(); await cache.GetChampionSetAsync("pickable", Load, default); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task UnknownReplayCannotBeWatchedAndAmbiguousProgressIsNotAPercent()
    {
        int writes = 0; var service = new LcuClientFeatureService(Sender(_ => "{\"state\":\"future-state\"}", (_, _, _) => writes++));
        Assert.False((await service.WatchReplayAsync(12)).Succeeded); Assert.Equal(0, writes);
        var downloading = LcuClientFeatureService.ParseReplay(12, "{\"state\":\"downloading\",\"downloadProgress\":0.5}");
        Assert.Null(downloading.Progress);
        Assert.Equal(50d, LcuClientFeatureService.ParseReplay(12, "{\"state\":\"downloading\",\"downloadProgressPercent\":50}").Progress);
    }

    [Fact]
    public async Task RewardsRejectDuplicateUnknownAndUnchosenCandidates()
    {
        int writes = 0;
        const string json = "[{\"info\":{\"id\":\"g\",\"status\":\"PENDING_SELECTION\"},\"rewardGroup\":{\"id\":\"group\",\"maximumSelections\":1,\"rewards\":[{\"id\":\"a\"},{\"id\":\"b\"}]}}]";
        var service = new LcuClientFeatureService(Sender(_ => json, (_, _, _) => writes++));
        Assert.False((await service.ClaimRewardAsync("g", [])).Succeeded);
        Assert.False((await service.ClaimRewardAsync("g", ["a", "a"])).Succeeded);
        Assert.False((await service.ClaimRewardAsync("g", ["foreign"])).Succeeded);
        await service.ClaimAllPendingRewardsAsync(); Assert.Equal(0, writes);
        Assert.True((await service.ClaimRewardAsync("g", ["b"])).Succeeded); Assert.Equal(1, writes);
    }

    [Fact]
    public async Task BackupUsesAdvertisedPatchAndRollsBackBothCategoriesOnPartialFailure()
    {
        using var folder = new Folder(); int game = 1, input = 2; bool rejectInput = false;
        var writes = new List<string>();
        var lcu = Sender(p => p switch
        {
            "/lol-summoner/v1/current-summoner" => "{\"puuid\":\"account\"}",
            "/swagger/v3/openapi.json" => "{\"paths\":{\"/lol-game-settings/v1/game-settings\":{\"patch\":{}},\"/lol-game-settings/v1/input-settings\":{\"patch\":{}}}}",
            "/lol-game-settings/v1/game-settings" => $"{{\"value\":{game}}}",
            "/lol-game-settings/v1/input-settings" => $"{{\"value\":{input}}}", _ => null
        }, (method, route, body) =>
        {
            Assert.Equal("PatchAsync", method); writes.Add(route);
            int value = JObject.Parse(body!).Value<int>("value");
            if (route.EndsWith("/game-settings")) game = value;
            else if (!rejectInput || value != 2) input = value;
        });
        var service = new LcuClientFeatureService(lcu, backupRoot: folder.Path);
        Assert.True((await service.SaveGameSettingsBackupAsync("test")).Succeeded);
        game = 10; input = 20;
        Assert.Equal(2, (await service.PreviewBackupAsync("test")).Differences.Count);
        rejectInput = true;
        Assert.False((await service.RestoreBackupSelectionAsync("test", true, true)).Succeeded);
        Assert.Equal(10, game); Assert.Equal(20, input); Assert.Equal(4, writes.Count);
    }

    [Fact]
    public async Task ManualHonorDoesNotReturnToLobbyOrSearch()
    {
        var automation = new PostGameAutomation(Service<IClientFeatureService>((_, _) => throw new Exception("must remain manual")), Service<IPartyService>((_, _) => throw new Exception("must not search")));
        await automation.RunAsync(true, true, true, strategy: "manual");
        Assert.Contains(automation.Snapshot(), s => s.Name == "返回大厅" && s.State == "等待");
    }

    [Fact]
    public async Task PartyRequiresActualEligibilityAndVerifiedOnlineFriend()
    {
        var writes = new List<(string Route, string? Body)>();
        var service = new LcuPartyService(Sender(p => p switch
        {
            "/lol-game-queues/v1/queues" => "[{\"id\":420},{\"id\":430,\"isQueueEnabled\":true}]",
            "/lol-lobby/v2/lobby" => "{\"localMember\":{\"canInviteOthers\":true},\"canStartActivity\":false}",
            "/lol-chat/v1/friends" => "[{\"puuid\":\"friend\",\"gameName\":\"好友\",\"availability\":\"chat\"},{\"puuid\":\"offline\",\"availability\":\"offline\"}]", _ => "[]"
        }, (_, p, b) => writes.Add((p, b))));
        Assert.False((await service.CreateAsync(420)).Succeeded);
        Assert.False((await service.InviteFriendAsync("nickname")).Succeeded);
        Assert.False((await service.InviteFriendAsync("offline")).Succeeded);
        Assert.False((await service.StartSearchAsync()).Succeeded);
        Assert.True((await service.InviteFriendAsync("friend")).Succeeded);
        Assert.Single(writes); var body = JArray.Parse(writes[0].Body!)[0];
        Assert.Equal("friend", body.Value<string>("toPuuid")); Assert.Null(body["toSummonerId"]);
    }

    [Fact]
    public async Task GrowthTrainingAndNotesAreIsolatedByQueriedPlayer()
    {
        using var folder = new Folder(); var routes = new List<string>();
        var matches = Service<IMatchHistoryService>((m, _) => m.Name == "GetDetailAsync" ? Task.FromResult<MatchDetail?>(null) : Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [Game(1, true)] } }));
        var growth = new LcuPlayerGrowthService(matches, Catalog(), Sender(p => { routes.Add(p); return "{}"; }), folder.Path);
        await growth.SetTrainingAsync("a", 1, true); await growth.SetReviewNoteAsync("a", 1, "十分钟回城决策");
        var a = await growth.LoadAsync("a", "全部"); var b = await growth.LoadAsync("b", "全部");
        Assert.True(a.Champions.Single(c => c.ChampionId == 1).Training); Assert.Equal("十分钟回城决策", a.ReviewNotes[1]);
        Assert.All(b.Champions, c => { Assert.False(c.Training); Assert.Null(c.Owned); }); Assert.Empty(b.ReviewNotes);
        Assert.DoesNotContain(routes, p => p.Contains("current-summoner") || p.Contains("owned-champions"));
    }

    [Fact]
    public async Task PostGameRetriesFailedStepsWithoutRepeatingAcceptedHonor()
    {
        int honors = 0, returns = 0;
        var features = Service<IClientFeatureService>((m, _) => { if (m.Name.Contains("Honor")) { honors++; return Task.FromResult(ClientFeatureResult.Success("ok")); } returns++; return Task.FromResult(returns == 1 ? ClientFeatureResult.Failure("not ready") : ClientFeatureResult.Success("ok")); });
        var party = Service<IPartyService>((_, _) => throw new Exception("search disabled"));
        var automation = new PostGameAutomation(features, party);
        await automation.RunAsync(true, true, false); await automation.RetryFailedAsync();
        Assert.Equal(1, honors); Assert.Equal(2, returns);
        automation.Pause(); await automation.RunAsync(true, true, false); Assert.True(automation.IsPaused);
        automation.NotifyPhase("EndOfGame"); automation.NotifyPhase("ChampSelect"); Assert.False(automation.IsPaused); Assert.Empty(automation.Snapshot());
    }

    [Fact]
    public async Task CompletedOldAutomationCannotRepopulateResetSession()
    {
        var pending = new TaskCompletionSource<ClientFeatureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automation = new PostGameAutomation(Service<IClientFeatureService>((_, _) => pending.Task), Service<IPartyService>((_, _) => throw new Exception()));
        var old = automation.RunAsync(true, false, false);
        automation.Reset(); pending.SetResult(ClientFeatureResult.Success("accepted old request")); await old;
        Assert.Empty(automation.Snapshot());
    }

    [Fact]
    public void BattleQueryIncludesBothGrowthTabs() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var query = new BattleQueryForm();
        var tabs = Field<AntdUI.Segmented>(query, "_viewSwitch");
        Assert.Equal(new[] { "对局记录", "数据统计", "英雄池档案", "个人复盘中心" }, tabs.Items.Cast<AntdUI.SegmentedItem>().Select(t => t.Text));
    });

    [Fact]
    public async Task ResetDiscardsLateCurrentAccountResponse()
    {
        using var folder = new Folder(); var pending = new TaskCompletionSource<PlayerProfile?>();
        var profile = new CachedPlayerProfileService(Service<IPlayerProfileService>((m, _) => m.Name == "ResetClientContext" ? null : pending.Task), folder.Path);
        var load = profile.GetCurrentAsync(); profile.ResetClientContext(); pending.SetResult(new("old", "old", "tag", 0, 1, 0, 0));
        Assert.Null(await load);
    }

    [Fact]
    public async Task OcrCorrectionPersistsByTextWithoutSavingImage()
    {
        using var folder = new Folder(); string file = System.IO.Path.Combine(folder.Path, "corrections.json");
        var inner = Service<IAugmentScanner>((_, _) => Task.FromResult(new AugmentScanResult([], "scan") { Cards = [new(0, null, "模糊文字", [new(1001, .4)])] }));
        var scanner = new AugmentReviewScanner(inner, () => [new(1001, "增幅")], file);
        await scanner.ScanAsync(); await scanner.CorrectAsync(0, 1001);
        var next = new AugmentReviewScanner(inner, () => [new(1001, "增幅")], file);
        Assert.True((await next.ScanAsync()).Cards[0].Corrected);
        Assert.Single(Directory.GetFiles(folder.Path)); Assert.DoesNotContain("模糊文字", await File.ReadAllTextAsync(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoTouchesOnlyThisApplicationAndRefusesSubsequentEdits(bool conflict)
    {
        using var folder = new Folder();
        var pages = JArray.Parse("[{\"id\":1,\"name\":\"original\",\"selectedPerkIds\":[1,2,3,4,5,6]},{\"id\":2,\"name\":\"unrelated\"}]");
        int writes = 0;
        var lcu = Sender(p => p switch
        {
            "/lol-summoner/v1/current-summoner" => "{\"puuid\":\"a\",\"summonerId\":42}",
            "/lol-gameflow/v1/gameflow-phase" => "\"Lobby\"",
            "/lol-perks/v1/pages" => pages.ToString(), "/lol-perks/v1/currentpage" => pages[0].ToString(), _ => "{}"
        }, (_, route, body) => { writes++; if (route == "/lol-perks/v1/pages/1") pages[0] = JObject.Parse(body!); });
        var inner = Service<IOpggBuildApplyService>((_, _) =>
        {
            pages[0]["name"] = "applied"; pages[0]["selectedPerkIds"] = new JArray(10, 11, 12, 13, 14, 15);
            return Task.FromResult(OpggBuildApplyResult.Success("ok"));
        });
        var service = new VerifiedBuildApplyService(inner, lcu, folder.Path);
        Assert.True((await service.ApplyPersonalRunePresetAsync(new PersonalRunePreset { ChampionId = 1, RunePerkIds = [10, 11, 12, 13, 14, 15] })).Succeeded);
        pages[1]["name"] = "later unrelated edit";
        if (conflict) pages[0]["name"] = "later edit";
        var result = await service.RestoreLastConfigurationAsync();
        Assert.Equal(!conflict, result.Succeeded); Assert.Equal(conflict ? 0 : 1, writes);
        Assert.Equal(conflict ? "later edit" : "original", pages[0].Value<string>("name"));
        Assert.Equal("later unrelated edit", pages[1].Value<string>("name"));
    }

    [Fact]
    public async Task UndoRecreatesCleanedManagedPagesAndDoesNotDuplicateThemOnRetry()
    {
        using var folder = new Folder();
        var pages = JArray.Parse("""
            [{"id":1,"name":"current","selectedPerkIds":[1,2,3,4,5,6]},
             {"id":2,"name":"LOL助手 OP.GG · 旧页","selectedPerkIds":[6,5,4,3,2,1]},
             {"id":3,"name":"manual"}]
            """);
        int posts = 0;
        var lcu = Sender(p => p switch
        {
            "/lol-summoner/v1/current-summoner" => "{\"puuid\":\"a\",\"summonerId\":42}",
            "/lol-gameflow/v1/gameflow-phase" => "\"Lobby\"",
            "/lol-perks/v1/pages" => pages.ToString(), "/lol-perks/v1/currentpage" => pages[0].ToString(), _ => "{}"
        }, (_, route, body) =>
        {
            if (route == "/lol-perks/v1/pages")
            {
                var restored = JObject.Parse(body!);
                Assert.Null(restored["id"]);
                restored["id"] = 100 + ++posts;
                pages.Add(restored);
            }
            else if (route == "/lol-perks/v1/pages/1") pages[0] = JObject.Parse(body!);
        });
        var inner = Service<IOpggBuildApplyService>((_, _) =>
        {
            pages[0]["name"] = "applied";
            pages[0]["selectedPerkIds"] = new JArray(10, 11, 12, 13, 14, 15);
            pages.RemoveAt(1);
            return Task.FromResult(OpggBuildApplyResult.Success("ok"));
        });
        var service = new VerifiedBuildApplyService(inner, lcu, folder.Path);
        Assert.True((await service.ApplyPersonalRunePresetAsync(new PersonalRunePreset { ChampionId = 1, RunePerkIds = [10, 11, 12, 13, 14, 15] })).Succeeded);
        for (int i = 0; i < 2; i++)
        {
            var result = await service.RestoreLastConfigurationAsync();
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(3, pages.Count);
            Assert.Equal(1, posts);
            Assert.Contains(pages, p => p.Value<long>("id") == 101 && p.Value<string>("name") == "LOL助手 OP.GG · 旧页");
            Assert.Contains(pages, p => p.Value<long>("id") == 3 && p.Value<string>("name") == "manual");
        }
    }

    [Theory]
    [InlineData(1100, false)]
    [InlineData(760, false)]
    [InlineData(1100, true)]
    [InlineData(760, true)]
    public void GrowthTabsRenderAndClearWhenSwitchingPlayer(int width, bool dark) => MatchListScrollingTests.OnUiThread(() =>
    {
        string original = UiTheme.Mode;
        bool originalAntd = AntdUI.Config.IsDark;
        try
        {
        UiTheme.SetMode(dark ? "Dark" : "Light");
        AntdUI.Config.IsDark = dark;
        var report = PlayerGrowthCalculator.Calculate([Game(1, true), Game(2, false)], "a");
        var service = Service<IPlayerGrowthService>((_, a) => Task.FromResult(new PlayerGrowthSnapshot((string)a[0]!, report,
            [new(1, "安妮", true, 32000, 7, true, report.Champions.Single())], ["全部", "召唤师峡谷"], DateTimeOffset.Now, false, "测试样本 · 非实际客户端画面")));
        using var window = new Form { ClientSize = new(width, 700) };
        using var control = new PlayerGrowthControl(service, Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(null)), Catalog());
        window.Controls.Add(control); window.Show(); Pump(control.ShowAsync("a", true, "全部"));
        Assert.NotNull(Field<AntdUI.Table>(control, "_table").DataSource);
        string? previews = Environment.GetEnvironmentVariable("COMPANION_UI_PREVIEWS");
        void Save(string name) { window.PerformLayout(); System.Windows.Forms.Application.DoEvents(); if (previews == null) return; Directory.CreateDirectory(previews); using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new Rectangle(Point.Empty, window.Size)); image.Save(System.IO.Path.Combine(previews, $"{name}-{width}{(dark ? "-dark" : "")}.png")); }
        Save("hero-pool"); Pump(control.ShowAsync("a", false, "全部")); Save("review");
        control.ResetPlayer(); Assert.Null(Field<AntdUI.Table>(control, "_table").DataSource); window.Close();
        }
        finally { AntdUI.Config.IsDark = originalAntd; UiTheme.SetMode(original); }
    });

    [Theory]
    [InlineData(820)]
    [InlineData(640)]
    public void PartyLayoutKeepsActionsInsideWindow(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        var service = Service<IPartyService>((m, _) => m.Name == "GetQueuesAsync"
            ? Task.FromResult<IReadOnlyList<PartyQueue>>([new(420, "单双排", true, "Available")])
            : Task.FromResult(new PartySnapshot(420, true, true, [new("self", "队长", true, "TOP"), new("friend", "好友", false, "JUNGLE")], [new("friend", "好友#123", true)], [], "测试队伍 · 非真实客户端画面")));
        using var form = new PartyHubForm(service) { Width = width }; form.Show();
        PumpUntil(() => Field<AntdUI.Label>(form, "_state").Text?.Contains("测试队伍") == true);
        var body = form.Controls.OfType<FlowLayoutPanel>().Single();
        Assert.All(body.Controls.OfType<AntdUI.Button>(), b => { Assert.True(b.Bottom <= body.ClientSize.Height); Assert.True(b.Right <= body.ClientSize.Width); });
        string? previews = Environment.GetEnvironmentVariable("COMPANION_UI_PREVIEWS");
        if (previews != null) { Directory.CreateDirectory(previews); using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(System.IO.Path.Combine(previews, $"party-{width}.png")); }
        form.Close();
    });
}
