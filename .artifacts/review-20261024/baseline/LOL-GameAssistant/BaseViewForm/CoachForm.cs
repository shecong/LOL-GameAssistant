using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
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
public sealed partial class CoachForm : UserControl, IThemeAware
{
    private readonly AntdUI.Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly AntdUI.Button _refresh = new() { Text = "重新分析", AutoSize = true };
    private readonly AntdUI.Button _applyOpgg = new() { Text = "OP.GG 一键配置当前英雄", AutoSize = true };
    private readonly AntdUI.Button _myRunes = new() { Text = "我的符文方案", AutoSize = true };
    private readonly AntdUI.Input _validation = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly AntdUI.Input _recommendation = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IRecommendationCoordinator _recommendationCoordinator;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IOpggBuildApplyService _opggBuildApplyService;
    private readonly IGameAssetService? _gameAssetService;
    private readonly RecommendationOverlayPresenter _overlay;
    private bool _applyingOpgg;
    private bool _opggPickerOpen;
    private OpggBuildPickerForm? _activeBuildPicker;
    private TaskCompletionSource<string>? _activeBuildPickerCompletion;
    private TaskCompletionSource _opggStateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _opggPromptedContext = "";
    private bool _opggPromptedAutoApply;
    private string _opggFailedContext = "";
    private DateTimeOffset _opggRetryAfter;

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
        IOpggBuildApplyService opggBuildApplyService,
        IGameAssetService? gameAssetService = null,
        IGameOverlayBridge? overlayBridge = null)
    {
        _aiCoachingService = aiCoachingService;
        _recommendationCoordinator = recommendationCoordinator;
        _settingsStore = settingsStore;
        _opggBuildApplyService = opggBuildApplyService;
        _gameAssetService = gameAssetService;
        _overlay = overlayBridge == null ? new() : new(overlayBridge);
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
            _recommendation.Dispose();
            _activeBuildPicker?.Close();
            NotifyOpggStateChanged();
        };

        RenderState(_recommendationCoordinator.Current);
    }

    /// <summary>供主窗体切换到该页面时触发；实际调度不依赖该页面是否打开。</summary>
    public Task RefreshRecommendationAsync(bool manual = false) =>
        _recommendationCoordinator.RefreshAsync(force: manual);
    public Task ApplyCurrentBuildAsync() => ApplyOpggBuildAsync();
    public void RefreshVisibleState() => RenderState(_recommendationCoordinator.Current);

    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        ApplyAdviceTheme(palette);
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
        _refresh.Enabled = state.Status != RecommendationStatus.Disabled;
        _status.ForeColor = GetStatusColor(state.Status);
        _status.Text = BuildStatusText(state);
        _validation.Text = BuildValidationText(state);
        _recommendation.Text = BuildRecommendationText(state);
        if (!IsHandleCreated || PageActivity.IsActive(this)) RenderAdviceCards(state);
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
    /// 由主窗口在选人阶段轮询调用，跟随预选和已选英雄自动配置或预加载推荐。
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
        _opggPromptedAutoApply = false;
        _opggFailedContext = "";
        _opggRetryAfter = DateTimeOffset.MinValue;
    }

    /// <summary>供选人伴随窗手动打开当前模式的方案选择；无需开启自动推荐。</summary>
    public async Task<string> OpenOpggBuildPickerAsync(CancellationToken cancellationToken = default, Form? owner = null)
    {
        RuntimeDiagnostics.Report("OP.GG 手动打开", "收到请求",
            $"正在应用 {_applyingOpgg} · 窗口打开中 {_opggPickerOpen} · 页面已释放 {IsDisposed}");
        if (IsDisposed) return "OP.GG 方案窗口不可用。";
        try
        {
            while (_applyingOpgg || _opggPickerOpen)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_activeBuildPicker is { IsDisposed: false } picker)
                {
                    Task<string>? existingResult = _activeBuildPickerCompletion?.Task;
                    if (owner is ChampSelectCompanionForm)
                    {
                        picker.Owner = null;
                        picker.ShowInTaskbar = true;
                    }
                    if (picker.WindowState == FormWindowState.Minimized) picker.WindowState = FormWindowState.Normal;
                    picker.Activate();
                    picker.BringToFront();
                    if (owner is ChampSelectCompanionForm)
                    {
                        // 伴随窗入口保持占用直到现有方案窗关闭。
                        await WaitForBuildPickerClosedAsync(picker, cancellationToken);
                        return existingResult == null ? "已取消 OP.GG 出装配置。"
                            : await existingResult.WaitAsync(cancellationToken);
                    }
                    return "OP.GG 方案窗口已打开，请选择方案。";
                }
                // 等待自动获取完成或弹窗显示，保留本次手动打开请求。
                await _opggStateChanged.Task.WaitAsync(cancellationToken);
                if (IsDisposed) return "OP.GG 方案窗口不可用。";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return "已取消 OP.GG 方案选择。";
        }

        _opggPickerOpen = true;
        try
        {
            _buildStatus.Text = "正在读取当前英雄与对局模式…";
            RuntimeDiagnostics.Report("OP.GG 手动打开", "读取对局", "正在获取当前英雄与对局模式");
            AiGameContext context = await GetManualBuildContextAsync(cancellationToken);
            RuntimeDiagnostics.Report("OP.GG 手动打开", "已读取对局",
                $"阶段 {context.Phase} · 英雄 {context.MyChampionId} · 模式 {context.GameMode}");
            return await ApplyOpggBuildAsync(context, automatic: false, cancellationToken, dialogOwner: owner);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return "已取消 OP.GG 方案选择。";
        }
        catch (Exception ex)
        {
            return $"获取当前选人信息失败：{ex.Message}";
        }
        finally
        {
            _opggPickerOpen = false;
            NotifyOpggStateChanged();
        }
    }

    private void NotifyOpggStateChanged()
    {
        var previous = _opggStateChanged;
        _opggStateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    /// <summary>根据当前英雄和上下文决定是否提示推荐方案。</summary>
    private async Task PromptOpggBuildIfNeededCoreAsync(CancellationToken cancellationToken)
    {
        AssistantSettings settings = _settingsStore.Load();
        if (AppCompositionRoot.Automation.IsPaused || _applyingOpgg || _opggPickerOpen || (!settings.OpggBuildAssistantEnabled && !settings.AutoApplyRuneBuild)) return;

        // 采集上下文期间也占用入口，避免手动点击与自动检测交错执行。
        _opggPickerOpen = true;
        try
        {
            AiGameContext context = await _aiCoachingService.CollectContextAsync(cancellationToken);
            if (AppCompositionRoot.Automation.IsPaused) return;
            if (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0)
            {
                ResetOpggChampSelectPrompt();
                return;
            }
            string modeKey = Infrastructure.LeagueClient.OpggBuildApplyService.NormalizeMode(context.GameMode, context.QueueId);
            if (modeKey == "unknown" ||
                (modeKey == "ranked" && PersonalRunePresetResolver.NormalizePosition(context.MyRole) == "unknown"))
                return;
            string promptKey = BuildOpggPromptKey(context);
            if (_opggPromptedContext == promptKey && (!settings.AutoApplyRuneBuild || _opggPromptedAutoApply)) return;
            if (_opggFailedContext == promptKey && DateTimeOffset.UtcNow < _opggRetryAfter) return;

            // 同一英雄只应用一次；切换英雄、分路或开启自动配置后重新应用。
            _opggPromptedContext = promptKey;
            _opggPromptedAutoApply = settings.AutoApplyRuneBuild;
            PersonalRunePreset? personal = null;
            if (settings.AutoApplyRuneBuild && modeKey != "aram_mayhem")
            {
                personal = PersonalRunePresetResolver.Find(
                    settings.PersonalRunePresets, context.MyChampionId, modeKey, context.MyRole);
                if (personal?.ItemBlocks is { Count: > 0 })
                {
                    _applyingOpgg = true;
                    try
                    {
                        AiGameContext latest = await _aiCoachingService.CollectContextAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!CanAutoApplyBuildContext(context, latest))
                        {
                            ScheduleOpggRetry(promptKey);
                            return;
                        }
                        OpggBuildApplyResult applied = await _opggBuildApplyService.ApplyPersonalRunePresetAsync(
                            personal, cancellationToken, allowReplaceCurrentRunePage: true);
                        _buildStatus.Text = applied.Message;
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
            RuntimeDiagnostics.Report("OP.GG 选人推荐", "检测到英雄", $"英雄 {context.MyChampionId} · 位置 {context.MyRole}");
            if (!settings.AutoApplyRuneBuild)
            {
                // Prepare cached data without opening or activating a window during champion select.
                OpggBuildChoices choices;
                try
                {
                    choices = await _opggBuildApplyService.GetBuildChoicesAsync(context.MyChampionId, context.MyRole,
                        new OpggBuildRequest(context.GameMode, context.QueueId, context.EnemyChampionIds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.WriteException(ex);
                    choices = OpggBuildChoices.Failure("推荐后台加载失败，可点击一键配置重试。");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                if (!choices.Succeeded) ScheduleOpggRetry(promptKey);
                _buildStatus.Text = choices.Succeeded ? "推荐方案已就绪，点击一键配置查看并应用。" : choices.Message;
                RuntimeDiagnostics.Report("OP.GG 选人推荐", choices.Succeeded ? "后台已加载" : "后台加载失败", _buildStatus.Text);
                return;
            }
            await ApplyOpggBuildAsync(context, automatic: true, cancellationToken,
                autoApply: true, personalPreset: personal);
        }
        finally
        {
            _opggPickerOpen = false;
            NotifyOpggStateChanged();
        }
    }

    /// <summary>安排推荐读取失败后的重试。</summary>
    private void ScheduleOpggRetry(string context)
    {
        _opggPromptedContext = "";
        _opggFailedContext = context;
        _opggRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
    }

    private bool CanAutoApplyBuildContext(AiGameContext original, AiGameContext latest) =>
        !AppCompositionRoot.Automation.IsPaused && _settingsStore.Load().AutoApplyRuneBuild &&
        string.Equals(latest.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) &&
        BuildOpggPromptKey(original) == BuildOpggPromptKey(latest);

    /// <summary>按当前配置决定是否展示推荐浮窗。</summary>
    private void ShowOverlayIfNeeded(RecommendationState state)
    {
        if (_overlay.OverlayVisible && !state.Recommendations.Any(card => card.Id == _overlayRecommendationId && card.ExpiresAt > DateTimeOffset.UtcNow))
            _overlay.Hide();
        if (_overlaySessionVersion != state.SessionVersion)
        {
            _overlaySessionVersion = state.SessionVersion; _shownAdvice.Clear();
            if (_overlay.OverlayVisible) _overlay.Hide();
        }
        if (!string.Equals(state.Context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
        {
            _shownAdvice.Clear();
            if (_overlay.OverlayVisible) _overlay.Hide();
            return;
        }

        if (state.Status is RecommendationStatus.Disabled or RecommendationStatus.DataUnavailable or
            RecommendationStatus.ConfigurationRequired || state.Recommendations.Count == 0)
        {
            if (_overlay.OverlayVisible) _overlay.Hide();
            return;
        }

        if (state.Status == RecommendationStatus.NoActiveGame ||
            state.Status == RecommendationStatus.Collecting && state.Recommendations[0].Source != RecommendationSource.LocalObservation)
            return;

        CloudAiSettings ai = _settingsStore.Load().Ai;
        if (!ai.RecommendationOverlayEnabled && !ai.ShowRecommendationPopup)
        {
            if (_overlay.OverlayVisible) _overlay.Hide();
            return;
        }
        CoachRecommendation recommendation = state.Recommendations[0];
        if (recommendation.Priority != RecommendationPriority.Important || recommendation.IsFormatError ||
            string.IsNullOrWhiteSpace(recommendation.ActionKey) || recommendation.ExpiresAt <= DateTimeOffset.UtcNow) return;
        string signature = $"{state.Context.MyChampionId}|{state.Context.GameMode}|{recommendation.SemanticKey}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_shownAdvice.TryGetValue(signature, out var lastShown) && now - lastShown < TimeSpan.FromSeconds(90)) return;
        _shownAdvice[signature] = now;

        _overlayRecommendationId = recommendation.Id;
        _overlay.ShowRecommendation(recommendation, ai);
    }

    /// <summary>应用前重新核对对局；无对局时允许手动预配置。</summary>
    internal static bool CanApplyBuildContext(AiGameContext original, AiGameContext latest,
        string selectedMode, bool allowPreconfiguration)
    {
        if (string.Equals(latest.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            // 分路由客户端推断，选人期间可能变化；用户可以主动选择任意分路的方案。
            return latest.MyChampionId == original.MyChampionId;

        return allowPreconfiguration && !latest.HasLiveClientData && (latest.Phase ?? "").ToLowerInvariant() is
            "" or "未知" or "none" or "lobby" or "matchmaking" or "readycheck";
    }

    /// <summary>将选中的推荐方案应用到客户端。</summary>
    private async Task ApplyOpggBuildAsync()
    {
        if (!_settingsStore.Load().OpggBuildAssistantEnabled)
        {
            _buildStatus.ForeColor = Color.DarkGoldenrod;
            _buildStatus.Text = "请先在“设置 → AI 设置”中开启 OP.GG 选人推荐。";
            return;
        }

        await OpenOpggBuildPickerAsync(owner: FindForm() ?? Program.GameMain);
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
    private async Task<string> ApplyOpggBuildAsync(
        AiGameContext context,
        bool automatic,
        CancellationToken cancellationToken,
        bool autoApply = false,
        Form? dialogOwner = null,
        PersonalRunePreset? personalPreset = null)
    {
        if (_applyingOpgg || IsDisposed) return "OP.GG 方案窗口不可用。";
        _applyingOpgg = true;
        _opggPickerOpen = true;
        RefreshOpggAvailability();
        _buildStatus.ForeColor = Color.DimGray;
        _buildStatus.Text = automatic ? "检测到已选英雄，正在从 OP.GG 获取图文方案…" : "正在从 OP.GG 获取图文方案…";
        try
        {
            bool previewOnly = false;
            if (!automatic)
                (context, previewOnly) = ResolveManualBuildContext(context);
            if (automatic && (!string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || context.MyChampionId <= 0))
            {
                _buildStatus.ForeColor = Color.DarkGoldenrod;
                _buildStatus.Text = "请在英雄选择阶段选定英雄后使用 OP.GG 配置。";
                return _buildStatus.Text;
            }

            OpggBuildChoices choices = await _opggBuildApplyService
                .GetBuildChoicesAsync(
                    context.MyChampionId,
                    context.MyRole,
                    new OpggBuildRequest(context.GameMode, context.QueueId, context.EnemyChampionIds),
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed) return "OP.GG 方案窗口不可用。";
            if (!choices.Succeeded || choices.Options.Count == 0)
            {
                if (automatic) ScheduleOpggRetry(_opggPromptedContext);
                _buildStatus.ForeColor = Color.Firebrick;
                _buildStatus.Text = choices.Message;
                // 取数失败时不会弹窗，只写状态栏容易被忽略；同时写进消息区，让“没弹窗”总有原因可查。
                Program.GameMain?.infoMsg.AddMsg($"OP.GG 未获取方案：{choices.Message}");
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "取数失败", choices.Message);
                return choices.Message;
            }

            string selectionKey = BuildOpggSelectionKey(context.MyChampionId, choices.Mode, context.MyRole);
            AssistantSettings settings = _settingsStore.Load();
            settings.OpggManualBuildSelections.TryGetValue(selectionKey, out int savedOrder);
            OpggBuildOption? selectedOption = automatic && autoApply
                ? choices.Options.FirstOrDefault(option => option.Order == savedOrder)
                : null;
            if (autoApply && selectedOption == null)
                selectedOption = choices.Options.FirstOrDefault();

            if (selectedOption == null)
            {
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "已获取方案", $"{choices.ChampionName} {choices.PositionName} · {choices.Options.Count} 套，正在等待选择");
                using var pickerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                OpggBuildApplyResult? selectedResult = null;
                using var picker = new OpggBuildPickerForm(choices, _gameAssetService ?? AppCompositionRoot.GameAssetService, savedOrder,
                    _opggBuildApplyService, hasCurrentMatch: !previewOnly,
                    applySelection: async (option, position, replace) =>
                        selectedResult = await ApplySelectedBuildAsync(context, option, position,
                            previewOnly, automatic, pickerCancellation.Token, replace, rememberSelection: true));
                picker.FormClosing += (_, _) => pickerCancellation.Cancel();
                // 非激活浮窗不能作为输入窗口的父窗口；方案窗使用异步生命周期。
                Form? owner = ResolveBuildDialogOwner(dialogOwner, FindForm() ?? Program.GameMain);
                _activeBuildPicker = picker;
                var pickerCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeBuildPickerCompletion = pickerCompletion;
                picker.Shown += (_, _) =>
                {
                    NotifyOpggStateChanged();
                    if (cancellationToken.IsCancellationRequested) picker.Close();
                };
                DialogResult dialogResult;
                try
                {
                    await WaitForBuildPickerClosedAsync(picker, cancellationToken, () =>
                    {
                        picker.ShowInTaskbar = owner == null;
                        if (owner == null) picker.Show();
                        else picker.Show(owner);
                        if (!picker.IsDisposed) picker.Activate();
                    });
                    dialogResult = picker.DialogResult;
                }
                finally
                {
                    pickerCompletion.TrySetResult(picker.DialogResult == DialogResult.OK
                        ? selectedResult?.Message ?? "OP.GG 方案已应用。" : "已取消 OP.GG 出装配置。");
                    _activeBuildPicker = null;
                    _activeBuildPickerCompletion = null;
                }
                RuntimeDiagnostics.Report("OP.GG 选人推荐", "弹窗已关闭",
                    picker.SelectedOption == null ? "未选择方案" : $"已选方案 {picker.SelectedOption.Order}");
                if (dialogResult != DialogResult.OK || picker.SelectedOption == null)
                {
                    _buildStatus.ForeColor = Color.DimGray;
                    _buildStatus.Text = "已取消 OP.GG 出装配置。";
                    return _buildStatus.Text;
                }

                // 选择窗完成写入后才返回 OK，不能再次应用同一方案。
                return selectedResult?.Message ?? "OP.GG 方案已应用。";
            }

            // 仅保存符文的个人方案使用当前英雄的推荐装备，避免沿用上一个英雄的物品集。
            if (personalPreset != null)
                selectedOption = selectedOption with
                {
                    PrimaryStyleId = personalPreset.PrimaryStyleId,
                    SubStyleId = personalPreset.SubStyleId,
                    RunePerkIds = personalPreset.RunePerkIds,
                    SummonerSpellIds = personalPreset.SummonerSpellIds
                };
            OpggBuildApplyResult result = await ApplySelectedBuildAsync(context, selectedOption, context.MyRole,
                previewOnly, automatic, cancellationToken, allowReplaceCurrentRunePage: automatic && autoApply, rememberSelection: false);
            if (automatic && !result.Succeeded) ScheduleOpggRetry(_opggPromptedContext);
            return result.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _buildStatus.ForeColor = Color.DimGray;
            _buildStatus.Text = "已离开选人阶段，取消 OP.GG 配置。";
        }
        catch (Exception ex)
        {
            if (automatic) ScheduleOpggRetry(_opggPromptedContext);
            RuntimeDiagnostics.WriteException(ex);
            _buildStatus.ForeColor = Color.Firebrick;
            _buildStatus.Text = "OP.GG 一键配置失败：" + ex.Message;
            Program.GameMain?.infoMsg.AddMsg("OP.GG 一键配置失败：" + ex.Message);
        }
        finally
        {
            _applyingOpgg = false;
            _opggPickerOpen = false;
            NotifyOpggStateChanged();
            RefreshOpggAvailability();
        }
        return _buildStatus.Text ?? "";
    }

    /// <summary>核对最新对局并应用方案，将结果返回给仍在显示的选择窗。</summary>
    private async Task<OpggBuildApplyResult> ApplySelectedBuildAsync(AiGameContext context, OpggBuildOption option,
        string position, bool previewOnly, bool automatic, CancellationToken cancellationToken,
        bool allowReplaceCurrentRunePage, bool rememberSelection)
    {
        AiGameContext latest = automatic
            ? await _aiCoachingService.CollectContextAsync(cancellationToken)
            : await GetManualBuildContextAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (automatic && AppCompositionRoot.Automation.IsPaused) return OpggBuildApplyResult.Failure("本局自动化已暂停，未应用方案");
        OpggBuildApplyResult result;
        bool canApply = CanApplyBuildContext(context, latest, option.Mode, previewOnly) &&
            (!automatic || CanAutoApplyBuildContext(context, latest));
        RuntimeDiagnostics.Report("OP.GG 应用校验", canApply ? "通过" : "拒绝",
            $"打开时 {context.Phase}/{context.MyChampionId}/{context.GameMode}/{context.QueueId} · " +
            $"当前 {latest.Phase}/{latest.MyChampionId}/{latest.GameMode}/{latest.QueueId} · 方案模式 {option.Mode}");
        if (!canApply)
            result = OpggBuildApplyResult.Failure("当前阶段不可配置，或英雄已变化，请重新获取方案。");
        else
        {
            _buildStatus.Text = $"正在应用 OP.GG 方案 {option.Order}…";
            result = await _opggBuildApplyService.ApplyBuildAsync(context.MyChampionId, position, option,
                cancellationToken, allowReplaceCurrentRunePage,
                applySummonerSpells: string.Equals(latest.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase));
        }
        _buildStatus.ForeColor = result.Succeeded ? Color.ForestGreen : Color.Firebrick;
        _buildStatus.Text = result.Message;
        RuntimeDiagnostics.Report("OP.GG 方案应用", result.Succeeded ? "成功" : "失败", result.Message);
        Program.GameMain?.infoMsg.AddMsg(result.Message);
        if (result.Succeeded)
        {
            if (string.Equals(latest.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            {
                _opggPromptedContext = BuildOpggPromptKey(latest);
                _opggPromptedAutoApply = true;
                _opggFailedContext = "";
                _opggRetryAfter = DateTimeOffset.MinValue;
            }
            if (rememberSelection)
            {
                AssistantSettings settings = _settingsStore.Load();
                settings.OpggManualBuildSelections[BuildOpggSelectionKey(context.MyChampionId, option.Mode, position)] = option.Order;
                _settingsStore.Save(settings);
            }
        }
        return result;
    }

    /// <summary>仅允许稳定的普通窗口拥有方案窗；伴随浮窗入口独立显示。</summary>
    private static Form? ResolveBuildDialogOwner(Form? requested, Form? host)
    {
        if (requested is ChampSelectCompanionForm) return null;
        Form? owner = requested ?? host;
        return owner is { IsDisposed: false, Visible: true } && owner.WindowState != FormWindowState.Minimized
            ? owner : null;
    }

    private static async Task WaitForBuildPickerClosedAsync(Form picker, CancellationToken token, Action? show = null)
    {
        if (picker.IsDisposed) return;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FormClosedEventHandler handler = (_, _) => closed.TrySetResult();
        picker.FormClosed += handler;
        using var registration = token.Register(() =>
        {
            try
            {
                if (picker.IsHandleCreated && !picker.IsDisposed)
                    picker.BeginInvoke((Action)(() => { if (!picker.IsDisposed) picker.Close(); }));
                else closed.TrySetCanceled(token);
            }
            catch (InvalidOperationException) { closed.TrySetResult(); }
        });
        try
        {
            token.ThrowIfCancellationRequested();
            show?.Invoke();
            await closed.Task;
        }
        finally { picker.FormClosed -= handler; }
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

    /// <summary>将当前业务状态组织为提示文本。</summary>
    private static string BuildStatusText(RecommendationState state)
    {
        string sample = $" · 数据 {state.Context.CollectedAt.ToLocalTime():HH:mm:ss}";
        string analyzed = state.AnalyzedAt is { } at ? $" · 分析 {at.ToLocalTime():HH:mm:ss}" : " · 尚未分析";
        return $"{GetStatusName(state.Status)}{sample}{analyzed} · 请求 {state.RequestCount}/{state.RequestBudget}";
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
        lines.Add("对线对象：" + (string.IsNullOrWhiteSpace(context.MatchupChampion) ? "未确认" : context.MatchupChampion));
        if (context.ConfirmedAugmentOptions.Count > 0) lines.Add("已确认增幅候选（非已选）：" + string.Join("、", context.ConfirmedAugmentOptions));
        lines.Add("本次触发：" + state.Trigger);
        if (context.CurrentItems.Count > 0) lines.Add("已购装备：" + string.Join("、", context.CurrentItems));
        if (context.EnemyChampions.Count > 0) lines.Add("可见敌方阵容：" + string.Join("、", context.EnemyChampions));
        var frame = CoachingDecisionFrame.Create(context);
        lines.Add("可核验事实：");
        lines.AddRange(frame.Facts.Select(f => $"[{f.Id}] {f.Value}"));
        lines.Add("本轮决策范围：" + string.Join("、", frame.Opportunities.Select(o => o.Category)));
        if (state.NextRefreshAt is { } next) lines.Add("最早再次分析：" + next.ToLocalTime().ToString("HH:mm:ss"));
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
                RecommendationStatus.FormatError => "本次输出没有通过行动与事实校验，已拦截。可重新分析或检查模型是否支持结构化输出。",
                RecommendationStatus.NoActionNeeded => "本次没有值得新增的可靠行动。保持当前计划，等待局势变化；不会为了凑数生成建议。",
                RecommendationStatus.BudgetPaused => "已达到本局请求预算。可在设置中提高上限；新一局自动重置。",
                RecommendationStatus.Collecting => "正在同步或分析新变化，失效建议已撤下。",
                RecommendationStatus.Stale => "上次建议已失效。可点击“重新分析”获取当前建议。",
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
            (item.Source == RecommendationSource.LocalObservation ? "来源：本机状态提醒" : "来源：AI")));
    }

    /// <summary>取得当前状态对应的显示颜色。</summary>
    private static Color GetStatusColor(RecommendationStatus status) => status switch
    {
        RecommendationStatus.AiReady => UiTheme.Palette.IsDark ? Color.LightGreen : Color.DarkGreen,
        RecommendationStatus.BudgetPaused or RecommendationStatus.Stale => UiTheme.Palette.IsDark ? Color.Khaki : Color.DarkGoldenrod,
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
        RecommendationStatus.NoActionNeeded => "继续观察，暂无新行动",
        RecommendationStatus.ConfigurationRequired => "AI 服务未配置",
        RecommendationStatus.DataUnavailable => "对局数据不可用",
        RecommendationStatus.BudgetPaused => "本局预算已用完",
        RecommendationStatus.FormatError => "建议未通过质量校验",
        RecommendationStatus.Stale => "建议已失效",
        _ => "建议生成失败"
    };

    /// <summary>将推荐优先级转换为显示名称。</summary>
    private static string GetPriorityName(RecommendationPriority priority) => priority switch
    {
        RecommendationPriority.Important => "重要",
        RecommendationPriority.Attention => "注意",
        _ => "提示"
    };

}
