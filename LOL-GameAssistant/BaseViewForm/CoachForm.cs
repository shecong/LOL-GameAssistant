using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// AI 时间线建议展示页。采集、定时和 AI 请求均由 RecommendationCoordinator 在后台管理，
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
    private bool _opggPickerOpen;
    private int _opggPromptedChampionId;
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
        RefreshOpggAvailability();
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
        RefreshOpggAvailability();
        _refresh.Enabled = state.Status != RecommendationStatus.Collecting;
        _status.ForeColor = GetStatusColor(state.Status);
        _status.Text = BuildStatusText(state);
        _validation.Text = BuildValidationText(state);
        _recommendation.Text = BuildRecommendationText(state);
        ShowOverlayIfNeeded(state);
    }

    /// <summary>设置保存后同步 OP.GG 入口状态；关闭开关时不会留下可点击的旧入口。</summary>
    public void RefreshOpggAvailability()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshOpggAvailability));
            return;
        }

        bool enabled = _settingsStore.Load().OpggBuildAssistantEnabled;
        _applyOpgg.Visible = enabled;
        _applyOpgg.Enabled = enabled && !_applyingOpgg;
        if (!enabled) _opggPromptedChampionId = 0;
    }

    /// <summary>
    /// 由主窗口在选人阶段轮询调用。功能关闭、未选英雄、同一英雄已弹出过时均无操作。
    /// 这确保弹窗跟随选人状态，而不依赖“智能建议”页是否正在显示。
    /// </summary>
    public Task PromptOpggBuildIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed) return Task.CompletedTask;
        if (!InvokeRequired) return PromptOpggBuildIfNeededCoreAsync(cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    await PromptOpggBuildIfNeededCoreAsync(cancellationToken);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            completion.TrySetResult();
        }
        return completion.Task;
    }

    /// <summary>一次选人阶段结束后重置“已提示”状态，下一局可再次展示方案。</summary>
    public void ResetOpggChampSelectPrompt()
    {
        _opggPromptedChampionId = 0;
    }

    private async Task PromptOpggBuildIfNeededCoreAsync(CancellationToken cancellationToken)
    {
        if (_opggPickerOpen || !_settingsStore.Load().OpggBuildAssistantEnabled) return;

        AiGameContext context = await _aiCoachingService.CollectContextAsync(cancellationToken);
        if (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0)
            return;
        if (_opggPromptedChampionId == context.MyChampionId) return;

        // 在请求 OP.GG 前先标记本英雄，避免 LCU 的连续选人事件重复打开同一模态框。
        _opggPromptedChampionId = context.MyChampionId;
        await ApplyOpggBuildAsync(context, automatic: true, cancellationToken);
    }

    private void ShowOverlayIfNeeded(RecommendationState state)
    {
        if (!string.Equals(state.Context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
        {
            _lastOverlaySignature = "";
            if (_overlay.Visible) _overlay.Hide();
            return;
        }

        if (state.Status is RecommendationStatus.Disabled or RecommendationStatus.DataUnavailable or
            RecommendationStatus.ConfigurationRequired or RecommendationStatus.Failed)
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
        if (!_settingsStore.Load().OpggBuildAssistantEnabled)
        {
            _status.ForeColor = Color.DarkGoldenrod;
            _status.Text = "请先在“设置 → AI 设置”中开启 OP.GG 选人推荐。";
            return;
        }

        var context = await _aiCoachingService.CollectContextAsync();
        await ApplyOpggBuildAsync(context, automatic: false, CancellationToken.None);
    }

    private async Task ApplyOpggBuildAsync(
        AiGameContext context,
        bool automatic,
        CancellationToken cancellationToken)
    {
        if (_applyingOpgg || IsDisposed) return;
        _applyingOpgg = true;
        _opggPickerOpen = automatic;
        RefreshOpggAvailability();
        _status.ForeColor = Color.DimGray;
        _status.Text = automatic ? "检测到已选英雄，正在从 OP.GG 获取图文方案…" : "正在从 OP.GG 获取图文方案…";
        try
        {
            if (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0)
            {
                _status.ForeColor = Color.DarkGoldenrod;
                _status.Text = "请在英雄选择阶段选定英雄后使用 OP.GG 配置。";
                return;
            }

            OpggBuildChoices choices = await _opggBuildApplyService
                .GetBuildChoicesAsync(context.MyChampionId, context.MyRole, cancellationToken);
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
                .ApplyBuildAsync(context.MyChampionId, context.MyRole, picker.SelectedOption, cancellationToken);
            _status.ForeColor = result.Succeeded ? Color.ForestGreen : Color.Firebrick;
            _status.Text = result.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status.ForeColor = Color.DimGray;
            _status.Text = "已离开选人阶段，取消 OP.GG 配置。";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = "OP.GG 一键配置失败：" + ex.Message;
        }
        finally
        {
            _applyingOpgg = false;
            _opggPickerOpen = false;
            RefreshOpggAvailability();
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
        var right = new GroupBox { Text = "AI 时间线建议", Dock = DockStyle.Fill, Padding = new Padding(10) };
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
            return state.Status switch
            {
                RecommendationStatus.Disabled => "AI 时间线建议已关闭。",
                RecommendationStatus.ConfigurationRequired => "请先在设置中填写 AI 服务商、模型名称和 API Key。",
                RecommendationStatus.DataUnavailable => "正在等待完整的本机实时对局数据。",
                RecommendationStatus.Failed => "AI 暂未生成建议，请检查网络、服务地址和模型配置。",
                _ => "进入英雄选择或对局后，将在这里显示 AI 基于当前局势生成的建议。"
            };

        return string.Join(Environment.NewLine + Environment.NewLine, state.Recommendations.Select(item =>
            $"[{GetPriorityName(item.Priority)} · {item.Category}]{Environment.NewLine}" +
            item.Title + Environment.NewLine +
            item.Body + Environment.NewLine +
            "依据：" + item.Evidence +
            Environment.NewLine + "来源：AI"));
    }

    private static Color GetStatusColor(RecommendationStatus status) => status switch
    {
        RecommendationStatus.AiReady => Color.ForestGreen,
        RecommendationStatus.ConfigurationRequired => Color.DarkGoldenrod,
        RecommendationStatus.Failed or RecommendationStatus.DataUnavailable => Color.Firebrick,
        _ => Color.DimGray
    };

    private static string GetStatusName(RecommendationStatus status) => status switch
    {
        RecommendationStatus.Disabled => "AI 时间线建议已关闭",
        RecommendationStatus.Collecting => "正在读取对局信息",
        RecommendationStatus.NoActiveGame => "等待对局",
        RecommendationStatus.AiReady => "AI 时间线建议已更新",
        RecommendationStatus.ConfigurationRequired => "AI 服务未配置",
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
