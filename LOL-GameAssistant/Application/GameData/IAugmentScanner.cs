namespace LOL_GameAssistant.Application.GameData;

/// <summary>在本机识别当前强化选项的扫描接口。</summary>
public interface IAugmentScanner
{
    /// <summary>执行本机强化选项识别。</summary>
    Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>强化扫描识别出的标识、状态说明和原始识别文本。</summary>
public sealed record AugmentScanResult(IReadOnlyList<int> AugmentIds, string Message, string? RecognizedText = null);
