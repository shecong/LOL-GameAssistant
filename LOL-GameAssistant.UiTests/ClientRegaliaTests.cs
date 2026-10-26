using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class ClientRegaliaTests
{
    private sealed class Sender : ILcuRequestSender
    {
        internal string Owner = "player-a", Platform = "HN1", Crest = "prestige", Banner = "lastSeasonHighestRank";
        internal int Prestige = 12, Writes;
        internal bool Accepted = true, Reflect = true, InvalidShape, ChangeOwnerOnWrite;
        internal JObject? Body;
        public Task<string?> GetStringAsync(string endpoint, CancellationToken token = default) => Task.FromResult<string?>(endpoint switch
        {
            "/lol-summoner/v1/current-summoner" => new JObject { ["puuid"] = Owner }.ToString(),
            "/lol-rso-auth/v1/authorization" => new JObject { ["currentPlatformId"] = Platform }.ToString(),
            _ => InvalidShape ? "{}" : new JObject { ["preferredCrestType"] = Crest, ["preferredBannerType"] = Banner, ["selectedPrestigeCrest"] = Prestige }.ToString()
        });
        public Task<bool> PutAsync(string endpoint, string body, CancellationToken token = default)
        {
            Assert.Equal("/lol-regalia/v2/current-summoner/regalia", endpoint);Writes++;Body = JObject.Parse(body);
            Assert.Equal(3, Body.Count);
            if (Reflect && Accepted) { Crest = (string)Body["preferredCrestType"]!;Banner = (string)Body["preferredBannerType"]!;Prestige = (int)Body["selectedPrestigeCrest"]!; }
            if (ChangeOwnerOnWrite) Owner = "player-b";
            return Task.FromResult(Accepted);
        }
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> PostAsync(string endpoint, string body, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string body, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken token = default) => throw new NotSupportedException();
    }
    [Fact]
    public async Task ApplyPreservesBannerAndPrestigeThenRestoresFirstBaseline()
    {
        var sender = new Sender();var service = new LcuClientRegaliaService(sender);
        var snapshot = await service.ReadAsync();
        Assert.True((await service.ApplyAsync(snapshot.Session, "ranked")).Verified);
        Assert.Equal("lastSeasonHighestRank", sender.Banner);Assert.Equal(12, sender.Prestige);
        await service.ReadAsync();await service.ApplyAsync(snapshot.Session, "prestige");
        await service.ApplyAsync(snapshot.Session, "ranked");
        Assert.True((await service.RestoreAsync(snapshot.Session)).Verified);Assert.Equal("prestige", sender.Crest);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountOrRegionChangeRejectsBeforeWrite(bool region)
    {
        var sender = new Sender();var service = new LcuClientRegaliaService(sender);var before = await service.ReadAsync();
        if (region) sender.Platform = "HN10";else sender.Owner = "player-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(before.Session, "ranked"));Assert.Equal(0, sender.Writes);
    }
    [Fact]
    public async Task AcceptanceWithoutMatchingReadbackIsNotSuccess()
    {
        var sender = new Sender { Reflect = false };var service = new LcuClientRegaliaService(sender);var before = await service.ReadAsync();
        Assert.False((await service.ApplyAsync(before.Session, "ranked")).Verified);Assert.Equal(1, sender.Writes);
    }
    [Fact]
    public async Task AccountChangeDuringWriteIsNotSuccess()
    {
        var sender = new Sender { ChangeOwnerOnWrite = true };var service = new LcuClientRegaliaService(sender);var before = await service.ReadAsync();
        Assert.False((await service.ApplyAsync(before.Session, "ranked")).Verified);Assert.Equal(1, sender.Writes);
    }
    [Fact]
    public async Task UnsupportedFieldsAndInvalidTierNeverWrite()
    {
        var sender = new Sender { InvalidShape = true };var service = new LcuClientRegaliaService(sender);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync("session", "CHALLENGER"));Assert.Equal(0, sender.Writes);
    }
    [Fact]
    public async Task RejectedPutIsNotSuccessEvenIfCurrentValueAlreadyMatches()
    {
        var sender = new Sender { Accepted = false, Crest = "ranked" };var service = new LcuClientRegaliaService(sender);var before = await service.ReadAsync();
        Assert.False((await service.ApplyAsync(before.Session, "ranked")).Verified);Assert.Equal(1, sender.Writes);
    }

    [Fact]
    public async Task CancellationBeforeApplyDoesNotWrite()
    {
        var sender = new Sender();var service = new LcuClientRegaliaService(sender);var before = await service.ReadAsync();
        using var canceled = new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(before.Session, "ranked", canceled.Token));
        Assert.Equal(0, sender.Writes);
    }

    [Fact]
    public void ClientBorderWorksWithoutEnablingCoreOrEnteringGame() => MatchListScrollingTests.OnUiThread(() =>
    {
        int nativeWrites = 0;
        var core = SecondPhaseRegressionTests.Service<ISkinCoreService>((method, _) =>
        {
            if (method.Name == "get_Enabled") return false;
            if (method.Name.Contains("Apply") || method.Name.Contains("GetRankBorder")) nativeWrites++;
            return null;
        });
        var settings = SecondPhaseRegressionTests.Service<IApplicationSettingsStore>((method, _) => method.Name == "Load" ? new AssistantSettings() : null);
        using var host = new Form { ClientSize = new Size(1100, 850), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, settings, clientRegalia: new LcuClientRegaliaService(new Sender()));
        host.Controls.Add(page);host.Show();System.Windows.Forms.Application.DoEvents();
        AntdUI.Button Button(string name) => Descendants(page).OfType<AntdUI.Button>().Single(control => control.Name == name);
        Button("RankBorderTab").PerformClick();Assert.True(Button("ClientBorderRead").Enabled);
        Assert.False(Button("RankBorderApply").Enabled);
        Button("ClientBorderRead").PerformClick();
        SecondPhaseRegressionTests.PumpUntil(() => Button("ClientBorderRestore").Enabled);
        var choices = Descendants(page).OfType<AntdUI.Select>().Single(control => control.Name == "ClientBorderChoices");
        choices.SelectedValue = "ranked";Button("ClientBorderApply").PerformClick();
        var status = Descendants(page).OfType<AntdUI.Label>().Single(control => control.Name == "ClientBorderStatus");
        SecondPhaseRegressionTests.PumpUntil(() => status.Text?.Contains("回读一致") == true);
        Button("ClientBorderRestore").PerformClick();SecondPhaseRegressionTests.PumpUntil(() => status.Text?.Contains("已恢复") == true);
        Assert.Equal(0, nativeWrites);
        string? preview = Environment.GetEnvironmentVariable("CLIENT_BORDER_PREVIEW");
        if (preview != null) { using var bitmap = new Bitmap(host.Width, host.Height);host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));bitmap.Save(preview); }
    });
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));
}
