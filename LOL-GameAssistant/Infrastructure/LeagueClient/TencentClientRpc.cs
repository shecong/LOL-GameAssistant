using LOL_GameAssistant.Helper;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

internal sealed record TencentClientIdentity(string AccountType, string Uin, int ZoneId);
internal sealed record TencentRewardSignature(string Nonce, long Time, string Token)
{
    public override string ToString() => "TencentRewardSignature [redacted]";
}
internal interface ITencentClientRpc
{
    Task<TencentClientIdentity> GetIdentityAsync(CancellationToken token);
    Task<TencentRewardSignature> SignRewardAsync(string stage, TencentClientIdentity expected, CancellationToken token);
}

/// <summary>复用官方 rcp-fe-rpcs 的本机协议，仅开放读取身份及奖励签名两个命令。</summary>
internal sealed class TencentClientRpc(Func<int?> lcuPort) : ITencentClientRpc
{
    private int? _lastPort;
    private int? _lastLcuPort;

    public async Task<TencentClientIdentity> GetIdentityAsync(CancellationToken token)
    {
        using var socket = await ConnectAsync(token).ConfigureAwait(false);
        return ParseIdentity(await CallAsync(socket, 4, "", "0", token).ConfigureAwait(false));
    }

    public async Task<TencentRewardSignature> SignRewardAsync(string stage, TencentClientIdentity expected, CancellationToken token)
    {
        if (!int.TryParse(stage, out int number) || number is < 0 or > 31 || number.ToString(CultureInfo.InvariantCulture) != stage)
            throw new InvalidOperationException("魄罗礼包阶段无效");
        using var socket = await ConnectAsync(token).ConfigureAwait(false);
        // 在同一连接上核对账号，再请求签名，避免切号后签给旧账号。
        var actual = ParseIdentity(await CallAsync(socket, 4, "", "0", token).ConfigureAwait(false));
        if (actual != expected) throw new InvalidOperationException("客户端账号已变化，请重新扫描");
        string nonce = RandomNumberGenerator.GetInt32(1_000_000_000).ToString("D10", CultureInfo.InvariantCulture);
        long time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string payload = string.Join('\0', time.ToString(CultureInfo.InvariantCulture), nonce, stage);
        string signature = await CallAsync(socket, 6, payload, "1", token).ConfigureAwait(false);
        if (signature.Length != 32 || signature.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("客户端未返回有效的奖励签名");
        return new(nonce, time, signature);
    }

    private async Task<TrafficWebSocket> ConnectAsync(CancellationToken token)
    {
        int current = lcuPort() ?? throw new InvalidOperationException("未连接英雄联盟客户端");
        if (current is < 1 or > 65535) throw new InvalidOperationException("客户端端口无效");
        var candidates = CandidatePorts(current).ToList();
        if (_lastLcuPort == current && _lastPort.HasValue) candidates.Insert(0, _lastPort.Value);
        foreach (int port in candidates.Distinct())
        {
            var transport = new ClientWebSocket();
            var socket = new TrafficWebSocket(NetworkModule.ClientRpc, transport);
            // 地址始终固定为 loopback；不对外部服务放宽证书校验，也不发送 LCU 密码。
            transport.Options.Proxy = null;
            transport.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            transport.Options.SetRequestHeader("Origin", $"https://127.0.0.1:{current}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(650));
            try
            {
                await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{port}"), timeout.Token).ConfigureAwait(false);
                _lastPort = port; _lastLcuPort = current;
                return socket;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { socket.Dispose(); throw; }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or HttpRequestException)
            { socket.Dispose(); }
        }
        throw new InvalidOperationException("客户端 RPCS 签名服务暂不可用");
    }

    internal static IReadOnlyList<int> CandidatePorts(int lcuPort)
    {
        // 与 rcp-fe-rpcs 的两个端口生成器一致，首个值会被官方脚本跳过。
        int value = 30000;
        var generated = new List<int>();
        for (int i = 0; i < 6; i++) { value += 7 * value % 19 + 1; if (i > 0) generated.Add(value); }
        return generated.Select(p => p + lcuPort % 1000).Concat(generated).Distinct().ToArray();
    }

    internal static TencentClientIdentity ParseIdentity(string value)
    {
        string[] fields = value.Split('\0');
        if (fields.Length != 4 || fields[0] != "1" || fields[1].Length == 0 || !fields[1].All(char.IsAsciiDigit)
            || !int.TryParse(fields[2], out int zone) || zone <= 0)
            throw new InvalidOperationException("未取得可核对的国服 QQ 账号身份");
        return new(fields[0], fields[1].TrimStart('0'), zone);
    }

    internal static string CreateCallFrame(int command, string callback, string value) =>
        "\u0002" + (char)command + callback + "\0" + value;

    internal static string? ReadCallback(string frame, string callback)
    {
        // 原生回包的第二个字符不是请求命令；只按官方协议的 callback id 匹配。
        if (frame.Length < 4 || frame[0] != 2) return null;
        int delimiter = frame.IndexOf('\0', 2);
        return delimiter >= 0 && frame[2..delimiter] == callback ? frame[(delimiter + 1)..] : null;
    }

    private static async Task<string> CallAsync(TrafficWebSocket socket, int command, string value, string callback, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        byte[] request = Encoding.UTF8.GetBytes(CreateCallFrame(command, callback, value));
        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
        byte[] buffer = new byte[4096];
        long receivedBytes = 0;
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 65536)
                    throw new InvalidOperationException("客户端 RPCS 响应格式异常");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            receivedBytes += message.Length;
            if (receivedBytes > 2_097_152) throw new InvalidOperationException("客户端 RPCS 推送数据超出限制");
            string? response = ReadCallback(Encoding.UTF8.GetString(message.ToArray()), callback);
            if (response != null) return response;
            // 其它原生命令、推送不执行，避免影响客户端界面或流程。
        }
    }
}
