using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 装备、符文、召唤师技能与对线知识的统一建议面板。
/// </summary>
public sealed class CoachForm : UserControl
{
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _refresh = new() { Text = "获取当前建议", AutoSize = true };
    private readonly Button _applyOpgg = new() { Text = "OP.GG 一键配置当前英雄", AutoSize = true };
    private readonly RichTextBox _validation = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.WhiteSmoke };
    private readonly RichTextBox _recommendation = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IOpggBuildApplyService _opggBuildApplyService;
    private readonly RecommendationOverlayForm _overlay = new();
    private bool _refreshing;
    private DateTime _lastRefreshAt = DateTime.MinValue;

    public CoachForm() : this(
        AppCompositionRoot.AiCoachingService,
        AppCompositionRoot.ApplicationSettingsStore,
        AppCompositionRoot.OpggBuildApplyService)
    {
    }

    /// <summary>教练面板经由应用端口读取上下文与云端建议。</summary>
    internal CoachForm(
        IAiCoachingService aiCoachingService,
        IApplicationSettingsStore settingsStore,
        IOpggBuildApplyService opggBuildApplyService)
    {
        _aiCoachingService = aiCoachingService;
        _settingsStore = settingsStore;
        _opggBuildApplyService = opggBuildApplyService;
        Dock = DockStyle.Fill;
        BuildUi();
        _refresh.Click += async (_, _) => await RefreshRecommendationAsync(manual: true);
        _applyOpgg.Click += async (_, _) => await ApplyOpggBuildAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshRecommendationAsync(manual: false);
        Disposed += (_, _) =>
        {
            _refreshTimer.Dispose();
            _overlay.Dispose();
        };
    }

    public void ConfigureAi(CloudAiSettings ai)
    {
        _refreshTimer.Interval = Math.Clamp(ai.DynamicRefreshSeconds, 15, 600) * 1000;
        _refreshTimer.Enabled = ai.Enabled && ai.DynamicRefreshEnabled;
    }

    public async Task RefreshRecommendationAsync(bool manual = false)
    {
        if (_refreshing || IsDisposed) return;
        _refreshing = true;
        _refresh.Enabled = false;
        _status.ForeColor = Color.DimGray;
        _status.Text = "正在读取本机对局状态并生成建议…";

        try
        {
            AssistantSettings config = _settingsStore.Load();
            var context = await _aiCoachingService.CollectContextAsync();
            var result = await _aiCoachingService.GetRecommendationAsync(config.Ai, context);
            if (IsDisposed) return;

            _validation.Text = result.LocalValidation;
            _recommendation.Text = result.Recommendation;
            _lastRefreshAt = DateTime.Now;
            _status.ForeColor = result.FromAi ? Color.ForestGreen : Color.DarkGoldenrod;
            _status.Text = result.FromAi
                ? $"已由 {config.Ai.Provider} 更新 · {_lastRefreshAt:HH:mm:ss}"
                : $"本地建议 · {_lastRefreshAt:HH:mm:ss}";

            if (string.Equals(result.Context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                (config.Ai.RecommendationOverlayEnabled || config.Ai.ShowRecommendationPopup))
                _overlay.ShowRecommendation(result.Recommendation, config.Ai);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = $"建议更新失败：{ex.Message}";
            }
        }
        finally
        {
            if (!IsDisposed) _refresh.Enabled = true;
            _refreshing = false;
        }
    }

    private async Task ApplyOpggBuildAsync()
    {
        if (_refreshing || IsDisposed) return;
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

        var left = new GroupBox { Text = "本局校验与对线知识", Dock = DockStyle.Fill, Padding = new Padding(10) };
        left.Controls.Add(_validation);
        var right = new GroupBox { Text = "装备、符文与召唤师技能建议", Dock = DockStyle.Fill, Padding = new Padding(10) };
        right.Controls.Add(_recommendation);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        // UserControl 在静态字段初始化阶段尚未加入父窗体，初始宽度可能是 0。
        // 不能在对象初始化时写固定 SplitterDistance，否则会违反最小面板宽度约束并导致类型初始化失败。
        split.SizeChanged += (_, _) => FitSplitter(split);
        HandleCreated += (_, _) => BeginInvoke(() => FitSplitter(split));

        Controls.Add(split);
        Controls.Add(header);
    }

    private static void FitSplitter(SplitContainer split)
    {
        int usableWidth = split.ClientSize.Width - split.SplitterWidth;
        const int leftMinimum = 260;
        const int rightMinimum = 320;
        int minimumWidth = leftMinimum + rightMinimum;
        if (usableWidth < minimumWidth) return;

        int target = usableWidth / 2;
        // 先在 WinForms 默认最小宽度约束下设置位置，再启用业务最小宽度。
        // 否则构造阶段的 0 宽度会使 ApplyPanel2MinSize 抛出异常。
        split.SplitterDistance = Math.Clamp(target, 25, usableWidth - 25);
        split.Panel1MinSize = leftMinimum;
        split.Panel2MinSize = rightMinimum;
        split.SplitterDistance = Math.Clamp(target, leftMinimum, usableWidth - rightMinimum);
    }
}
