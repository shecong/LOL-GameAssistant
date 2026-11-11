using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.Settings;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public class GameChatWidthCalibratorTests
{
    [Fact]
    public async Task FailedSendDoesNotScanOrOverwriteSettings()
    {
        var store = new MemoryStore();
        var result = await new GameChatWidthCalibrator(store).CalibrateAsync((_, _) =>
            Task.FromResult(new GameShoutSendResult(false, "未找到游戏")));
        Assert.False(result.Succeeded);
        Assert.Equal("未找到游戏", result.Message);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task ConcurrentCalibrationDoesNotSendSecondProbe()
    {
        var store = new MemoryStore();
        var calibrator = new GameChatWidthCalibrator(store);
        var pending = new TaskCompletionSource<GameShoutSendResult>();
        var first = calibrator.CalibrateAsync((_, _) => pending.Task);
        var second = await calibrator.CalibrateAsync((_, _) => throw new Exception("Must not send"));
        Assert.False(second.Succeeded);
        pending.SetResult(new(false, "stopped"));
        await first;
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void CalibrationSurvivesLegacyJsonRoundTripWithoutChangingOldBoolean()
    {
        var domain = new AssistantSettings
        {
            GameChatFirstLineWidth = 42,
            GameChatContinuationLineWidth = 88,
            GameKdaOnePlayerPerLine = true
        };
        var json = JsonConvert.SerializeObject(LegacySettingsMapper.ToLegacy(domain));
        var restored = LegacySettingsMapper.ToDomain(JsonConvert.DeserializeObject<SettingConfig>(json)!);
        Assert.Equal(42, restored.GameChatFirstLineWidth);
        Assert.Equal(88, restored.GameChatContinuationLineWidth);
        Assert.True(restored.GameKdaOnePlayerPerLine);
        var old = LegacySettingsMapper.ToDomain(JsonConvert.DeserializeObject<SettingConfig>("{\"gameKdaOnePlayerPerLine\":true}")!);
        Assert.Equal(30, old.GameChatFirstLineWidth);
        Assert.Equal(70, old.GameChatContinuationLineWidth);
        Assert.True(old.GameKdaOnePlayerPerLine);
    }

    [Fact]
    public void LocalOcrRecognizesWrappedCalibrationOnDarkChatBackground()
    {
        const string digits = "0123456789264810357968520914375913086247852149076349861302574927041863598014627539860415720394726185306948201753695284016374189532076";
        string probe = string.Concat(digits.Select(c => (char)(c + 0xfee0)));
        using var image = new System.Drawing.Bitmap(1100, 300, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = System.Drawing.Graphics.FromImage(image))
        using (var font = new System.Drawing.Font("Microsoft YaHei", 22, System.Drawing.GraphicsUnit.Pixel))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(12, 22, 32));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            string[] rows = [probe[..15], probe[15..50], probe[50..85], probe[85..120], probe[120..]];
            for (int i = 0; i < rows.Length; i++)
                graphics.DrawString(rows[i], font, System.Drawing.Brushes.White, i == 0 ? 460 : 20, 20 + i * 45);
        }
        using var engine = AugmentOcrEngine.Create();
        var result = AugmentImageRecognizer.ReadImage(image, engine);
        var recognized = result.Lines.Where(line => line.RecognitionScore >= .85f)
            .OrderBy(line => line.Box.Y1).Select(line => line.Text).ToArray();
        Assert.Equal(new LOL_GameAssistant.Domain.MatchAnalysis.GameChatLineWidths(30, 70),
            LOL_GameAssistant.Domain.MatchAnalysis.GameChatWidthCalibration.Measure(probe, recognized));
    }

    private sealed class MemoryStore : IApplicationSettingsStore
    {
        public int Saves { get; private set; }
        public AssistantSettings Load() => new();
        public void Save(AssistantSettings settings) => Saves++;
        public string GetStoragePath() => "unused";
    }
}
