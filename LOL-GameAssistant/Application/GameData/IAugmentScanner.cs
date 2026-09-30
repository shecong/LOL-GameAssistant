namespace LOL_GameAssistant.Application.GameData;

public interface IAugmentScanner
{
    Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed record AugmentScanResult(IReadOnlyList<int> AugmentIds, string Message, string? RecognizedText = null);
