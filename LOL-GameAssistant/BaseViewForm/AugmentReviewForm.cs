using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class AugmentReviewForm : AntdUI.Window
{
    private readonly UiRequestScope _requests = new();
    internal AugmentReviewForm(IAugmentScanner scanner, IAugmentReviewService review)
    {
        Text = "增幅识别校正"; Size = new Size(850, 620); MinimumSize = new Size(640, 470); StartPosition = FormStartPosition.CenterParent;
        var table = new AntdUI.Table { Dock = DockStyle.Fill, Bordered = true };
        foreach (var (key, title) in new[] { ("Position", "卡片位置"), ("Name", "匹配结果"), ("Score", "匹配分数（不是概率）"), ("Text", "识别文本") }) table.Columns.Add(new AntdUI.Column(key, title) { Width = key == "Text" ? "40%" : "20%" });
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), WrapContents = true };
        var scan = new AntdUI.Button { Text = "重新扫描三张卡片", AutoSize = true };
        var position = new AntdUI.Select { Width = 100, Height = 36, List = true, DropDownArrow = true }; position.Items.AddRange(new object[] { "左侧", "中间", "右侧" }); position.SelectedIndex = 0;
        var name = new AntdUI.Select { Width = 290, Height = 36, List = true, DropDownArrow = true };
        foreach (var c in review.Choices) name.Items.Add(new Choice(c.Id, c.Name));
        var correct = new AntdUI.Button { Text = "确认 / 校正名称", AutoSize = true }; bar.Controls.AddRange([scan, position, name, correct]);
        var status = new AntdUI.Label { Dock = DockStyle.Bottom, Height = 70, TextMultiLine = true, Padding = new Padding(12), Text = "只修正本地名称匹配，不自动选取游戏内增幅。识别不确定与统计无样本是不同状态。" };
        void Render()
        {
            table.DataSource = (review.LastScan?.Cards ?? []).Select(c => new Row(new[] { "左侧", "中间", "右侧" }[Math.Clamp(c.Position, 0, 2)],
                c.Id.HasValue ? review.Choices.FirstOrDefault(x => x.Id == c.Id)?.Name ?? c.Id.ToString()! : "不确定",
                c.Corrected ? "人工校正" : c.Candidates.FirstOrDefault()?.MatchScore.ToString("0.00") ?? "—", c.Text)).ToArray();
        }
        bool busy = false;
        scan.Click += async (_, _) => { if (busy) return; busy = true; try { var result = await scanner.ScanAsync(_requests.Token); if (!IsDisposed) { status.Text = result.Message; Render(); } } catch (OperationCanceledException) { } catch (Exception e) { if (!IsDisposed) status.Text = e.Message; } finally { busy = false; } };
        correct.Click += async (_, _) => { if (busy || name.SelectedValue is not Choice c) return; busy = true; try { var result = await review.CorrectAsync(position.SelectedIndex, c.Id, _requests.Token); if (!IsDisposed) { status.Text = result.Message; Render(); } } catch (OperationCanceledException) { } catch (Exception e) { if (!IsDisposed) status.Text = e.Message; } finally { busy = false; } };
        Controls.Add(table); Controls.Add(bar); Controls.Add(status); AntdWindowChrome.AddHeader(this); Render(); Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
    private sealed record Choice(int Id, string Name) { public override string ToString() => Name; }
    private sealed record Row(string Position, string Name, string Score, string Text);
}
