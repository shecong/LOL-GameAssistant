using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class CoachForm
{
    private readonly AntdUI.Label _buildStatus = new() { AutoSize = true };
    private readonly AntdUI.Label _summary = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
    private readonly AntdUI.Label _analysisStatus = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 4, 12, 8) };
    private readonly Dictionary<string, DateTimeOffset> _shownAdvice = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedAdvice = new(StringComparer.Ordinal);
    private int _overlaySessionVersion;
    private string _overlayRecommendationId = "";
    private readonly AntdUI.Button _reviewAugments = new() { Text = "确认增幅候选", AutoSize = true, Visible = false };
    private readonly AntdUI.Select _matchupPicker = new() { Name = "coachMatchup", List = true, DropDownArrow = true, Width = 180, Height = UiMetrics.ControlHeight };
    private readonly FlowLayoutPanel _cardsPanel = new() { Name = "coachCards", Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
    private readonly Panel _dataPanel = new() { Dock = DockStyle.Bottom, Height = 160, Visible = false, Padding = new Padding(12, 0, 12, 8) };
    private bool _updatingMatchup;
    private string _cardsSignature = "";
    private bool _showSecondaryAdvice;
    private readonly AntdUI.Button _moreAdvice = new() { Name = "moreAdvice", Text = "展开后续建议", AutoSize = true, Visible = false };

    private void BuildUi()
    {
        Font = new Font(UiMetrics.FontFamily, 10);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(12, 8, 12, 4) };
        header.Controls.Add(_refresh);
        var history = new AntdUI.Button { Text = "本局建议记录", AutoSize = true };
        history.Click += (_, _) => ShowAdviceHistory();
        header.Controls.Add(history);
        header.Controls.Add(_moreAdvice);
        _moreAdvice.Click += (_, _) =>
        {
            _showSecondaryAdvice = !_showSecondaryAdvice;
            for (int i = 0; i < _cardsPanel.Controls.Count; i++) _cardsPanel.Controls[i].Visible = i == 0 || _showSecondaryAdvice;
            _moreAdvice.Text = _showSecondaryAdvice ? "只看当前重点" : "展开后续建议";
        };
        var data = new AntdUI.Button { Text = "本次使用的数据 ▾", AutoSize = true };
        data.Click += (_, _) => { _dataPanel.Visible = !_dataPanel.Visible; data.Text = _dataPanel.Visible ? "收起数据 ▴" : "本次使用的数据 ▾"; };
        header.Controls.Add(data);
        var settings = new AntdUI.Button { Text = "AI 设置", AutoSize = true };
        settings.Click += (_, _) => Program.GameMain?.OpenAiRecommendationSettings();
        header.Controls.Add(settings);
        header.Controls.Add(_reviewAugments);
        _reviewAugments.Click += async (_, _) =>
        {
            using var review = new AugmentReviewForm(Bootstrap.AppCompositionRoot.AugmentScanner, Bootstrap.AppCompositionRoot.AugmentReview);
            review.ShowDialog(FindForm());
            if (!IsDisposed) await _recommendationCoordinator.RefreshAsync(force: true);
        };
        _status.AutoSize = false; _status.Dock = DockStyle.Top; _status.Height = 28; _status.Padding = new Padding(12, 0, 12, 0);
        var context = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(12, 0, 12, 6) };
        context.Controls.Add(_summary);
        context.Controls.Add(new AntdUI.Label { Text = "确认对线对象", AutoSize = true, Margin = new Padding(12, 8, 8, 0) });
        context.Controls.Add(_matchupPicker);
        _matchupPicker.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingMatchup) _recommendationCoordinator.ConfirmMatchup(_matchupPicker.SelectedIndex <= 0 ? null : _matchupPicker.SelectedValue?.ToString());
        };
        var builds = new FlowLayoutPanel { Name = "coachBuildConfiguration", Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(12, 6, 12, 6) };
        builds.Controls.Add(new AntdUI.Label { Text = "选人配置", AutoSize = true, Margin = new Padding(0, 10, 12, 0) });
        builds.Controls.Add(_applyOpgg); builds.Controls.Add(_myRunes); builds.Controls.Add(_buildStatus);
        _dataPanel.Controls.Add(_validation);
        _recommendation.Dock = DockStyle.None;
        _recommendation.Height = 120;
        _cardsPanel.SizeChanged += (_, _) => SizeAdviceCards();
        Controls.Add(_cardsPanel); Controls.Add(_dataPanel); Controls.Add(builds); Controls.Add(_analysisStatus); Controls.Add(_status); Controls.Add(context); Controls.Add(header);
    }

    private void RenderAdviceCards(RecommendationState state)
    {
        var c = state.Context;
        _moreAdvice.Visible = state.Recommendations.Count > 1;
        _reviewAugments.Visible = c.Phase == "InProgress" && c.Mode.Contains("海克斯", StringComparison.Ordinal);
        _analysisStatus.Text = state.Diagnostic;
        _summary.Text = $"{c.MyChampion} · {c.MyRole} · {c.Mode} · {c.GameTimeText}";
        _updatingMatchup = true;
        try
        {
            string[] options = new[] { "对线英雄未确认" }.Concat(c.EnemyChampions.Distinct()).ToArray();
            if (!_matchupPicker.Items.Cast<string>().SequenceEqual(options))
            { _matchupPicker.Items.Clear(); _matchupPicker.Items.AddRange(options); }
            int selected = Array.IndexOf(options, c.MatchupChampion);
            _matchupPicker.SelectedIndex = Math.Max(0, selected);
            _matchupPicker.Enabled = !c.IsAram && c.EnemyChampions.Count > 0;
        }
        finally { _updatingMatchup = false; }
        string signature = string.Join("|", state.Recommendations.Select(card => card.Id + card.ContextVersion + card.GeneratedAt.Ticks)) +
            string.Join("|", state.History.Select(h => h.Feedback)) + (state.Recommendations.Count == 0 ? state.Status.ToString() : "");
        if (signature == _cardsSignature) return;
        _cardsSignature = signature;
        _expandedAdvice.IntersectWith(state.Recommendations.Select(card => card.Id));
        _cardsPanel.SuspendLayout();
        try
        {
            // 控件清理时保留由页面统一持有的空状态输入框。
            _cardsPanel.Controls.Remove(_recommendation);
            foreach (Control old in _cardsPanel.Controls.Cast<Control>().ToArray()) old.Dispose();
            _cardsPanel.Controls.Clear();
            if (state.Recommendations.Count == 0) _cardsPanel.Controls.Add(_recommendation);
            else
                for (int i = 0; i < state.Recommendations.Count; i++)
                {
                    var card = CreateAdviceCard(state.Recommendations[i], i, state);
                    card.Visible = i == 0 || _showSecondaryAdvice;
                    _cardsPanel.Controls.Add(card);
                }
            SizeAdviceCards();
        }
        finally { _cardsPanel.ResumeLayout(true); }
        ApplyAdviceTheme(UiTheme.Palette);
    }

    private Control CreateAdviceCard(CoachRecommendation card, int index, RecommendationState state)
    {
        var panel = new TableLayoutPanel { Name = index == 0 ? "coachPrimaryCard" : "coachSecondaryCard", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 12), BorderStyle = BorderStyle.FixedSingle };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var eyebrow = AdviceLabel((index == 0 ? "当前重点" : "接下来关注") +
            (card.Source == RecommendationSource.LocalObservation ? " · 本机状态提醒" : " · AI 战术计划"), true);
        panel.Controls.Add(eyebrow);
        var title = AdviceLabel($"{GetPriorityName(card.Priority)} · {card.Category}　{card.Title}", true);
        title.Font = new Font(UiMetrics.FontFamily, index == 0 ? 13 : 11, FontStyle.Bold);
        panel.Controls.Add(title);
        if (card.Steps.Count > 0)
            for (int step = 0; step < card.Steps.Count; step++) panel.Controls.Add(AdviceLabel($"{step + 1}. {card.Steps[step]}"));
        else panel.Controls.Add(AdviceLabel(card.Body));
        panel.Controls.Add(AdviceLabel("适用条件：" + (string.IsNullOrWhiteSpace(card.Conditions) ? "请结合当前可见局势判断" : card.Conditions)));
        if (card.ExpectedOutcome.Length > 0) panel.Controls.Add(AdviceLabel("预期收益：" + card.ExpectedOutcome));
        if (card.Fallback.Length > 0) panel.Controls.Add(AdviceLabel("条件不满足时：" + card.Fallback));
        var details = AdviceLabel($"判断与取舍：{card.Why}\n采样事实：{card.Evidence}\n停止执行：{card.InvalidWhen}\n分析于 {card.GeneratedAt.ToLocalTime():HH:mm:ss} · 最晚有效至 {card.ExpiresAt.ToLocalTime():HH:mm:ss}");
        details.Name = "coachCardEvidence"; details.Visible = _expandedAdvice.Contains(card.Id);
        panel.Controls.Add(details);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        var expand = new AntdUI.Button { Text = details.Visible ? "收起分析依据" : "查看分析依据", AutoSize = true };
        expand.Click += (_, _) =>
        {
            details.Visible = !details.Visible;
            if (details.Visible) _expandedAdvice.Add(card.Id); else _expandedAdvice.Remove(card.Id);
            expand.Text = details.Visible ? "收起分析依据" : "查看分析依据";
        };
        actions.Controls.Add(expand);
        string feedback = state.History.LastOrDefault(h => h.Recommendation.Id == card.Id)?.Feedback ?? "";
        foreach (string label in new[] { "有帮助", "不适用" })
        {
            var button = new AntdUI.Button { Text = feedback == label ? "✓ " + label : label, AutoSize = true };
            button.Click += (_, _) => _recommendationCoordinator.RecordFeedback(card.Id, label);
            actions.Controls.Add(button);
        }
        panel.Controls.Add(actions);
        return panel;
    }
    private static AntdUI.Label AdviceLabel(string text, bool bold = false) => new()
    {
        Text = text, AutoSizeMode = AntdUI.TAutoSize.Height, TextMultiLine = true,
        TextAlign = ContentAlignment.TopLeft, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10),
        Font = new Font(UiMetrics.FontFamily, bold ? 11 : 10, bold ? FontStyle.Bold : FontStyle.Regular)
    };
    private void SizeAdviceCards()
    {
        int width = Math.Max(180, _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (Control card in _cardsPanel.Controls)
        {
            card.Width = width;
            if (card is TableLayoutPanel table)
            {
                table.MinimumSize = new Size(width, 0);
                table.MaximumSize = new Size(width, 0);
                foreach (AntdUI.Label label in table.Controls.OfType<AntdUI.Label>()) label.MaximumSize = new Size(Math.Max(100, width - table.Padding.Horizontal - 8), 0);
            }
        }
    }
    private void ApplyAdviceTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette);
        _summary.ForeColor = palette.TextPrimary;
        _analysisStatus.ForeColor = palette.TextSecondary;
        _cardsPanel.BackColor = palette.Surface;
        foreach (Control card in _cardsPanel.Controls)
        {
            if (card == _recommendation) continue;
            card.BackColor = palette.SurfaceRaised;
            foreach (AntdUI.Label label in card.Controls.OfType<AntdUI.Label>()) label.ForeColor = palette.TextPrimary;
            if (card.Controls.Count > 0) card.Controls[0].ForeColor = palette.Accent;
        }
    }
    private void ShowAdviceHistory()
    {
        var history = _recommendationCoordinator.Current.History;
        using var dialog = new AntdContentWindow { Text = "本局建议记录（仅保留本局最近60条）", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var text = new AntdUI.Input { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            Text = history.Count == 0 ? "本局暂无建议记录。" : string.Join("\n\n", history.Reverse().Select(h =>
                $"{h.At.ToLocalTime():HH:mm:ss} · 游戏 {h.GameTime} · {h.Recommendation.Title}\n{h.Recommendation.Body}\n适用：{h.Recommendation.Conditions}\n预期：{h.Recommendation.ExpectedOutcome}\n备选：{h.Recommendation.Fallback}\n判断：{h.Recommendation.Why}\n依据：{h.Recommendation.Evidence}\n反馈：{(h.Feedback.Length == 0 ? "未评价" : h.Feedback)}")) };
        dialog.Controls.Add(text); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); dialog.ShowDialog(FindForm());
    }
}
