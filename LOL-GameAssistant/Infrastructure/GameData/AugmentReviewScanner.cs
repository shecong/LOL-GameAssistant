using LOL_GameAssistant.Application.GameData;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>只保存用户确认的卡片文本匹配修正，不保存截图或替用户选择游戏内增幅。</summary>
internal sealed class AugmentReviewScanner(IAugmentScanner inner, Func<IReadOnlyList<AugmentCatalogChoice>> catalog, string? file = null) : IAugmentScanner, IAugmentReviewService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "augment-corrections.json");
    private Dictionary<string, int>? _corrections;
    public AugmentScanResult? LastScan { get; private set; }
    public IReadOnlyList<AugmentCatalogChoice> Choices => catalog();
    private static string Key(AugmentCardScan card) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(card.Text.Trim())));
    private async Task Load(CancellationToken token)
    {
        if (_corrections != null) return;
        try { _corrections = JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(_file, token).ConfigureAwait(false)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { _corrections = []; }
    }
    public async Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = await inner.ScanAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Load(cancellationToken).ConfigureAwait(false);
            var cards = result.Cards.Select(c => !string.IsNullOrWhiteSpace(c.Text) && _corrections!.TryGetValue(Key(c), out int id) && Choices.Any(x => x.Id == id)
                ? c with { Id = id, Corrected = true } : c).ToArray();
            LastScan = cards.Length == 0 ? result : result with { Cards = cards, AugmentIds = cards.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).Distinct().ToArray() };
            return LastScan;
        }
        finally { _gate.Release(); }
    }
    public async Task<AugmentScanResult> CorrectAsync(int position, int id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Load(token).ConfigureAwait(false);
            if (LastScan == null || !Choices.Any(c => c.Id == id)) throw new InvalidOperationException("请先扫描，并从本机增幅名称表选择有效项目");
            var card = LastScan.Cards.FirstOrDefault(c => c.Position == position);
            if (card == null || string.IsNullOrWhiteSpace(card.Text)) throw new InvalidOperationException("该卡片没有可关联的识别文本，请重新扫描");
            if (_corrections!.Count >= 512) _corrections.Clear(); _corrections[Key(card)] = id;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!); string temp = _file + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_corrections), token).ConfigureAwait(false); File.Move(temp, _file, true);
            var cards = LastScan.Cards.Select(c => c.Position == position ? c with { Id = id, Corrected = true } : c).ToArray();
            return LastScan = LastScan with { Cards = cards, AugmentIds = cards.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).Distinct().ToArray(), Message = "本地名称校正已保存，下次相同文本扫描会沿用；未操作游戏内卡片" };
        }
        finally { _gate.Release(); }
    }
}
