using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Insights;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 设置页中的本机客户端工具。交互控件统一采用 AntdUI，图片控件仅用于展示 LCU 返回的资源。
/// </summary>
public sealed class ClientToolsForm : UserControl, IThemeAware
{
    private readonly IClientFeatureService _features;
    private readonly IChampionInsightsService _championInsights;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IProfileIconService _profileIcons;
    private readonly List<AntdUI.Panel> _cards = [];
    private readonly ToolTip _featureTip = new() { AutoPopDelay = 16000, InitialDelay = 320, ReshowDelay = 120, ShowAlways = true };
    private readonly AntdUI.Switch _autoAccept = new();
    private readonly AntdUI.InputNumber _acceptMin = Number(0, 15000);
    private readonly AntdUI.InputNumber _acceptMax = Number(0, 15000);
    private readonly AntdUI.Switch _preselectOnly = new();
    private readonly AntdUI.Switch _skipFill = new();
    private readonly AntdUI.Switch _autoHonor = new();
    private readonly AntdUI.Switch _autoReturn = new();
    private readonly AntdUI.Switch _returnAndSearch = new();
    private readonly AntdUI.InputNumber _quickQueue = Number(1, 3000, 430);
    private readonly AntdUI.Input _replayGameId = Input("对局 ID", 150);
    private readonly AntdUI.Input _benchChampionId = Input("英雄 ID", 120);
    private readonly AntdUI.Input _backupName = Input("备份名称", 145, "默认配置");
    private readonly AntdUI.Select _backupList = Select(210);
    private readonly AntdUI.Select _availability = Select(128);
    private readonly AntdUI.Input _statusMessage = Input("状态文案（最多 128 字）", 250);
    private readonly AntdUI.Input _backgroundSkinId = Input("背景皮肤 ID", 132);
    private readonly AntdUI.Input _profileIconId = Input("头像 ID", 132);
    private readonly AntdUI.Select _insightMode = Select(140);
    private readonly AntdUI.Panel _friendRows = new() { Dock = DockStyle.Fill, Padding = new Padding(6), Radius = 8, BorderWidth = 1 };
    private readonly AntdUI.Panel _insightRows = new() { Dock = DockStyle.Fill, Padding = new Padding(6), Radius = 8, BorderWidth = 1 };
    private readonly AntdUI.Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 32,
        Padding = new Padding(12, 7, 12, 0),
        Text = "所有操作仅作用于当前电脑已登录的英雄联盟客户端。"
    };
    private readonly AntdUI.Label _backgroundPreviewCaption = Caption("输入皮肤 ID 后点击预览。", 260);
    private readonly AntdUI.Label _profileIconPreviewCaption = Caption("输入头像 ID 后点击预览。", 180);
    private readonly PictureBox _backgroundPreview = new()
    {
        Size = new Size(208, 96),
        BackColor = Color.FromArgb(34, 43, 56),
        SizeMode = PictureBoxSizeMode.Zoom
    };
    private readonly PictureBox _profileIconPreview = new()
    {
        Size = new Size(82, 82),
        BackColor = Color.FromArgb(34, 43, 56),
        SizeMode = PictureBoxSizeMode.Zoom
    };

    public ClientToolsForm(
        IClientFeatureService features,
        IChampionInsightsService championInsights,
        IApplicationSettingsStore settingsStore,
        IProfileIconService profileIcons)
    {
        _features = features;
        _championInsights = championInsights;
        _settingsStore = settingsStore;
        _profileIcons = profileIcons;
        Dock = DockStyle.Fill;

        AddSelectItems(_availability, "在线", "离开", "请勿打扰", "离线", "手机在线");
        _availability.SelectedIndex = 0;
        AddSelectItems(_insightMode, "峡谷 / 排位", "极地大乱斗", "斗魂竞技场", "无限火力", "极限闪击");
        _insightMode.SelectedIndex = 0;

        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 8, 0) };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCard(content, CreateAutomationCard());
        AddCard(content, CreateMatchToolsCard());
        AddCard(content, CreateReplayRewardsCard());
        AddCard(content, CreateProfileCard());
        AddCard(content, CreateFriendsCard());
        AddCard(content, CreateInsightsCard());
        viewport.Controls.Add(content);
        Controls.Add(viewport);
        Controls.Add(_status);
        AttachFeatureTips();

        Load += async (_, _) =>
        {
            LoadAutomationSettings();
            await RefreshBackupsAsync();
        };
        Disposed += (_, _) =>
        {
            _backgroundPreview.Image?.Dispose();
            _profileIconPreview.Image?.Dispose();
            _featureTip.Dispose();
        };
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _status.BackColor = palette.Surface;
        _status.ForeColor = palette.TextSecondary;
        foreach (AntdUI.Panel card in _cards)
        {
            card.BackColor = palette.SurfaceRaised;
            card.ForeColor = palette.TextPrimary;
        }
        _friendRows.BackColor = palette.SurfaceMuted;
        _friendRows.ForeColor = palette.TextPrimary;
        _insightRows.BackColor = palette.SurfaceMuted;
        _insightRows.ForeColor = palette.TextPrimary;
    }

    private Control CreateAutomationCard()
    {
        AntdUI.Panel card = Card("自动化（手动操作优先）", 134, out FlowLayoutPanel body);
        body.Controls.Add(Toggle("自动接受", _autoAccept));
        body.Controls.Add(FieldLabel("接受延迟（毫秒）"));
        body.Controls.Add(_acceptMin);
        body.Controls.Add(FieldLabel("至"));
        body.Controls.Add(_acceptMax);
        body.Controls.Add(Toggle("仅预选，不锁定", _preselectOnly));
        body.Controls.Add(Toggle("补位时跳过自动选人", _skipFill));
        body.Controls.Add(Toggle("赛后自动点赞", _autoHonor));
        body.Controls.Add(Toggle("赛后自动返回大厅", _autoReturn));
        body.Controls.Add(Toggle("返回后继续匹配", _returnAndSearch));
        body.Controls.Add(Button("保存自动化设置", (_, _) => SaveAutomationSettings()));
        return card;
    }

    private Control CreateMatchToolsCard()
    {
        AntdUI.Panel card = Card("大厅与选人", 100, out FlowLayoutPanel body);
        body.Controls.Add(FieldLabel("队列 ID"));
        body.Controls.Add(_quickQueue);
        body.Controls.Add(Button("快速创建大厅", async (_, _) =>
        {
            SaveQuickLobbyQueue();
            await RunAsync(() => _features.CreateQuickLobbyAsync((int)_quickQueue.Value));
        }));
        body.Controls.Add(ActionButton("取消当前匹配", () => _features.DeclineReadyCheckAsync()));
        body.Controls.Add(ActionButton("退出英雄选择", () => _features.DodgeChampionSelectAsync()));
        body.Controls.Add(_benchChampionId);
        body.Controls.Add(Button("交换极地大乱斗备战英雄", async (_, _) =>
        {
            if (!TryParsePositive(_benchChampionId, "英雄 ID", out long championId) || championId > int.MaxValue) return;
            await RunAsync(() => _features.SwapAramBenchAsync((int)championId));
        }));
        return card;
    }

    private Control CreateReplayRewardsCard()
    {
        AntdUI.Panel card = Card("回放、奖励与游戏设置备份", 140, out FlowLayoutPanel body);
        body.Controls.Add(_replayGameId);
        body.Controls.Add(Button("下载回放", async (_, _) =>
        {
            if (!TryParsePositive(_replayGameId, "对局 ID", out long gameId)) return;
            await RunAsync(() => _features.DownloadReplayAsync(gameId));
        }));
        body.Controls.Add(Button("观看回放", async (_, _) =>
        {
            if (!TryParsePositive(_replayGameId, "对局 ID", out long gameId)) return;
            await RunAsync(() => _features.WatchReplayAsync(gameId));
        }));
        body.Controls.Add(ActionButton("一键领取待选奖励", () => _features.ClaimAllPendingRewardsAsync()));
        body.Controls.Add(_backupName);
        body.Controls.Add(Button("保存当前游戏设置", async (_, _) =>
        {
            string name = _backupName.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("请输入备份名称。", false); return; }
            await RunAsync(() => _features.SaveGameSettingsBackupAsync(name));
            await RefreshBackupsAsync();
        }));
        body.Controls.Add(_backupList);
        body.Controls.Add(Button("恢复选中备份", async (_, _) =>
        {
            if (!TryGetSelectedBackup(out string? name)) return;
            await RunAsync(() => _features.RestoreGameSettingsBackupAsync(name!));
        }));
        body.Controls.Add(Button("删除选中备份", async (_, _) =>
        {
            if (!TryGetSelectedBackup(out string? name)) return;
            await RunAsync(() => _features.DeleteGameSettingsBackupAsync(name!));
            await RefreshBackupsAsync();
        }));
        return card;
    }

    private Control CreateProfileCard()
    {
        AntdUI.Panel card = Card("客户端状态与生涯资料", 510, out FlowLayoutPanel body);
        body.Controls.Add(FieldLabel("客户端状态"));
        body.Controls.Add(_availability);
        body.Controls.Add(_statusMessage);
        body.Controls.Add(ActionButton("更新在线状态", () =>
            _features.UpdateChatPresenceAsync(GetAvailabilityValue(), _statusMessage.Text)));
        body.Controls.Add(ActionButton("清除挑战角标", () => _features.ClearChallengeBadgesAsync()));

        body.Controls.Add(PreviewBox("生涯背景", _backgroundPreview, _backgroundPreviewCaption,
            _backgroundSkinId,
            Button("预览", async (_, _) => await PreviewBackgroundAsync()),
            Button("浏览选择", async (_, _) => await ChooseBackgroundAsync()),
            Button("应用背景", async (_, _) =>
            {
                if (!TryParsePositive(_backgroundSkinId, "背景皮肤 ID", out long skinId)) return;
                await RunAsync(() => _features.UpdateProfileBackgroundAsync(skinId));
            })));
        body.Controls.Add(PreviewBox("召唤师头像", _profileIconPreview, _profileIconPreviewCaption,
            _profileIconId,
            Button("预览", async (_, _) => await PreviewProfileIconAsync()),
            Button("浏览选择", async (_, _) => await ChooseProfileIconAsync()),
            Button("应用头像", async (_, _) =>
            {
                if (!TryParsePositive(_profileIconId, "头像 ID", out long iconId) || iconId > int.MaxValue) return;
                await RunAsync(() => _features.SetProfileIconAsync((int)iconId));
            })));
        return card;
    }

    private Control CreateFriendsCard()
    {
        var card = new AntdUI.Panel { Height = 270, Dock = DockStyle.Fill, Padding = new Padding(12), Radius = 10, BorderWidth = 1 };
        _cards.Add(card);
        var title = Title("好友游戏时长、队列状态与组色标记");
        var refresh = Button("刷新好友活动", async (_, _) => await RefreshFriendsAsync());
        refresh.Dock = DockStyle.Top;
        refresh.Height = 34;
        card.Controls.Add(_friendRows);
        card.Controls.Add(refresh);
        card.Controls.Add(title);
        return card;
    }

    private Control CreateInsightsCard()
    {
        var card = new AntdUI.Panel { Height = 290, Dock = DockStyle.Fill, Padding = new Padding(12), Radius = 10, BorderWidth = 1 };
        _cards.Add(card);
        var title = Title("外置 OP.GG 英雄 T 级与极地大乱斗平衡修正");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 4, 0, 2), WrapContents = false };
        actions.Controls.Add(_insightMode);
        actions.Controls.Add(Button("读取英雄 T 级", async (_, _) => await RefreshTiersAsync()));
        actions.Controls.Add(Button("读取极地大乱斗平衡修正", async (_, _) => await RefreshAramBalanceAsync()));
        card.Controls.Add(_insightRows);
        card.Controls.Add(actions);
        card.Controls.Add(title);
        return card;
    }

    private static AntdUI.Panel Card(string title, int height, out FlowLayoutPanel body)
    {
        var card = new AntdUI.Panel { Height = height, Dock = DockStyle.Fill, Padding = new Padding(12), Radius = 10, BorderWidth = 1 };
        body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            Padding = new Padding(0, 5, 0, 0),
            WrapContents = true
        };
        card.Controls.Add(body);
        card.Controls.Add(Title(title));
        return card;
    }

    private static AntdUI.Label Title(string text) => new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static AntdUI.Input Input(string placeholder, int width, string text = "") => new()
    {
        Width = width,
        Height = 32,
        Text = text,
        PlaceholderText = placeholder,
        Margin = new Padding(4)
    };

    private static AntdUI.InputNumber Number(decimal min, decimal max, decimal value = 0) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = 86,
        Height = 32,
        Margin = new Padding(4)
    };

    private static AntdUI.Select Select(int width) => new() { Width = width, Height = 32, Margin = new Padding(4) };

    private static void AddSelectItems(AntdUI.Select select, params string[] items) => select.Items.AddRange(items.Cast<object>().ToArray());

    private static AntdUI.Label FieldLabel(string value) => new()
    {
        AutoSize = true,
        Padding = new Padding(5, 8, 2, 0),
        Text = value
    };

    private static AntdUI.Label Caption(string value, int width) => new()
    {
        Width = width,
        Height = 42,
        ForeColor = Color.DimGray,
        Text = value,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Control Toggle(string label, AntdUI.Switch toggle)
    {
        var group = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(4) };
        group.Controls.Add(toggle);
        group.Controls.Add(FieldLabel(label));
        return group;
    }

    private static AntdUI.Button Button(string text, EventHandler click)
    {
        var button = new AntdUI.Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Margin = new Padding(4),
            Padding = new Padding(9, 2, 9, 2)
        };
        button.Click += click;
        return button;
    }

    private static Control PreviewBox(
        string title,
        PictureBox preview,
        AntdUI.Label caption,
        AntdUI.Input input,
        AntdUI.Button previewButton,
        AntdUI.Button chooseButton,
        AntdUI.Button applyButton)
    {
        var box = new AntdUI.Panel { Width = 530, Height = 168, Padding = new Padding(8), Radius = 8, BorderWidth = 1, Margin = new Padding(4) };
        preview.Dock = DockStyle.Left;
        var detail = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 0, 0, 0) };
        detail.Controls.Add(new AntdUI.Label { Text = title, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
        detail.Controls.Add(input);
        detail.Controls.Add(caption);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.Add(previewButton);
        buttons.Controls.Add(chooseButton);
        buttons.Controls.Add(applyButton);
        detail.Controls.Add(buttons);
        box.Controls.Add(detail);
        box.Controls.Add(preview);
        return box;
    }

    private AntdUI.Button ActionButton(string text, Func<Task<ClientFeatureResult>> action) => Button(text, async (_, _) => await RunAsync(action));

    private void AddCard(TableLayoutPanel content, Control card)
    {
        if (card is AntdUI.Panel panel && !_cards.Contains(panel)) _cards.Add(panel);
        int row = content.RowCount++;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, card.Height + 12));
        card.Margin = new Padding(0, 0, 0, 12);
        content.Controls.Add(card, 0, row);
    }

    private void LoadAutomationSettings()
    {
        AssistantSettings settings = _settingsStore.Load();
        _autoAccept.Checked = settings.AutoAccept;
        _acceptMin.Value = Math.Clamp(settings.AutoAcceptDelayMinMilliseconds, (int)_acceptMin.Minimum!.Value, (int)_acceptMin.Maximum!.Value);
        _acceptMax.Value = Math.Clamp(settings.AutoAcceptDelayMaxMilliseconds, (int)_acceptMax.Minimum!.Value, (int)_acceptMax.Maximum!.Value);
        _preselectOnly.Checked = settings.AutoPickPreselectOnly;
        _skipFill.Checked = settings.SkipAutoPickOnFill;
        _autoHonor.Checked = settings.AutoHonor;
        _autoReturn.Checked = settings.AutoReturnToLobby;
        _returnAndSearch.Checked = settings.AutoReturnStartMatchmaking;
        _quickQueue.Value = Math.Clamp(settings.QuickLobbyQueueId, (int)_quickQueue.Minimum!.Value, (int)_quickQueue.Maximum!.Value);
    }

    private void SaveAutomationSettings()
    {
        AssistantSettings settings = _settingsStore.Load();
        settings.AutoAccept = _autoAccept.Checked;
        settings.AutoAcceptDelayMinMilliseconds = (int)_acceptMin.Value;
        settings.AutoAcceptDelayMaxMilliseconds = (int)_acceptMax.Value;
        settings.AutoPickPreselectOnly = _preselectOnly.Checked;
        settings.SkipAutoPickOnFill = _skipFill.Checked;
        settings.AutoHonor = _autoHonor.Checked;
        settings.AutoReturnToLobby = _autoReturn.Checked;
        settings.AutoReturnStartMatchmaking = _returnAndSearch.Checked;
        settings.QuickLobbyQueueId = (int)_quickQueue.Value;
        settings.Normalize();
        _settingsStore.Save(settings);
        SetStatus("自动化设置已保存。", true);
    }

    private void SaveQuickLobbyQueue()
    {
        AssistantSettings settings = _settingsStore.Load();
        settings.QuickLobbyQueueId = (int)_quickQueue.Value;
        _settingsStore.Save(settings);
    }

    private async Task ChooseBackgroundAsync()
    {
        using var picker = new ProfileAppearancePickerForm(_features, _profileIcons, isBackgroundPicker: true);
        if (picker.ShowDialog(FindForm() ?? Program.GameMain) != DialogResult.OK || picker.SelectedId <= 0) return;
        _backgroundSkinId.Text = picker.SelectedId.ToString();
        await PreviewBackgroundAsync();
    }

    private async Task ChooseProfileIconAsync()
    {
        using var picker = new ProfileAppearancePickerForm(_features, _profileIcons, isBackgroundPicker: false);
        if (picker.ShowDialog(FindForm() ?? Program.GameMain) != DialogResult.OK || picker.SelectedId is <= 0 or > int.MaxValue) return;
        _profileIconId.Text = picker.SelectedId.ToString();
        await PreviewProfileIconAsync();
    }

    private async Task PreviewBackgroundAsync()
    {
        if (!TryParsePositive(_backgroundSkinId, "背景皮肤 ID", out long skinId)) return;
        _backgroundPreviewCaption.Text = "正在从客户端皮肤库存读取预览…";
        try
        {
            ClientSkinPreview? preview = await _features.GetProfileSkinPreviewAsync(skinId);
            if (preview?.ImageBytes is not { Length: > 0 })
            {
                _backgroundPreviewCaption.Text = "客户端库存中未找到该皮肤；仍可按 ID 应用。";
                return;
            }
            ReplaceImage(_backgroundPreview, DecodeImage(preview.ImageBytes));
            _backgroundPreviewCaption.Text = $"{preview.ChampionName} · {preview.Name}（ID {preview.SkinId}）";
        }
        catch (Exception ex)
        {
            _backgroundPreviewCaption.Text = $"预览失败：{ex.Message}";
        }
    }

    private void AttachFeatureTips()
    {
        Tip(_autoAccept, "匹配确认出现后按保存的随机延迟自动接受；手动拒绝会取消这一轮自动接受。\n");
        Tip(_acceptMin, "自动接受延迟的最小值，单位毫秒。设为 0 表示不额外等待。\n");
        Tip(_acceptMax, "自动接受延迟的最大值。每局将在最小值与最大值之间随机取值。\n");
        Tip(_preselectOnly, "开启后只把英雄设为预选，不会替你锁定英雄。\n");
        Tip(_skipFill, "客户端明确标记为补位时，跳过本轮自动选人。\n");
        Tip(_autoHonor, "结算页可点赞时，随机给可点赞队友发送荣誉。\n");
        Tip(_autoReturn, "结算后调用客户端“再来一局”返回大厅。\n");
        Tip(_returnAndSearch, "需同时开启“赛后自动返回大厅”；返回大厅后尝试开始匹配。\n");
        Tip(_quickQueue, "创建大厅时使用的队列 ID。默认 430 为匹配模式。\n");
        Tip(_benchChampionId, "输入极地大乱斗共享备战席中的英雄 ID 后，发送交换请求。\n");
        Tip(_replayGameId, "输入历史对局 ID 后下载或启动对应回放。\n");
        Tip(_backupName, "本机保存游戏设置备份的名称；仅使用文字、数字、空格、- 和 _。\n");
        Tip(_backupList, "选择一份已保存的游戏设置备份，再恢复或删除。\n");
        Tip(_availability, "选择客户端聊天在线状态；显示文字会自动转换为客户端需要的状态值。\n");
        Tip(_statusMessage, "写入客户端聊天签名，最多保留 128 个字符。\n");
        Tip(_backgroundSkinId, "可手动输入皮肤 ID，也可点“浏览选择”从本机皮肤库存中看图选择。\n");
        Tip(_profileIconId, "可手动输入头像 ID，也可点“浏览选择”打开头像缩略图目录。\n");
        Tip(_insightMode, "选择 OP.GG T 级的数据模式；界面显示中文，读取时自动映射为服务端模式名。\n");
        Tip(_friendRows, "按刷新时刻显示好友在线状态、游戏阶段、队列和已持续时长；颜色用于突出选人或游戏中。\n");
        Tip(_insightRows, "展示外置 OP.GG 公开统计或极地大乱斗的平衡修正；不会写入游戏客户端。\n");

        foreach (AntdUI.Button button in EnumerateControls(this).OfType<AntdUI.Button>())
        {
            string? description = button.Text switch
            {
                "保存自动化设置" => "将本卡片的自动化开关和延迟写入本机设置；功能会在下次对应游戏阶段生效。",
                "快速创建大厅" => "使用上方队列 ID 调用本机客户端创建大厅。",
                "取消当前匹配" => "请求客户端取消当前匹配确认，不会影响已经开始的对局。",
                "退出英雄选择" => "请求退出当前英雄选择；仅客户端允许退出的阶段会成功。",
                "交换极地大乱斗备战英雄" => "用上方英雄 ID 与极地大乱斗共享备战席执行交换。",
                "下载回放" => "请求客户端开始下载此对局的回放文件。",
                "观看回放" => "请求客户端启动已下载完成的回放。",
                "一键领取待选奖励" => "读取待选奖励并按客户端允许数量提交默认选择；客户端需要人工确认时会提示失败。",
                "保存当前游戏设置" => "读取当前客户端游戏设置并保存到助手目录的账户专属备份中。",
                "恢复选中备份" => "把选中备份写回客户端游戏设置；不会删除其它备份。",
                "删除选中备份" => "永久删除本机选中的设置备份，不影响客户端当前设置。",
                "更新在线状态" => "把选定状态和签名写入本机客户端聊天资料。",
                "清除挑战角标" => "清空客户端当前展示的挑战身份徽章。",
                "预览" => "按当前 ID 读取客户端资源并显示图片，不会修改生涯资料。",
                "浏览选择" => "打开带缩略图的资源目录；选择仅回填 ID，仍需点击应用。",
                "应用背景" => "把当前皮肤 ID 写为生涯背景。",
                "应用头像" => "把当前头像 ID 写为召唤师头像。",
                "刷新好友活动" => "从客户端好友接口重新读取在线、队列和游戏时长。",
                "读取英雄 T 级" => "从 OP.GG 公开接口读取所选模式的英雄 T 级，不写入客户端。",
                "读取极地大乱斗平衡修正" => "读取极地大乱斗模式的伤害、承受、治疗、护盾和韧性修正。",
                _ => null
            };
            if (description != null) Tip(button, description);
        }
    }

    private void Tip(Control control, string text) => _featureTip.SetToolTip(control, text.Trim());

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in EnumerateControls(child)) yield return nested;
        }
    }

    private async Task PreviewProfileIconAsync()
    {
        if (!TryParsePositive(_profileIconId, "头像 ID", out long iconId) || iconId > int.MaxValue) return;
        _profileIconPreviewCaption.Text = "正在读取客户端头像…";
        try
        {
            byte[]? bytes = await _profileIcons.GetProfileIconAsync((int)iconId);
            if (bytes is not { Length: > 0 })
            {
                _profileIconPreviewCaption.Text = "客户端未返回该头像资源；仍可按 ID 应用。";
                return;
            }
            ReplaceImage(_profileIconPreview, DecodeImage(bytes));
            _profileIconPreviewCaption.Text = $"头像 ID {iconId}";
        }
        catch (Exception ex)
        {
            _profileIconPreviewCaption.Text = $"预览失败：{ex.Message}";
        }
    }

    private async Task RefreshFriendsAsync()
    {
        try
        {
            IReadOnlyList<FriendActivity> entries = await _features.GetFriendActivitiesAsync();
            _friendRows.SuspendLayout();
            _friendRows.Controls.Clear();
            foreach (FriendActivity friend in entries.OrderByDescending(item => item.StartedAt))
            {
                string elapsed = friend.Elapsed is { } value && value >= TimeSpan.Zero ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : "-";
                var row = new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 5, 8, 0), Text = $"{friend.DisplayName}  ·  {ToChineseAvailability(friend.Availability)}  ·  {ToChineseGameStatus(friend.GameStatus)}  ·  {ToChineseQueue(friend.QueueId, friend.QueueName)}  ·  {elapsed}" };
                if (friend.GameStatus.Contains("inProgress", StringComparison.OrdinalIgnoreCase)) row.BackColor = Color.FromArgb(222, 241, 230);
                else if (friend.GameStatus.Contains("champ", StringComparison.OrdinalIgnoreCase)) row.BackColor = Color.FromArgb(255, 242, 204);
                _friendRows.Controls.Add(row);
            }
            if (entries.Count == 0) AddEmptyRow(_friendRows, "当前没有可展示的好友活动。");
            SetStatus($"已刷新 {entries.Count} 位好友的活动状态。", true);
        }
        catch (Exception ex) { SetStatus($"刷新好友活动失败：{ex.Message}", false); }
        finally { _friendRows.ResumeLayout(); }
    }

    private async Task RefreshBackupsAsync()
    {
        try
        {
            IReadOnlyList<ClientSettingsBackup> backups = await _features.ListGameSettingsBackupsAsync();
            _backupList.Items.Clear();
            foreach (ClientSettingsBackup backup in backups) _backupList.Items.Add(backup.Name);
            if (_backupList.Items.Count > 0) _backupList.SelectedIndex = 0;
        }
        catch (Exception ex) { SetStatus($"读取备份失败：{ex.Message}", false); }
    }

    private async Task RefreshTiersAsync()
    {
        string mode = GetInsightModeKey();
        try
        {
            IReadOnlyList<ChampionTierInsight> entries = await _championInsights.GetChampionTiersAsync(mode);
            _insightRows.SuspendLayout();
            _insightRows.Controls.Clear();
            foreach (ChampionTierInsight entry in entries)
                AddDataRow(_insightRows, $"{entry.ChampionName}  ·  {ToChineseMode(entry.Mode)} {entry.Tier}  ·  排名 {Display(entry.Rank)}  ·  胜率 {Percent(entry.WinRate)}  ·  登场 {Percent(entry.PickRate)}");
            if (entries.Count == 0) AddEmptyRow(_insightRows, "暂无可用的英雄 T 级数据。");
            SetStatus($"已读取 {entries.Count} 个{_insightMode.Text}英雄 T 级。", true);
        }
        catch (Exception ex) { SetStatus($"读取英雄 T 级失败：{ex.Message}", false); }
        finally { _insightRows.ResumeLayout(); }
    }

    private async Task RefreshAramBalanceAsync()
    {
        try
        {
            IReadOnlyList<ChampionBalanceAdjustment> entries = await _championInsights.GetAramBalanceAdjustmentsAsync();
            _insightRows.SuspendLayout();
            _insightRows.Controls.Clear();
            foreach (ChampionBalanceAdjustment entry in entries)
            {
                string adjustments = string.Join(" · ", new[] { $"造成 {entry.DamageDealt - 100:+0.##;-0.##;0}%", $"承受 {entry.DamageTaken - 100:+0.##;-0.##;0}%", $"治疗 {entry.Healing - 100:+0.##;-0.##;0}%", $"护盾 {entry.ShieldAmount - 100:+0.##;-0.##;0}%", $"韧性 {entry.Tenacity - 100:+0.##;-0.##;0}%" });
                AddDataRow(_insightRows, $"{entry.ChampionName}  ·  极地大乱斗平衡修正  ·  {adjustments}");
            }
            if (entries.Count == 0) AddEmptyRow(_insightRows, "暂无极地大乱斗平衡性数据。");
            SetStatus($"已读取 {entries.Count} 个英雄的极地大乱斗平衡修正。", true);
        }
        catch (Exception ex) { SetStatus($"读取极地大乱斗平衡修正失败：{ex.Message}", false); }
        finally { _insightRows.ResumeLayout(); }
    }

    private async Task RunAsync(Func<Task<ClientFeatureResult>> action)
    {
        try { ClientFeatureResult result = await action(); SetStatus(result.Message, result.Succeeded); }
        catch (Exception ex) { SetStatus($"操作失败：{ex.Message}", false); }
    }

    private bool TryGetSelectedBackup(out string? name)
    {
        name = _backupList.SelectedValue?.ToString() ?? _backupList.Text;
        if (!string.IsNullOrWhiteSpace(name)) return true;
        SetStatus("请先选择一个设置备份。", false);
        return false;
    }

    private bool TryParsePositive(AntdUI.Input input, string label, out long value)
    {
        if (long.TryParse(input.Text.Trim(), out value) && value > 0) return true;
        SetStatus($"{label}必须是正整数。", false);
        return false;
    }

    private void SetStatus(string text, bool success)
    {
        _status.Text = text;
        _status.ForeColor = success ? Color.FromArgb(42, 125, 74) : Color.FromArgb(190, 55, 55);
    }

    private string GetAvailabilityValue() => _availability.Text switch { "离开" => "away", "请勿打扰" => "dnd", "离线" => "offline", "手机在线" => "mobile", _ => "chat" };
    private string GetInsightModeKey() => _insightMode.Text switch { "极地大乱斗" => "aram", "斗魂竞技场" => "arena", "无限火力" => "urf", "极限闪击" => "nexus_blitz", _ => "ranked" };
    private static string ToChineseAvailability(string value) => value.ToLowerInvariant() switch { "chat" => "在线", "away" => "离开", "dnd" => "请勿打扰", "mobile" => "手机在线", "offline" => "离线", _ => "未知状态" };
    private static string ToChineseGameStatus(string value) => value.ToLowerInvariant() switch { "inprogress" => "游戏中", "championselect" => "英雄选择中", "inqueue" => "匹配中", "outofgame" => "空闲", _ => string.IsNullOrWhiteSpace(value) ? "未知" : value };
    private static string ToChineseQueue(int queueId, string value) => queueId switch { 420 => "单双排", 430 => "匹配模式", 440 => "灵活排位", 450 => "极地大乱斗", 490 => "快速模式", 900 => "无限火力", 1700 => "斗魂竞技场", _ => ToChineseMode(value) };
    private static string ToChineseMode(string? value) => (value ?? "").ToLowerInvariant() switch { "ranked" or "classic" or "summonersrift" => "峡谷 / 排位", "ranked_solo_5x5" => "单双排", "ranked_flex_sr" => "灵活排位", "normal" or "normal_draft" => "匹配模式", "practice_tool" => "训练模式", "aram" or "howlingabyss" => "极地大乱斗", "arena" or "cherry" => "斗魂竞技场", "urf" or "arurf" => "无限火力", "nexus_blitz" or "nexusblitz" => "极限闪击", "" => "-", _ => value! };
    private static string Display(int value) => value > 0 ? value.ToString() : "-";
    private static string Percent(double value) => value > 0 ? $"{value:F1}%" : "-";
    private static void AddDataRow(AntdUI.Panel target, string text) => target.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 5, 8, 0), Text = text });
    private static void AddEmptyRow(AntdUI.Panel target, string text) => AddDataRow(target, text);

    private static Image? DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static void ReplaceImage(PictureBox target, Image? image)
    {
        Image? old = target.Image;
        target.Image = image;
        old?.Dispose();
    }
}
