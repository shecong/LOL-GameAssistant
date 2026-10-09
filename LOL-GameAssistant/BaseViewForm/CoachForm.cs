using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// AI 时间线建议展示页。采集、定时和 AI 请求均由 RecommendationCoordinator 在后台管理，
/// 因此本页未打开时，局内浮窗和建议状态仍会正常更新。
/// </summary>
public sealed class CoachForm : UserControl, IThemeAware
{
    private readonly AntdUI.Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly AntdUI.Button _refresh = new() { Text = "立即更新建议", AutoSize = true };
    private readonly AntdUI.Button _applyOpgg = new() { Text = "OP.GG 一键配置当前英雄", AutoSize = true };
    private readonly AntdUI.Button _myRunes = new() { Text = "我的符文方案", AutoSize = true };
    private readonly AntdUI.Input _validation = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly AntdUI.Input _recommendation = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IRecommendationCoordinator _recommendationCoordinator;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IOpggBuildApplyService _opggBuildApplyService;
    private readonly RecommendationOverlayForm _overlay = new();
    private bool _applyingOpgg;
    private bool _opggPickerOpen;
    private string _opggPromptedContext = "";
    private string _opggFailedContext = "";
    private DateTimeOffset _opggRetryAfter;
    private string _lastOverlaySignature = "";

    /// <summary>初始化 CoachForm 的实例状态。</summary>
    public CoachForm() : this(
        AppCompositionRoot.AiCoachingService,
        AppCompositionRoot.RecommendationCoordinator,
        AppCompositionRoot.ApplicationSettingsStore,
        AppCompositionRoot.OpggBuildApplyService)
    {
    }

