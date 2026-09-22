using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 智能建议展示页。采集、定时和云端请求均由 RecommendationCoordinator 在后台管理，
/// 因此本页未打开时，局内浮窗和建议状态仍会正常更新。
/// </summary>
public sealed class CoachForm : UserControl
{
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _refresh = new() { Text = "立即更新建议", AutoSize = true };
    private readonly Button _applyOpgg = new() { Text = "OP.GG 一键配置当前英雄", AutoSize = true };
    private readonly RichTextBox _validation = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.WhiteSmoke };
    private readonly RichTextBox _recommendation = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IRecommendationCoordinator _recommendationCoordinator;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IOpggBuildApplyService _opggBuildApplyService;
    private readonly RecommendationOverlayForm _overlay = new();
    private bool _applyingOpgg;
    private string _lastOverlaySignature = "";

    public CoachForm() : this(
        AppCompositionRoot.AiCoachingService,
        AppCompositionRoot.RecommendationCoordinator,
        AppCompositionRoot.ApplicationSettingsStore,
        AppCompositionRoot.OpggBuildApplyService)
    {
    }

    internal CoachForm(
        IAiCoachingService aiCoachingService,
        IRecommendationCoordinator recommendationCoordinator,
        IApplicationSettingsStore settingsStore,
        IOpggBuildApplyService opggBuildApplyService)
    {
        _aiCoachingService = aiCoachingService;
        _recommendationCoordinator = recommendationCoordinator;
        _settingsStore = settingsStore;
        _opggBuildApplyService = opggBuildApplyService;
        Dock = DockStyle.Fill;
        BuildUi();
        _refresh.Click += async (_, _) => await RefreshRecommendationAsync(manual: true);
        _applyOpgg.Click += async (_, _) => await ApplyOpggBuildAsync();
        _recommendationCoordinator.StateChanged += OnRecommendationStateChanged;
        HandleCreated += (_, _) => RenderState(_recommendationCoordinator.Current);
        Disposed += (_, _) =>
        {
            _recommendationCoordinator.StateChanged -= OnRecommendationStateChanged;
            _overlay.Dispose();
        };

        RenderState(_recommendationCoordinator.Current);
    }

    /// <summary>供主窗体切换到该页面时触发；实际调度不依赖该页面是否打开。</summary>
    public Task RefreshRecommendationAsync(bool manual = false) =>
        _recommendationCoordinator.RefreshAsync(force: manual);

    private void OnRecommendationStateChanged(RecommendationState state)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<RecommendationState>(OnRecommendationStateChanged), state);
                return;
            }
            RenderState(state);
        }
        catch (InvalidOperationException)
        {
            // 控件正在销毁。
        }
    }

    private void RenderState(RecommendationState state)
    {
        if (IsDisposed) return;
        _refresh.Enabled = state.Status != RecommendationStatus.Collecting;
        _status.ForeColor = GetStatusColor(state.Status);
        _status.Text = BuildStatusText(state);
        _validation.Text = BuildValidationText(state);
        _recommendation.Text = BuildRecommendationText(state);
        ShowOverlayIfNeeded(state);
    }

    private void ShowOverlayIfNeeded(RecommendationState state)
    {
        if (!string.Equals(state.Context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
        {
            _lastOverlaySignature = "";
            if (_overlay.Visible) _overlay.Hide();
            return;
        }

        if (state.Status is RecommendationStatus.Disabled or RecommendationStatus.DataUnavailable)
        {
            _lastOverlaySignature = "";
            if (_overlay.Visible) _overlay.Hide();
            return;
        }

        if (
            state.Recommendations.Count == 0 ||
            state.Status is RecommendationStatus.Collecting or RecommendationStatus.NoActiveGame)
            return;

        CloudAiSettings ai = _settingsStore.Load().Ai;
        if (!ai.RecommendationOverlayEnabled && !ai.ShowRecommendationPopup) return;
        CoachRecommendation recommendation = state.Recommendations[0];
        string signature = $"{recommendation.Id}|{recommendation.Body}";
        if (signature == _lastOverlaySignature) return;

        _lastOverlaySignature = signature;
        _overlay.ShowRecommendation(recommendation, ai);
    }

    private async Task ApplyOpggBuildAsync()
    {
        if (_applyingOpgg || IsDisposed) return;
        _applyingOpgg = true;
        _applyOpgg.Enabled = false;
        _status.ForeColor = Color.DimGray;
        _status.Text = "正在从 OP.GG 获取可选出装路线…";
        try
        {
            var context = await _aiCoachingService.CollectContextAsync();
            if (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            {
                _status.ForeColor = Color.DarkGoldenrod;
                _status.Text = "请在英雄选择阶段并锁定英雄后使用一键配置。";
                return;
            }

            OpggBuildChoices choices = await _opggBuildApplyService
                .GetBuildChoicesAsync(context.MyChampionId, context.MyRole);
            if (!choices.Succeeded)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = choices.Message;
                return;
            }

            using var picker = new OpggBuildPickerForm(choices, AppCompositionRoot.GameAssetService);
            if (picker.ShowDialog(FindForm() ?? Program.GameMain) != DialogResult.OK || picker.SelectedOption == null)
            {
                _status.ForeColor = Color.DimGray;
                _status.Text = "已取消 OP.GG 出装配置。";
                return;
            }

            _status.Text = $"正在应用 OP.GG 方案 {picker.SelectedOption.Order}…";
            OpggBuildApplyResult result = await _opggBuildApplyService
                .ApplyBuildAsync(context.MyChampionId, context.MyRole, picker.SelectedOption);
            _status.ForeColor = result.Succeeded ? Color.ForestGreen : Color.Firebrick;
            _status.Text = result.Message;
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = "OP.GG 一键配置失败：" + ex.Message;
        }
        finally
        {
            if (!IsDisposed) _applyOpgg.Enabled = true;
            _applyingOpgg = false;
        }
    }

    private void BuildUi()
    {
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(12, 9, 12, 6),
            WrapContents = false
        };
        header.Controls.Add(_refresh);
        header.Controls.Add(_applyOpgg);
        header.Controls.Add(_status);
        _status.Padding = new Padding(8, 6, 0, 0);

        var left = new GroupBox { Text = "数据状态与当前局势", Dock = DockStyle.Fill, Padding = new Padding(10) };
        left.Controls.Add(_validation);
        var right = new GroupBox { Text = "当前时间线建议", Dock = DockStyle.Fill, Padding = new Padding(10) };
        right.Controls.Add(_recommendation);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        split.SizeChanged += (_, _) => FitSplitter(split);
        HandleCreated += (_, _) => BeginInvoke(() => FitSplitter(split));

        Controls.Add(split);
        Controls.Add(header);
    }

    private static string BuildStatusText(RecommendationState state)
    {
        string time = state.UpdatedAt == DateTimeOffset.MinValue ? "" : $" · {state.UpdatedAt:HH:mm:ss}";
        string next = state.NextRefreshAt is { } refreshAt ? $" · 下次 {refreshAt:HH:mm:ss}" : "";
        return $"{GetStatusName(state.Status)}{time}{next}";
    }

    private static string BuildValidationText(RecommendationState state)
    {
        AiGameContext context = state.Context;
        var lines = new List<string>
        {
            "状态：" + GetStatusName(state.Status),
            "说明：" + state.Diagnostic,
            "阶段：" + context.Phase,
            "模式：" + context.Mode,
            "英雄：" + context.MyChampion + "（" + context.MyRole + "）"
        };
        if (context.GameTimeSeconds > 0) lines.Add("游戏时间：" + context.GameTimeText);
        if (context.CurrentGold > 0) lines.Add("当前金币：" + context.CurrentGold);
        if (context.CurrentItems.Count > 0) lines.Add("已购装备：" + string.Join("、", context.CurrentItems));
        if (context.EnemyChampions.Count > 0) lines.Add("可见敌方阵容：" + string.Join("、", context.EnemyChampions));
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildRecommendationText(RecommendationState state)
    {
        if (state.Recommendations.Count == 0)
            return state.Status == RecommendationStatus.Disabled
                ? "本地规则建议已关闭。"
                : "进入英雄选择或对局后，将在这里显示基于时间线、金币和装备变化的建议。";

        return string.Join(Environment.NewLine + Environment.NewLine, state.Recommendations.Select(item =>
            $"[{GetPriorityName(item.Priority)} · {item.Category}]{Environment.NewLine}" +
            item.Title + Environment.NewLine +
            item.Body + Environment.NewLine +
            "依据：" + item.Evidence +
            (item.Source == RecommendationSource.CloudAi ? Environment.NewLine + "来源：云端 AI 补充" : "")));
    }

    private static Color GetStatusColor(RecommendationStatus status) => status switch
    {
        RecommendationStatus.EnhancedByAi => Color.ForestGreen,
        RecommendationStatus.LocalRulesReady => Color.DarkGoldenrod,
        RecommendationStatus.ConfigurationRequired => Color.DarkGoldenrod,
        RecommendationStatus.Failed or RecommendationStatus.DataUnavailable => Color.Firebrick,
        _ => Color.DimGray
    };

    private static string GetStatusName(RecommendationStatus status) => status switch
    {
        RecommendationStatus.Disabled => "智能建议已关闭",
        RecommendationStatus.Collecting => "正在读取对局信息",
        RecommendationStatus.NoActiveGame => "等待对局",
        RecommendationStatus.LocalRulesReady => "本地时间线建议",
        RecommendationStatus.EnhancedByAi => "本地建议 + 云端增强",
        RecommendationStatus.ConfigurationRequired => "本地建议（云端未配置）",
        RecommendationStatus.DataUnavailable => "对局数据不可用",
        _ => "建议生成失败"
    };

    private static string GetPriorityName(RecommendationPriority priority) => priority switch
    {
        RecommendationPriority.Important => "重要",
        RecommendationPriority.Attention => "注意",
        _ => "提示"
    };

    private static void FitSplitter(SplitContainer split)
    {
        int usableWidth = split.ClientSize.Width - split.SplitterWidth;
        const int leftMinimum = 260;
        const int rightMinimum = 320;
        if (usableWidth < leftMinimum + rightMinimum) return;

        int target = usableWidth / 2;
        split.SplitterDistance = Math.Clamp(target, 25, usableWidth - 25);
        split.Panel1MinSize = leftMinimum;
        split.Panel2MinSize = rightMinimum;
        split.SplitterDistance = Math.Clamp(target, leftMinimum, usableWidth - rightMinimum);
    }
}