using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>LCU display preferences only. Does not overwrite rank statistics or game memory.</summary>
public sealed class LcuClientRegaliaService(ILcuRequestSender lcu) : IClientRegaliaService
{
    private const string Endpoint = "/lol-regalia/v2/current-summoner/regalia";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _owner = "", _session = "";
    private ClientRegaliaState? _baseline;

    public async Task<ClientRegaliaState> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { return (await ReadCurrentAsync(token)).State; }
        finally { _gate.Release(); }
    }
    public Task<ClientRegaliaResult> ApplyAsync(string session, string crestType, CancellationToken token = default)
    {
        if (crestType is not ("ranked" or "prestige")) throw new ArgumentException("只能选择客户端实际段位或等级边框。", nameof(crestType));
        return ChangeAsync(session, crestType, false, token);
    }
    public Task<ClientRegaliaResult> RestoreAsync(string session, CancellationToken token = default) => ChangeAsync(session, "", true, token);

    private async Task<ClientRegaliaResult> ChangeAsync(string session, string crestType, bool restore, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current = await ReadCurrentAsync(token);
            if (session != _session || _baseline == null) throw new InvalidOperationException("客户端账号或大区已变化，请重新读取边框状态。");
            var selected = restore ? _baseline : current.State with { CrestType = crestType };
            var body = new JObject { ["preferredCrestType"] = selected.CrestType,
                ["preferredBannerType"] = selected.BannerType, ["selectedPrestigeCrest"] = selected.PrestigeCrest };
            token.ThrowIfCancellationRequested();
            bool accepted = await lcu.PutAsync(Endpoint, body.ToString(Formatting.None), token);
            // PUT acceptance alone is never reported as visual or state success.
            var after = await ReadCurrentAsync(token);
            bool verified = accepted && current.Owner == after.Owner && session == _session &&
                selected.CrestType == after.State.CrestType && selected.BannerType == after.State.BannerType && selected.PrestigeCrest == after.State.PrestigeCrest;
            return new(after.State, verified, verified
                ? restore ? "客户端原边框偏好已恢复，接口回读一致。" : "客户端边框偏好已更新，接口回读一致；画面显示请在客户端确认。"
                : "边框结果未核验，不能视为成功；请重新读取状态。未自动重试。");
        }
        finally { _gate.Release(); }
    }

    private async Task<(string Owner, ClientRegaliaState State)> ReadCurrentAsync(CancellationToken token)
    {
        string ownerBefore = await ReadOwnerAsync(token);
        JObject regalia = Parse(await lcu.GetStringAsync(Endpoint, token));
        string ownerAfter = await ReadOwnerAsync(token);
        if (ownerBefore != ownerAfter) throw new InvalidOperationException("读取期间客户端账号或大区发生变化，请重新读取。");
        if (regalia["preferredCrestType"]?.Type != JTokenType.String || regalia["preferredBannerType"]?.Type != JTokenType.String ||
            regalia["selectedPrestigeCrest"]?.Type != JTokenType.Integer || (long)regalia["selectedPrestigeCrest"]! is < 0 or > 100000)
            throw new InvalidOperationException("当前客户端的边框字段不兼容，未执行修改。");
        if (_owner != ownerBefore || _baseline == null)
        {
            _owner = ownerBefore;_session = Guid.NewGuid().ToString("N");_baseline = null;
        }
        var state = new ClientRegaliaState(_session, (string)regalia["preferredCrestType"]!,
            (string)regalia["preferredBannerType"]!, (int)regalia["selectedPrestigeCrest"]!);
        _baseline ??= state;
        return (ownerBefore, state);
    }
    private async Task<string> ReadOwnerAsync(CancellationToken token)
    {
        JObject summoner = Parse(await lcu.GetStringAsync("/lol-summoner/v1/current-summoner", token));
        JObject auth = Parse(await lcu.GetStringAsync("/lol-rso-auth/v1/authorization", token));
        string puuid = (string?)summoner["puuid"] ?? "", platform = (string?)auth["currentPlatformId"] ?? "";
        if (puuid.Length == 0 || platform.Length == 0) throw new InvalidOperationException("请先登录 LOL 客户端并选择大区，再读取边框状态。");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(puuid + "\n" + platform)));
    }
    private static JObject Parse(string? json)
    {
        try { return JObject.Parse(json ?? ""); }
        catch (JsonException) { throw new InvalidOperationException("客户端边框接口不可用，请确认已登录且当前版本支持该接口。"); }
    }
}