    /// <summary>初始化 CoachForm 的实例状态，并保存传入的依赖或数据。</summary>
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
        ApplyTheme(UiTheme.Palette);
        RefreshOpggAvailability();
        _refresh.Click += async (_, _) => await RefreshRecommendationAsync(manual: true);
        _applyOpgg.Click += async (_, _) => await ApplyOpggBuildAsync();
        _myRunes.Click += (_, _) =>
        {
            using var manager = new RunePresetManagerForm(_settingsStore, _opggBuildApplyService, _aiCoachingService);
            manager.ShowDialog(FindForm() ?? Program.GameMain);
        };
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

    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        // AntdUI.Input hides Control's color properties; set its drawing colors directly.
        foreach (AntdUI.Input input in new[] { _validation, _recommendation })
        {
            input.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
            input.BackColor = palette.SurfaceRaised;
            input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
            input.BorderHover = palette.Border;
            input.BorderActive = palette.Accent;
        }
    }

    /// <summary>推荐状态变化后刷新界面及关联展示。</summary>
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

    /// <summary>将推荐状态转换为当前界面内容。</summary>
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
        _opggPromptedContext = "";
        _opggFailedContext = "";
        _opggRetryAfter = DateTimeOffset.MinValue;
    }

    /// <summary>供选人伴随窗手动打开当前模式的方案选择；无需开启自动推荐。</summary>
    public async Task<string> OpenOpggBuildPickerAsync(CancellationToken cancellationToken = default, Form? owner = null)
    {
        if (IsDisposed) return "OP.GG 方案窗口不可用。";
        if (_applyingOpgg || _opggPickerOpen) return "OP.GG 方案正在获取或选择中。";
        _opggPickerOpen = true;
        try
        {
            AiGameContext context = await GetManualBuildContextAsync(cancellationToken);
            await ApplyOpggBuildAsync(context, automatic: false, cancellationToken, dialogOwner: owner);
            return _status.Text ?? "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return "已取消 OP.GG 方案选择。";
        }
        catch (Exception ex)
        {
            return $"获取当前选人信息失败：{ex.Message}";
        }
        finally { _opggPickerOpen = false; }
    }

    /// <summary>根据当前英雄和上下文决定是否提示推荐方案。</summary>
    private async Task PromptOpggBuildIfNeededCoreAsync(CancellationToken cancellationToken)
    {
        AssistantSettings settings = _settingsStore.Load();
        if (_applyingOpgg || _opggPickerOpen || (!settings.OpggBuildAssistantEnabled && !settings.AutoApplyRuneBuild)) return;

        AiGameContext context = await _aiCoachingService.CollectContextAsync(cancellationToken);
        if (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0)
            return;
        string modeKey = Infrastructure.LeagueClient.OpggBuildApplyService.NormalizeMode(context.GameMode, context.QueueId);
          string promptKey = BuildOpggPromptKey(context);
          if (_opggPromptedContext == promptKey) return;
          if (_opggFailedContext == promptKey && DateTimeOffset.UtcNow < _opggRetryAfter) return;

        // 在请求 OP.GG 前先标记本英雄，避免 LCU 的连续选人事件重复打开同一模态框。
        _opggPromptedContext = promptKey;
        if (settings.AutoApplyRuneBuild && modeKey != "aram_mayhem")
        {
            PersonalRunePreset? personal = PersonalRunePresetResolver.Find(
                settings.PersonalRunePresets, context.MyChampionId, modeKey, context.MyRole);
            if (personal != null)
            {
                _applyingOpgg = true;
                try
                {
                    OpggBuildApplyResult applied = await _opggBuildApplyService.ApplyPersonalRunePresetAsync(personal, cancellationToken);
                    _status.Text = applied.Message;
                    RuntimeDiagnostics.Report("个人符文方案", applied.Succeeded ? "已应用" : "失败", applied.Message);
                    if (!applied.Succeeded) ScheduleOpggRetry(promptKey);
                }
                finally
                {
                    _applyingOpgg = false;
                    RefreshOpggAvailability();
                }
                return;
            }
        }
        if (modeKey == "ranked" && PersonalRunePresetResolver.NormalizePosition(context.MyRole) == "unknown") return;
        RuntimeDiagnostics.Report("OP.GG 选人推荐", "检测到英雄", $"英雄 {context.MyChampionId} · 位置 {context.MyRole}");
          await ApplyOpggBuildAsync(context, automatic: true, cancellationToken, autoApply: settings.AutoApplyRuneBuild);
      }

      /// <summary>安排推荐读取失败后的重试。</summary>
      private void ScheduleOpggRetry(string context)
      {
          _opggPromptedContext = "";
          _opggFailedContext = context;
          _opggRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
      }

    /// <summary>按当前配置决定是否展示推荐浮窗。</summary>
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

    /// <summary>将选中的推荐方案应用到客户端。</summary>
    private async Task ApplyOpggBuildAsync()
    {
        if (!_settingsStore.Load().OpggBuildAssistantEnabled)
        {
            _status.ForeColor = Color.DarkGoldenrod;
            _status.Text = "请先在“设置 → AI 设置”中开启 OP.GG 选人推荐。";
            return;
        }

        var context = await GetManualBuildContextAsync(CancellationToken.None);
        await ApplyOpggBuildAsync(context, automatic: false, CancellationToken.None);
    }

    /// <summary>为用户手动选择方案收集英雄和模式信息。</summary>
    private async Task<AiGameContext> GetManualBuildContextAsync(CancellationToken token)
    {
        try { return await _aiCoachingService.CollectContextAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new AiGameContext(); }
    }

    /// <summary>从可用上下文中确定手动推荐所需的场景。</summary>
    internal static (AiGameContext Context, bool PreviewOnly) ResolveManualBuildContext(AiGameContext context)
    {
        string mode = Infrastructure.LeagueClient.OpggBuildApplyService.NormalizeMode(context.GameMode, context.QueueId);
        bool preview = !string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
            context.MyChampionId <= 0 || mode == "unknown";
        if (!preview) return (context, false);
        // 无对局时也能浏览；保留已知英雄，否则默认亚索、峡谷、中路。
        return (new AiGameContext
        {
            Phase = context.Phase,
            MyChampionId = context.MyChampionId > 0 ? context.MyChampionId : 157,
            MyRole = PersonalRunePresetResolver.NormalizePosition(context.MyRole) == "unknown" ? "MIDDLE" : context.MyRole,
            GameMode = mode == "unknown" ? "CLASSIC" : context.GameMode,
            QueueId = mode == "unknown" ? 0 : context.QueueId,
            EnemyChampionIds = context.EnemyChampionIds
        }, true);
    }

    /// <summary>将选中的推荐方案应用到客户端。</summary>
    private async Task ApplyOpggBuildAsync(
        AiGameContext context,
        bool automatic,
        CancellationToken cancellationToken,
        bool autoApply = false,
        Form? dialogOwner = null)
    {
        if (_applyingOpgg || IsDisposed) return;
        _applyingOpgg = true;
        _opggPickerOpen = true;
        RefreshOpggAvailability();
        _status.ForeColor = Color.DimGray;
        _status.Text = automatic ? "检测到已选英雄，正在从 OP.GG 获取图文方案…" : "正在从 OP.GG 获取图文方案…";
        try
        {
            bool previewOnly = false;
            if (!automatic)
                (context, previewOnly) = ResolveManualBuildContext(context);
            if (automatic && (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0))
            {
                _status.ForeColor = Color.DarkGoldenrod;
                _status.Text = "请在英雄选择阶段选定英雄后使用 OP.GG 配置。";
                return;
            }

            OpggBuildChoices choices = await _opggBuildApplyService
                .GetBuildChoicesAsync(
                    context.MyChampionId,
                    context.MyRole,
                    new OpggBuildRequest(context.GameMode, context.QueueId, context.EnemyChampionIds),
                    cancellationToken);
            if (!choices.Succeeded)
            {
                if (automatic) ScheduleOpggRetry(_opggPromptedContext);
                _status.ForeColor = Color.Firebrick;
                _status.Text = choices.Message;
                // 取数失败时不会弹窗，只写状态栏容易被忽略；同时写进消息区，让“没弹窗”总有原因可查。
                Program.GameMain.infoMsg.AddMsg($"OP.GG 未弹出方案：{choices.Message}");
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "取数失败", choices.Message);
                return;
            }

            string selectionKey = BuildOpggSelectionKey(context.MyChampionId, choices.Mode, context.MyRole);
            AssistantSettings settings = _settingsStore.Load();
            settings.OpggManualBuildSelections.TryGetValue(selectionKey, out int savedOrder);
            OpggBuildOption? selectedOption = automatic
                ? choices.Options.FirstOrDefault(option => option.Order == savedOrder)
                : null;
            bool manuallySelected = false;
            bool allowReplaceCurrentRunePage = false;
            string selectedPosition = context.MyRole;

            if (autoApply && selectedOption == null)
                selectedOption = choices.Options.FirstOrDefault();

            if (selectedOption == null)
            {
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "已获取方案", $"{choices.ChampionName} {choices.PositionName} · {choices.Options.Count} 套，正在等待选择");
                using var picker = new OpggBuildPickerForm(choices, AppCompositionRoot.GameAssetService, savedOrder,
                    _opggBuildApplyService, allowApply: !previewOnly);
                // 从选人伴随窗打开时，以伴随窗为父窗口，关闭后不会激活隐藏的主窗体。
                Form? owner = ResolveBuildDialogOwner(dialogOwner, FindForm() ?? Program.GameMain);
                DialogResult dialogResult = picker.ShowDialog(owner);
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "弹窗已关闭",
                    picker.SelectedOption == null ? "未选择方案" : $"已选方案 {picker.SelectedOption.Order}");
                if (dialogResult != DialogResult.OK || picker.SelectedOption == null)
                {
                    _status.ForeColor = Color.DimGray;
                    _status.Text = previewOnly ? "默认推荐浏览已关闭，进入选人阶段后可应用。" : "已取消 OP.GG 出装配置。";
                    return;
                }

                selectedOption = picker.SelectedOption;
                selectedPosition = picker.SelectedPosition;
                selectionKey = BuildOpggSelectionKey(context.MyChampionId, selectedOption.Mode, selectedPosition);
                allowReplaceCurrentRunePage = picker.AllowReplaceCurrentRunePage;
                manuallySelected = true;
            }

            AiGameContext latest = await _aiCoachingService.CollectContextAsync(cancellationToken);
            if (!string.Equals(latest.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
                latest.MyChampionId != context.MyChampionId ||
                Infrastructure.LeagueClient.OpggBuildApplyService.NormalizeMode(latest.GameMode, latest.QueueId) != choices.Mode ||
                !string.Equals(latest.MyRole, context.MyRole, StringComparison.OrdinalIgnoreCase))
            {
                _status.ForeColor = Color.DarkGoldenrod;
                _status.Text = "选人英雄、模式或位置已变化，请重新获取方案。";
                return;
            }

            _status.Text = automatic && savedOrder > 0
                ? $"正在按已保存的 {choices.PositionName} 方案 {selectedOption.Order} 配置…"
                : $"正在应用 OP.GG 方案 {selectedOption.Order}…";
            OpggBuildApplyResult result = await _opggBuildApplyService
                .ApplyBuildAsync(context.MyChampionId, selectedPosition, selectedOption, cancellationToken, allowReplaceCurrentRunePage);
            _status.ForeColor = result.Succeeded ? Color.ForestGreen : Color.Firebrick;
            _status.Text = result.Message;
            RuntimeDiagnostics.Report("OP.GG 方案应用", result.Succeeded ? "成功" : "失败", result.Message);
            Program.GameMain.infoMsg.AddMsg(result.Message);
            if (result.Succeeded)
            {
                // 手动应用同样满足本次选人的配置，后续轮询不能覆盖用户的选择。
                _opggPromptedContext = BuildOpggPromptKey(latest);
                _opggFailedContext = "";
                _opggRetryAfter = DateTimeOffset.MinValue;
            }
            if (result.Succeeded && manuallySelected)
            {
                // 弹窗期间设置可能变化；只在应用成功后保存选择。
                settings = _settingsStore.Load();
                settings.OpggManualBuildSelections[selectionKey] = selectedOption.Order;
                _settingsStore.Save(settings);
            }
            if (!result.Succeeded && !automatic)
                MessageBox.Show(ResolveBuildDialogOwner(dialogOwner, FindForm() ?? Program.GameMain), result.Message, "符文装备应用失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (automatic && !result.Succeeded) ScheduleOpggRetry(_opggPromptedContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status.ForeColor = Color.DimGray;
            _status.Text = "已离开选人阶段，取消 OP.GG 配置。";
        }
        catch (Exception ex)
        {
            if (automatic) ScheduleOpggRetry(_opggPromptedContext);
            RuntimeDiagnostics.WriteException(ex);
            _status.ForeColor = Color.Firebrick;
            _status.Text = "OP.GG 一键配置失败：" + ex.Message;
            Program.GameMain.infoMsg.AddMsg("OP.GG 一键配置失败：" + ex.Message);
        }
        finally
        {
            _applyingOpgg = false;
            _opggPickerOpen = false;
            RefreshOpggAvailability();
        }
    }

    /// <summary>确定推荐弹窗的所属窗口，保证模态显示关系正确。</summary>
    private static Form? ResolveBuildDialogOwner(Form? requested, Form? host)
    {
        Form? owner = requested ?? host;
        return owner is { IsDisposed: false, Visible: true } && owner.WindowState != FormWindowState.Minimized
            ? owner : null;
    }

    /// <summary>使用实际模式和标准分路去重，避免客户端字段别名变化触发重复配置。</summary>
    private static string BuildOpggPromptKey(AiGameContext context)
    {
        string mode = Infrastructure.LeagueClient.OpggBuildApplyService.NormalizeMode(context.GameMode, context.QueueId);
        string position = mode == "ranked" ? PersonalRunePresetResolver.NormalizePosition(context.MyRole) : "none";
        return $"{context.MyChampionId}:{mode}:{position}";
    }

    /// <summary>根据推荐选择上下文生成去重键。</summary>
    private static string BuildOpggSelectionKey(int championId, string mode, string? position)
    {
        string normalizedPosition = mode == "ranked"
            ? (position ?? "mid").Trim().ToLowerInvariant()
            : "none";
        return $"{championId}:{mode}:{normalizedPosition}";
    }

    /// <summary>创建当前界面使用的布局和操作控件。</summary>
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
        header.Controls.Add(_myRunes);
        header.Controls.Add(_status);
        _status.Padding = new Padding(8, 6, 0, 0);

        var left = new AntdUI.Panel { Dock = DockStyle.Fill, Padding = new Padding(10), Radius = 8, BorderWidth = 1 };
        left.Controls.Add(_validation);
        left.Controls.Add(new AntdUI.Label { Text = "数据状态与当前局势", Dock = DockStyle.Top, Height = 28 });
        var right = new AntdUI.Panel { Dock = DockStyle.Fill, Padding = new Padding(10), Radius = 8, BorderWidth = 1 };
        right.Controls.Add(_recommendation);
        right.Controls.Add(new AntdUI.Label { Text = "AI 时间线建议", Dock = DockStyle.Top, Height = 28 });

        var split = new AntdUI.Splitter { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        split.SizeChanged += (_, _) => FitSplitter(split);
        HandleCreated += (_, _) => BeginInvoke(() => FitSplitter(split));

        Controls.Add(split);
        Controls.Add(header);
    }

    /// <summary>将当前业务状态组织为提示文本。</summary>
    private static string BuildStatusText(RecommendationState state)
    {
        string time = state.UpdatedAt == DateTimeOffset.MinValue ? "" : $" · {state.UpdatedAt:HH:mm:ss}";
        string next = state.NextRefreshAt is { } refreshAt ? $" · 下次 {refreshAt:HH:mm:ss}" : "";
        return $"{GetStatusName(state.Status)}{time}{next}";
    }

    /// <summary>将验证结果组织为用户可读的文本。</summary>
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

    /// <summary>将推荐内容组织为展示文本。</summary>
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

        if (state.Recommendations.Count == 1 &&
            state.Recommendations[0].Id.StartsWith("cloud-text-", StringComparison.Ordinal))
            return state.Recommendations[0].Body;

        return string.Join(Environment.NewLine + Environment.NewLine, state.Recommendations.Select(item =>
            $"[{GetPriorityName(item.Priority)} · {item.Category}]{Environment.NewLine}" +
            item.Title + Environment.NewLine +
            item.Body + Environment.NewLine +
            (string.IsNullOrWhiteSpace(item.Evidence) ? "" : "依据：" + item.Evidence + Environment.NewLine) +
            "来源：AI"));
    }

    /// <summary>取得当前状态对应的显示颜色。</summary>
    private static Color GetStatusColor(RecommendationStatus status) => status switch
    {
        RecommendationStatus.AiReady => Color.ForestGreen,
        RecommendationStatus.ConfigurationRequired => Color.DarkGoldenrod,
        RecommendationStatus.Failed or RecommendationStatus.DataUnavailable => Color.Firebrick,
        _ => Color.DimGray
    };

    /// <summary>取得当前状态的显示名称。</summary>
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

    /// <summary>将推荐优先级转换为显示名称。</summary>
    private static string GetPriorityName(RecommendationPriority priority) => priority switch
    {
        RecommendationPriority.Important => "重要",
        RecommendationPriority.Attention => "注意",
        _ => "提示"
    };

    /// <summary>按可用空间调整分隔区域大小。</summary>
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
