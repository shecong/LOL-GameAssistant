using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed class NativeSkinCoreTransport(string directory, bool kernelRelay = false,
    Action<SkinCoreException>? diagnostic = null) : ISkinCoreTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    // Keep validated locator RVAs alive while host processes come and go.
    // Failure to create the optional cache never prevents a normal scan.
    private static readonly Lazy<MemoryMappedFile?> ProfileCache = new(() =>
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return MemoryMappedFile.CreateOrOpen(@"Local\LeagueSkinCore.Auto.ProfileCache.v1", 16384); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    });
    internal static SkinCoreReply ParseReply(string json, bool outcomeUnknown = false) => JsonSerializer.Deserialize<SkinCoreReply>(json, JsonOptions)
        ?? throw new SkinCoreException("invalid_bridge_reply", outcomeUnknown,
            new SkinCoreFailureDetails { Stage = "reply_parse" });
    internal static SkinCoreException ReplyFailure(SkinCoreReply reply, int exitCode) => new(
        string.IsNullOrEmpty(reply.Error) ? "core_failed" : reply.Error,
        reply.Error == "request_outcome_unknown" || reply.Invoked,
        new SkinCoreFailureDetails { Stage = reply.Stage, ProfileResolution = reply.ProfileResolution, Win32 = reply.Win32, RequestId = reply.RequestId,
            Phase = reply.Phase, Pid = reply.Pid, ThreadId = reply.ThreadId, HostArchitecture = reply.HostArchitecture,
            HookTraceAvailable = reply.HookTraceAvailable, HookCallbacks = reply.HookCallbacks,
            HookMessages = reply.HookMessages, HookStage = reply.HookStage, HookWin32 = reply.HookWin32,
            WindowDiscovery = reply.WindowDiscovery,
            MessageRoute = reply.MessageRoute,
            NativeElapsedMs = reply.ElapsedMs, ExitCode = exitCode });
    public async Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        try { return await InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false); }
        catch (System.ComponentModel.Win32Exception error)
        {
            var failure = new SkinCoreException("host_start_failed", details: new SkinCoreFailureDetails
                { Stage = "host_start", Win32 = error.NativeErrorCode });
            RecordFailure(failure, arguments, clock.ElapsedMilliseconds);
            throw failure;
        }
        catch (SkinCoreException error)
        {
            RecordFailure(error, arguments, clock.ElapsedMilliseconds);
            throw;
        }
    }

    private void RecordFailure(SkinCoreException error, IReadOnlyList<string> arguments, long elapsedMs)
    {
        error.Details = (error.Details ?? new SkinCoreFailureDetails()) with {
            Command = arguments.Count > 0 ? arguments[0] : "", Backend = kernelRelay ? "kernel_relay" : "classic",
            HostPath = Path.Combine(directory, kernelRelay ? "league-skin-hybrid-host.exe" : "league-skin-core-host.exe"),
            ElapsedMs = elapsedMs, ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(), OsDescription = RuntimeInformation.OSDescription };
        if (error.Code is "core_response_timeout" or "request_expired" or "request_outcome_unknown")
        {
            try { error.Details = error.Details with { TargetProbe = SkinCoreTargetDiagnostics.Capture(error.Details) }; }
            catch { /* Best-effort metadata must not hide the original failure. */ }
        }
        // Reporting must never replace the original transport failure.
        bool expectedLoadingWait = arguments.Count > 0 && arguments[0] == "rank-border-auto" && !error.OutcomeUnknown &&
            error.Code is "game_window_not_found" or "rank_border_not_ready" or "rank_border_loading_assets_unavailable";
        try { if (!expectedLoadingWait) diagnostic?.Invoke(error); }
        catch { }
    }

    private async Task<SkinCoreReply> InvokeCoreAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string host = Path.Combine(directory, kernelRelay ? "league-skin-hybrid-host.exe" : "league-skin-core-host.exe");
        if (!File.Exists(host) || !File.Exists(Path.Combine(directory, "league-skin-core.dll")))
            throw new SkinCoreException(kernelRelay ? "hybrid_files_missing" : "core_files_missing");
        if (arguments.Count == 0 || arguments[0] is not ("catalog" or "status" or "profile" or "entry" or "restore" or "object-catalog" or "objects" or "objects-restore" or "module-state" or "camera-status" or "camera" or "camera-view" or "rank-border" or "rank-border-catalog" or "rank-border-auto" or "driver-status") ||
            (arguments[0] == "driver-status" && !kernelRelay))
            throw new SkinCoreException("invalid_request");
        bool write = arguments[0] is "entry" or "restore" or "objects" or "objects-restore" or "camera" or "camera-view" or "rank-border" or "rank-border-auto";
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = directory };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ProfileCache.Value;
        using var process = Process.Start(info) ?? throw new SkinCoreException("host_start_failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(9));
        try
        {
            Task<string> output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            Task<string> errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            var reply = ParseReply(await output.ConfigureAwait(false), write);
            if (!reply.Ok) throw ReplyFailure(reply, process.ExitCode);
            if (process.ExitCode != 0 || !reply.Independent || reply.OriginalRequired)
                throw new SkinCoreException("invalid_independent_reply", write);
            if (arguments[0] != "module-state" && arguments[0] != "driver-status" && reply.BridgeProtocol != 2)
                throw new SkinCoreException("bridge_version_mismatch", write);
            if (kernelRelay && arguments[0] != "module-state" && reply.Backend != "kernel_relay")
                throw new SkinCoreException("invalid_kernel_reply", write);
            return reply;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (write) throw new SkinCoreException("request_outcome_unknown", true,
                new SkinCoreFailureDetails { Stage = "host_wait" });
            if (cancellationToken.IsCancellationRequested) throw;
            throw new SkinCoreException("host_response_timeout", details: new SkinCoreFailureDetails { Stage = "host_wait" });
        }
        catch (JsonException) { throw new SkinCoreException("invalid_bridge_reply", write,
            new SkinCoreFailureDetails { Stage = "reply_parse", ExitCode = process.HasExited ? process.ExitCode : null }); }
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
