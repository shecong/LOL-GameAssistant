using System.Text;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class BuildComparisonForm : AntdUI.Window, IThemeAware
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AntdUI.Input _text = new() { Name = "buildComparisonText", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Text = "正在读取装备说明…" };
    internal BuildComparisonForm(OpggBuildChoices choices, IGameAssetService assets, Action<OpggBuildOption>? select = null)
    {
        Text = "比较方案与依据 · " + choices.ChampionName; ClientSize = new(840, 680); MinimumSize = new(600, 420);
        StartPosition = FormStartPosition.CenterParent; Font = new Font(UiMetrics.FontFamily, 10);
        Controls.Add(_text);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        var close = new AntdUI.Button { Name = "closeBuildComparison", Text = "关闭", AutoSize = true };
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = close;
        buttons.Controls.Add(close);
        if (select != null)
            foreach (var option in choices.Options.Take(6))
            {
                var button = new AntdUI.Button { Text = $"选择方案 {option.Order}（暂不应用）", AutoSize = true };
                button.Click += (_, _) => { select(option); Close(); }; buttons.Controls.Add(button);
            }
        Controls.Add(buttons); AntdWindowChrome.AddHeader(this); UiTheme.Apply(this);
        Shown += async (_, _) =>
        {
            try { string text = await DescribeAsync(choices, assets, _lifetime.Token); if (!IsDisposed) _text.Text = text; }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) _text.Text = "说明暂不可用：" + e.Message + "\n方案选择仍可返回原窗口完成。"; }
        };
        Disposed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
    internal static async Task<string> DescribeAsync(OpggBuildChoices choices, IGameAssetService assets, CancellationToken token)
    {
        var options = choices.Options.Take(6).ToArray();
        var ids = options.SelectMany(o => o.CoreItemIds.Concat(o.SituationalItemIds)).Where(i => i > 0).Distinct().Take(80).ToArray();
        var names = new Dictionary<int, string>(); var descriptions = new Dictionary<int, string>();
        // Limited batches share the existing asset cache and avoid flooding the UI/network.
        foreach (var batch in ids.Chunk(6))
        {
            var rows = await Task.WhenAll(batch.Select(async id =>
            {
                try { return (Id: id, Name: await assets.GetItemNameAsync(id, token), Description: await assets.GetItemDescriptionAsync(id, token)); }
                catch (OperationCanceledException) { throw; }
                catch { return (Id: id, Name: (string?)null, Description: (string?)null); }
            }));
            foreach (var row in rows) { names[row.Id] = row.Name ?? $"装备 #{row.Id}"; descriptions[row.Id] = Regex.Replace(row.Description ?? "说明未取得", "<[^>]+>", " ").Trim(); }
        }
        string Items(IEnumerable<int> items) => string.Join("、", items.Select(id => names.GetValueOrDefault(id, $"#{id}")));
        var text = new StringBuilder($"{choices.ChampionName} · {choices.PositionName} · {choices.Mode}\n来源：{(choices.Mode == "aram_mayhem" ? "aramgg / ARAMKit" : "OP.GG")} · 读取 {choices.RetrievedAt.ToLocalTime():MM-dd HH:mm}\n样本版本：{(string.IsNullOrWhiteSpace(choices.DataVersion) ? "来源未提供，不等同于当前客户端版本" : choices.DataVersion)}\n\n");
        var mostPlayed = options.OrderByDescending(o => o.Matches).FirstOrDefault();
        var bestRate = options.Where(o => o.Matches >= 100).OrderByDescending(o => o.WinRate).FirstOrDefault();
        foreach (var option in options)
        {
            text.AppendLine($"方案 {option.Order} · {(option == mostPlayed && option.Matches > 0 ? "当前列表样本最多" : "备选路线")}{(option == bestRate ? " · ≥100 场样本中胜率最高" : "")}");
            text.AppendLine(option.Matches > 0 ? $"核心路线样本 {option.Matches:N0} 场 · 胜率 {option.WinRate:0.#}%" : "样本量未提供，不据此比较可靠性。");
            text.AppendLine("核心装备：" + Items(option.CoreItemIds));
            text.AppendLine("可选装备：" + (option.SituationalItemIds.Count > 0 ? Items(option.SituationalItemIds) : "来源未提供"));
            if (mostPlayed != null && option != mostPlayed)
            {
                text.AppendLine("与样本最多路线的核心差异：" + Items(option.CoreItemIds.Except(mostPlayed.CoreItemIds)));
                text.AppendLine("符文差异：" + (option.RunePerkIds.SequenceEqual(mostPlayed.RunePerkIds) ? "相同" : "不同，具体效果可在原方案中悬停查看"));
            }
            foreach (int id in option.CoreItemIds.Distinct())
                text.AppendLine($"  {names.GetValueOrDefault(id, $"#{id}")}：{descriptions.GetValueOrDefault(id, "说明未取得")}");
            text.AppendLine("判断方式：比较上述属性与自己需要的装备效果；高胜率可能来自样本和出装时机差异，不表示每局都应照抄。\n");
        }
        text.AppendLine("统计边界：装备路线、符文和召唤师技能可能来自独立统计，不代表整套组合的联合胜率。个人方案保留在“我的符文方案”中，本窗口不覆盖个人配置。");
        return text.ToString();
    }
    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);
}
