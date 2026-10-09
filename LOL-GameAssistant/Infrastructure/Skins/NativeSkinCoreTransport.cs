using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed class NativeSkinCoreTransport(string directory) : ISkinCoreTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    internal static SkinCoreReply ParseReply(string json, bool outcomeUnknown = false) => JsonSerializer.Deserialize<SkinCoreReply>(json, JsonOptions)
        ?? throw new SkinCoreException("invalid_bridge_reply", outcomeUnknown);
    public async Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string host = Path.Combine(directory, "league-skin-core-host.exe");
        if (!File.Exists(host) || !File.Exists(Path.Combine(directory, "league-skin-core.dll")))
            throw new SkinCoreException("core_files_missing");
        if (arguments.Count == 0 || arguments[0] is not ("catalog" or "status" or "entry" or "restore"))
            throw new SkinCoreException("invalid_request");
        bool write = arguments[0] is "entry" or "restore";
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = directory };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new SkinCoreException("host_start_failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(9));
        try
        {
            Task<string> output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            Task<string> errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            var reply = ParseReply(await output.ConfigureAwait(false), write);
            if (!reply.Ok) throw new SkinCoreException(string.IsNullOrEmpty(reply.Error) ? "core_failed" : reply.Error,
                reply.Error == "request_outcome_unknown" || reply.Invoked);
            if (process.ExitCode != 0 || !reply.Independent || reply.OriginalRequired)
                throw new SkinCoreException("invalid_independent_reply", write);
            return reply;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (write) throw new SkinCoreException("request_outcome_unknown", true);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new SkinCoreException("request_expired");
        }
        catch (JsonException) { throw new SkinCoreException("invalid_bridge_reply", write); }
        finally { TryKill(process); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > 1100000) throw new SkinCoreException("reply_too_large", true);
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }
}
