namespace LOL_GameAssistant.Infrastructure.Pengu;

/// <summary>Initializes new installations once; interrupted downloads retry on the next launch.</summary>
public sealed class PenguFirstRunSetup
{
    private readonly string _stateFile;
    private readonly bool _existingUser;
    private readonly Func<CancellationToken, Task> _initialize;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PenguFirstRunSetup(string directory, Func<CancellationToken, Task> initialize)
    {
        _stateFile = Path.Combine(directory, "pengu-first-run.state");
        _existingUser = File.Exists(Path.Combine(directory, "pengu-loader-local.json")) ||
            File.Exists(Path.Combine(directory, "pengu-plugin.json"));
        _initialize = initialize;
    }

    public async Task<bool> EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string state = File.Exists(_stateFile) ? File.ReadAllText(_stateFile).Trim() : "";
            if (state == "complete" || (_existingUser && state != "pending")) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile, "pending");
            await _initialize(cancellationToken).ConfigureAwait(false);
            File.WriteAllText(_stateFile, "complete");
            return true;
        }
        finally { _gate.Release(); }
    }

    internal static readonly PenguVisualSettings VisualDefaults = new() { WallpaperEnabled = false };
    internal static readonly PenguClientOptions ClientDefaults = new()
    {
        OptimizedClient = false, RiotClientApi = true, InsecureMode = true
    };
}
