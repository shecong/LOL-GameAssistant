using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Small;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>从程序集内嵌模型加载 CPU OCR，支持单文件发布。</summary>
internal static class AugmentOcrEngine
{
    internal static PaddleOcrAll Create() => PaddleOcrAll.Load(ChineseV6SmallModels.Default, Options());

    internal static PaddleOcrOptions Options() => new()
    {
        UseDirectionClassification = false, // Game titles are upright.
        LineWorkerCount = 1,
        DetIntraOpThreads = 2,
        Detector = new PaddleOcrDetectorOptions { MaxPooledSessions = 1 },
        Recognizer = new PaddleOcrRecognizerOptions { MaxPooledSessions = 1 }
    };
}
