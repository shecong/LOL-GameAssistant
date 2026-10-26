using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class ClientIdentityTests
{
    [Fact]
    public async Task HttpTransportRetainsOnlySafeServerRejectionCategory()
    {
        string category=HttpClientHelper.ClassifyServerError("Player does not own REGALIA_BANNER; private-account-id");
        Assert.Equal("not_owned",category);
        var sender=new LcuHttpRequestSender((_,_,_,_)=>Task.FromResult(new LcuHttpResult(null,HttpStatusCode.BadRequest,LcuHttpFailure.HttpStatus){ServerErrorCode=category}));
        var response=await sender.SendAsync("POST","/test","{}");Assert.Equal("not_owned",response.ServerErrorCode);Assert.Null(response.Data);
    }
    internal sealed class Sender : ILcuResultRequestSender
    {
        internal string Owner = "account-a", Region = "HN1";
        internal JObject Prefs = new() { ["title"] = "11", ["bannerAccent"] = "1", ["crestBorder"] = "1", ["prestigeCrestBorderLevel"] = 75, ["challengeIds"] = new JArray(101, 303) };
        internal JObject Regalia = new() { ["preferredCrestType"] = "prestige", ["preferredBannerType"] = "blank", ["selectedPrestigeCrest"] = 4 };
        internal readonly List<(string Method, string Path, JObject Body)> Writes = [];
        internal bool Reject, FilterBadge, ChangeOwner, NetworkFailure;
        public Task<string?> GetStringAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            JToken? value = path switch {
                "/lol-summoner/v1/current-summoner" => new JObject { ["puuid"] = Owner, ["summonerLevel"] = 75 },
                "/lol-rso-auth/v1/authorization" => new JObject { ["currentPlatformId"] = Region },
                "/lol-regalia/v2/current-summoner/regalia" => Regalia,
                "/lol-challenges/v1/summary-player-data/local-player" => new JObject {
                    ["title"] = new JObject { ["itemId"] = Prefs["title"]!.DeepClone() }, ["selectedChallengesString"] = string.Join(',', Prefs["challengeIds"]!.Values<long>()),
                    ["bannerId"] = Prefs["bannerAccent"]!.DeepClone(), ["crestId"] = Prefs["crestBorder"]!.DeepClone(), ["prestigeCrestBorderLevel"] = Prefs["prestigeCrestBorderLevel"]!.DeepClone() },
                "/lol-game-data/assets/v1/regalia.json" => JArray.Parse("""[{"regaliaType":"kBanner","id":"1","localizedName":"默认旗帜","assetPath":"/lol-game-data/assets/default.png"},{"regaliaType":"kBanner","id":"2","localizedName":"未获取旗帜","assetPath":"/lol-game-data/assets/locked.png"}]"""),
                "/lol-regalia/v3/inventory/REGALIA_BANNER" => JObject.Parse("""{"1":{"isOwned":true,"items":[]},"2":{"isOwned":false,"items":[]}}"""),
                "/lol-challenges/v2/titles/all" => JObject.Parse("""{"a":{"itemId":11,"name":"已有头衔"},"b":{"itemId":12,"name":"未获取头衔"}}"""),
                "/lol-challenges/v2/titles/local-player" => JArray.Parse("""[{"itemId":11}]"""),
                "/lol-game-data/assets/v1/challenges.json" => JObject.Parse("""{"challenges":{"101":{"name":"已有勋章","levelToIconPath":{"GOLD":"/lol-game-data/assets/a.png"}},"202":{"name":"未获取勋章","levelToIconPath":{"IRON":"/lol-game-data/assets/b.png"}}}}"""),
                "/lol-challenges/v1/challenges/local-player" => JObject.Parse("""{"101":{"currentLevel":"GOLD"},"202":{"currentLevel":"NONE"}}"""),
                _ => null
            };
            return Task.FromResult(value?.ToString());
        }
        public Task<LcuResponse<byte[]>> SendAsync(string method, string path, string? body = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();var json = JObject.Parse(body!);Writes.Add((method, path, json));
            if (NetworkFailure)return Task.FromResult(new LcuResponse<byte[]>(null, null, LcuFailure.Network));
            if (Reject)return Task.FromResult(new LcuResponse<byte[]>(Encoding.UTF8.GetBytes("Player does not own; private-account-id"), HttpStatusCode.BadRequest, LcuFailure.HttpStatus));
            if (method == "PUT")Regalia = (JObject)json.DeepClone();
            else {
                Prefs = (JObject)json.DeepClone();
                if (FilterBadge)Prefs["challengeIds"] = new JArray(Prefs["challengeIds"]!.Values<long>().Where(x => x != 202));
            }
            if (ChangeOwner)Owner = "account-b";
            return Task.FromResult(new LcuResponse<byte[]>([], HttpStatusCode.NoContent, LcuFailure.None));
        }
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [Fact]
    public async Task FullCatalogIncludesUnownedAndSeparatesTheirOwnership()
    {
        var sender = new Sender();var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();
        Assert.Equal(21, catalog.Choices.Count(x => x.Category == ClientIdentityCategory.Border));
        Assert.Contains(catalog.Choices, x => x.Category == ClientIdentityCategory.Border && x.Id == "21" && x.Owned == false);
        foreach (var category in new[] { ClientIdentityCategory.Title, ClientIdentityCategory.Banner, ClientIdentityCategory.Badge })
            Assert.Contains(catalog.Choices, x => x.Category == category && x.Owned == false);
        Assert.Empty(sender.Writes);
    }
    [Fact]
    public async Task UnearnedLevelBorderUsesVerifiedLevelPreferenceAndRestoresAllOriginalFields()
    {
        var sender = new Sender();var before = sender.Prefs.DeepClone();var originalRegalia = sender.Regalia.DeepClone();
        var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();
        var result = await service.ApplyAsync(catalog.State.Session, ClientIdentityCategory.Border, "21");
        Assert.True(result.Verified);Assert.Equal(500, result.State.BorderLevel);Assert.Equal(4, result.State.Border);
        Assert.Single(sender.Writes);Assert.Equal("POST", sender.Writes[0].Method);
        Assert.Equal("11", (string?)sender.Prefs["title"]);Assert.Equal(new long[] { 101, 303 }, sender.Prefs["challengeIds"]!.Values<long>());
        Assert.True((await service.RestoreAsync(catalog.State.Session)).Verified);
        Assert.True(JToken.DeepEquals(before, sender.Prefs));Assert.True(JToken.DeepEquals(originalRegalia, sender.Regalia));
    }
    [Theory]
    [InlineData(ClientIdentityCategory.Title,"12")]
    [InlineData(ClientIdentityCategory.Banner,"2")]
    public async Task UnownedChoiceIsSubmittedButServerRejectionIsAccurateAndRedacted(ClientIdentityCategory category,string id)
    {
        var sender = new Sender();var logs = new List<string>();var service = new LcuClientIdentityService(sender, logs.Add);
        var catalog = await service.ReadAsync();sender.Reject = true;
        var result = await service.ApplyAsync(catalog.State.Session, category, id);
        Assert.False(result.Verified);Assert.Equal("not_owned", result.Code);Assert.Single(sender.Writes);
        Assert.DoesNotContain("private-account-id", string.Join('\n', logs));Assert.Equal("11", (string?)sender.Prefs["title"]);
    }
    [Fact]
    public async Task FilteredBadgeRestoresPrecedingSlotsWithoutRepeatingFailedSelection()
    {
        var sender = new Sender { FilterBadge = true };var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();
        var result = await service.ApplyAsync(catalog.State.Session, ClientIdentityCategory.Badge, "202");
        Assert.False(result.Verified);Assert.Equal("readback_reverted", result.Code);Assert.Contains("已恢复",result.Message);
        Assert.Equal(new long[] { 101, 303 }, result.State.Badges);Assert.Equal(2,sender.Writes.Count);
        Assert.DoesNotContain(202L,sender.Writes[1].Body["challengeIds"]!.Values<long>());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedAccountOrRegionNeverReceivesOldSelectionOrRestore(bool region)
    {
        var sender = new Sender();var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();
        if(region)sender.Region="HN10";else sender.Owner="account-b";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(catalog.State.Session,ClientIdentityCategory.Banner,"2"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RestoreAsync(catalog.State.Session));Assert.Empty(sender.Writes);
    }
    [Fact]
    public async Task UnknownTransportOutcomeNeverReplaysOrRestoresAutomatically()
    {
        var sender = new Sender();var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();sender.NetworkFailure=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(catalog.State.Session,ClientIdentityCategory.Title,"12"));Assert.Single(sender.Writes);
    }
    [Fact]
    public async Task AccountChangeDuringWriteStopsWithoutTouchingNewAccount()
    {
        var sender = new Sender();var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();sender.ChangeOwner=true;
        var result=await service.ApplyAsync(catalog.State.Session,ClientIdentityCategory.Title,"12");
        Assert.False(result.Verified);Assert.Equal("session_changed",result.Code);Assert.Single(sender.Writes);
    }
    [Fact]
    public async Task InvalidSelectionAndCancelledRequestNeverWrite()
    {
        var sender = new Sender();var service = new LcuClientIdentityService(sender);var catalog = await service.ReadAsync();
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ApplyAsync(catalog.State.Session,ClientIdentityCategory.Banner,"invented"));
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ApplyAsync(catalog.State.Session,ClientIdentityCategory.Title,"12",token:canceled.Token));Assert.Empty(sender.Writes);
    }
    [Fact]
    public void FullBrowserLeavesUnownedItemsSelectableUntilExplicitApply() => MatchListScrollingTests.OnUiThread(() => {
        var sender=new Sender();var service=new LcuClientIdentityService(sender);
        using var form=new ClientIdentityForm(service);form.Show();System.Windows.Forms.Application.DoEvents();
        var categories=(AntdUI.Segmented)form.Controls.Find("IdentityCategories",true).Single();categories.SelectIndex=3;
        var locked=(AntdUI.Button)form.Controls.Find("IdentityChoice_Banner_2",true).Single();Assert.True(locked.Enabled);locked.PerformClick();
        Assert.Empty(sender.Writes);Assert.True(((AntdUI.Button)form.Controls.Find("IdentityApply",true).Single()).Enabled);
        string? snapshot=Environment.GetEnvironmentVariable("CODEX_IDENTITY_UI_SNAPSHOT");
        if(!string.IsNullOrWhiteSpace(snapshot)){for(int i=0;i<10;i++){System.Windows.Forms.Application.DoEvents();Thread.Sleep(25);}using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save(snapshot);}
        form.Close();
    });
}
