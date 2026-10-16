using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>关于页：展示项目说明、版本更新和外部数据来源。</summary>
public partial class AboutForm
{
    private readonly AntdUI.Segmented _aboutNavigation = new() { Dock = DockStyle.Top, Height = 44, Full = true };
    private readonly Panel _aboutPages = new() { Dock = DockStyle.Fill };
    private readonly List<TableLayoutPanel> _aboutCards = [];
    private readonly List<(Panel Page, TableLayoutPanel List)> _aboutLists = [];
    private bool _layingOutAbout;
    private static readonly string[] AboutTabs = ["功能说明", "常用网站"];

    /// <summary>组织关于页的说明、项目链接和数据来源卡片。</summary>
    private void BuildAboutUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(18, 10, 18, 8) };
        header.Controls.Add(new AntdUI.Label
        {
            Text = "把选人、对局和战绩信息放在一起", Dock = DockStyle.Bottom, Height = 26,
            ForeColor = Color.DimGray, AutoEllipsis = true
        });
        header.Controls.Add(new AntdUI.Label
        {
            Text = "LOL 对局小助手", Dock = DockStyle.Top, Height = 38,
            Font = new Font(UiMetrics.FontFamily, 19, FontStyle.Bold), AutoEllipsis = true
        });
        foreach (string title in AboutTabs) _aboutNavigation.Items.Add(new AntdUI.SegmentedItem { Text = title });

        Panel features = CreateAboutPage("aboutFeatures", out TableLayoutPanel featureList);
        AddAboutCard(featureList, "首页与好友", "查看自己的段位和近期战绩，搜索玩家基础信息；查看好友活动、在线状态、队列及游戏时长。");
        AddAboutCard(featureList, "对局与战绩查询", "按蓝红双方展示玩家、当前英雄、近期战绩、KDA 评估与开黑标记。支持战绩详情、筛选和回到顶部；玩家头像或名字单击复制用户标识，双击跳转战绩查询。");
        AddAboutCard(featureList, "对局自动化", "自动匹配、自动接受及随机接受延迟；按优先级禁用和选择英雄，可仅预选或补位跳过。支持定时刷新、结束提醒、赛后点赞、返回大厅与继续匹配，选项自动保存。");
        AddAboutCard(featureList, "大厅与选人操作", "在对局自动化中设置队列 ID、快速创建大厅、取消匹配确认或退出英雄选择。");
        AddAboutCard(featureList, "一键符文与装备", "按英雄、模式与分路选择 OP.GG 方案，查看符文、核心装备、可选装备和召唤师技能并应用。推荐数据与图片缓存到本地；无法获取当前对局时可预览默认方案。");
        AddAboutCard(featureList, "个人符文方案", "捕获当前符文和召唤师技能，按英雄、模式、分路管理名称、优先级及自动应用。选人时优先使用个人方案，没有匹配方案时使用 OP.GG 推荐。");
        AddAboutCard(featureList, "选人伴随窗", "在客户端旁查看队友段位和近期战绩，快速选择符文装备。窗口随客户端移动、缩放、隐藏与恢复；普通和海克斯大乱斗都支持点击备战席英雄交换。");
        AddAboutCard(featureList, "大乱斗备战席优先级", "设置英雄优先顺序，备战席出现比当前英雄优先级更高的英雄时自动交换。");
        AddAboutCard(featureList, "海克斯增幅与 OCR", "局内侧边栏展示英雄增幅推荐，按白银、黄金、棱彩切换。扫描当前增幅卡片后按英雄样本胜率排序；截图在本机内存中处理，识别模型已内置。");
        AddAboutCard(featureList, "KDA 评估与自动发送", "统计最近 30 天同模式有效对局的累计 KDA。选人通过客户端聊天汇总我方；对局发送蓝红双方，可按队伍汇总或单人一行。等待游戏就绪并位于前台后发送，开启和关闭即时保存。");
        AddAboutCard(featureList, "快捷喊话与热键", "管理默认和自定义短句，支持随机、多选批量、逐字及所有人频道发送。可配置快捷键、发送间隔和剪贴板输入方式。");
        AddAboutCard(featureList, "AI 时间线建议", "配置服务商、接口和模型，测试连接并获取可用模型；查看当前局势与建议，支持局内浮窗、定时刷新和提醒。");
        AddAboutCard(featureList, "客户端工具", "下载和观看回放、领取待选奖励、备份与恢复游戏设置；修改在线状态、签名、生涯背景、头像及挑战角标，查看好友活动、英雄 T 级和大乱斗平衡数据。");
        AddAboutCard(featureList, "客户端、外观与窗口", "设置安装目录并启动客户端，配置开机启动、托盘、分辨率、透明度、按住置顶热键与响应范围。支持浅色、深色、跟随系统主题及中英文切换。");
        AddAboutCard(featureList, "日志、诊断与本地缓存", "查看客户端连接、游戏流程与功能运行结果，定位获取或发送失败。设置自动保存在本机，推荐及资源缓存减少重复请求。");

        Panel websites = CreateAboutPage("aboutWebsites", out TableLayoutPanel siteList);
        AddAboutCard(siteList, "OP.GG", "英雄统计、符文、出装与各模式数据。", "https://www.op.gg/");
        AddAboutCard(siteList, "aramgg", "海克斯大乱斗英雄及增幅胜率数据。", "https://aramgg.com/en/augments");
        AddAboutCard(siteList, "ARAMKit", "大乱斗英雄与增幅数据查询。", "https://aramkit.com/");
        AddAboutCard(siteList, "CommunityDragon", "英雄联盟社区资源与游戏数据项目。", "https://www.communitydragon.org/");
        AddAboutCard(siteList, "项目发布与更新记录", "查看完整发布包、版本记录和更新内容。", "https://github.com/shecong/LOL-GameAssistant/releases");
        AddAboutCard(siteList, "问题反馈与功能建议", "向项目提交问题、查看现有反馈和讨论改进。", "https://github.com/shecong/LOL-GameAssistant/issues");

        _aboutPages.Controls.Add(features);
        _aboutPages.Controls.Add(websites);
        _aboutNavigation.SelectIndexChanged += (_, e) =>
        {
            features.Visible = e.Value == 0;
            websites.Visible = e.Value == 1;
            LayoutAboutCards();
        };
        _aboutNavigation.SelectIndex = 0;
        features.Visible = true;
        websites.Visible = false;

        var footer = new FlowLayoutPanel
        {
            Name = "aboutFooter", Dock = DockStyle.Bottom, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 66),
            Padding = new Padding(18, 12, 18, 8), WrapContents = true
        };
        footer.Controls.Add(lblVersion);
        footer.Controls.Add(btn_opengithub);
        footer.Controls.Add(btn_github);
        footer.Controls.Add(btn_update);
        footer.Controls.Add(UpdateBadge.Create("AboutUpdateBadge", _openUrl));
        var joinGroup = new AntdUI.Button { Name = "JoinQqGroup", Text = $"加入QQ群 · {UpdateAvailableForm.QqGroupNumber}", AutoSize = true, Height = 36 };
        joinGroup.Click += (_, _) => OpenUrl(UpdateAvailableForm.QqGroupUrl);
        footer.Controls.Add(joinGroup);
        Controls.Add(_aboutPages);
        Controls.Add(footer);
        Controls.Add(_aboutNavigation);
        Controls.Add(header);
        SizeChanged += (_, _) => LayoutAboutCards();
        DpiChangedAfterParent += (_, _) => LayoutAboutCards();
        VisibleChanged += (_, _) => { if (Visible) LayoutAboutCards(); };
        ApplyTheme(UiTheme.Palette);
        ApplyLanguage();
    }

    /// <summary>创建关于页使用的内容容器。</summary>
    private Panel CreateAboutPage(string name, out TableLayoutPanel list)
    {
        var page = new Panel { Name = name, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 6, 12, 6) };
        list = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(4)
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(list);
        _aboutLists.Add((page, list));
        list.SizeChanged += (_, _) => LayoutAboutCards();
        return page;
    }

    /// <summary>向关于页追加带说明和链接的内容卡片。</summary>
    private void AddAboutCard(TableLayoutPanel list, string title, string description, string? url = null)
    {
        var card = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 1,
            Padding = new Padding(16, 10, 16, 12), Margin = new Padding(0, 0, 0, 10)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _aboutCards.Add(card);
        var heading = new AntdUI.Label
        {
            Text = title, TextMultiLine = true, Font = new Font(UiMetrics.FontFamily, 11, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5)
        };
        var detail = new AntdUI.Label { Text = description, TextMultiLine = true, Margin = Padding.Empty };
        heading.TextChanged += (_, _) => LayoutAboutCards();
        detail.TextChanged += (_, _) => LayoutAboutCards();
        card.Controls.Add(heading, 0, 0);
        card.Controls.Add(detail, 0, 1);
        if (url != null)
        {
            var link = new AntdUI.Button { Text = "打开网站", Tag = url, AutoSize = true, Height = 34, Margin = new Padding(0, 8, 0, 0) };
            link.Click += (_, _) => OpenUrl(url);
            card.Controls.Add(link, 0, 2);
        }
        int row = list.RowCount++;
        list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        list.Controls.Add(card, 0, row);
    }

    /// <summary>按实际可用宽度排列卡片，文字变宽后允许行高回缩。</summary>
    private void LayoutAboutCards()
    {
        if (_layingOutAbout || IsDisposed) return;
        _layingOutAbout = true;
        try
        {
            int Scale(int value) => value * DeviceDpi / 96;
            _aboutNavigation.Height = Scale(48);
            foreach (var (page, list) in _aboutLists)
            {
                var cards = list.Controls.OfType<TableLayoutPanel>().ToArray();
                if (cards.Length == 0) continue;
                int columns = list.ClientSize.Width >= Scale(960) ? 2 : 1;
                list.SuspendLayout();
                try
                {
                    list.ColumnCount = columns;
                    list.ColumnStyles.Clear();
                    for (int column = 0; column < columns; column++)
                        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
                    list.RowCount = (cards.Length + columns - 1) / columns;
                    list.RowStyles.Clear();
                    for (int row = 0; row < list.RowCount; row++) list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    int columnWidth = Math.Max(1, (list.ClientSize.Width - list.Padding.Horizontal) / columns);
                    for (int index = 0; index < cards.Length; index++)
                    {
                        var card = cards[index];
                        list.SetCellPosition(card, new TableLayoutPanelCellPosition(index % columns, index / columns));
                        card.Padding = new Padding(Scale(16), Scale(12), Scale(16), Scale(14));
                        card.Margin = new Padding(0, 0, columns > 1 && index % columns == 0 ? Scale(12) : 0, Scale(12));
                        int textWidth = Math.Max(1, columnWidth - card.Margin.Horizontal - card.Padding.Horizontal);
                        foreach (AntdUI.Label label in card.Controls.OfType<AntdUI.Label>())
                        {
                            label.MaximumSize = new Size(textWidth, 0);
                            label.MinimumSize = new Size(textWidth, 0);
                            // AntdUI's AutoSize measures these labels as one line even
                            // with MaximumSize. Measure wrapping at the final width.
                            Size measured = TextRenderer.MeasureText(label.Text, label.Font,
                                new Size(textWidth, int.MaxValue),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
                            label.Size = new Size(textWidth, Math.Max(label.Font.Height, measured.Height) + Scale(6));
                        }
                    }
                }
                finally { list.ResumeLayout(true); }
            }
        }
        finally { _layingOutAbout = false; }
    }

    /// <summary>按当前语言刷新界面文本。</summary>
    public void ApplyLanguage()
    {
        for (int index = 0; index < AboutTabs.Length; index++)
            _aboutNavigation.Items[index].Text = UiLanguage.T(AboutTabs[index]);
        LayoutAboutCards();
    }

    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        _aboutPages.BackColor = palette.Surface;
        foreach (var (page, list) in _aboutLists) page.BackColor = list.BackColor = palette.Surface;
        _aboutNavigation.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        _aboutNavigation.BackColor = palette.SurfaceMuted;
        _aboutNavigation.ForeColor = palette.TextPrimary;
        // 按当前语言更新关于页中的标签文本。
        void SetLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is AntdUI.Label label)
                {
                    label.ForeColor = palette.TextPrimary;
                    label.BackColor = Color.Transparent;
                }
                SetLabels(child);
            }
        }
        SetLabels(this);
        lblVersion.ForeColor = palette.TextSecondary;
        foreach (TableLayoutPanel card in _aboutCards)
        {
            card.BackColor = palette.SurfaceRaised;
            card.ForeColor = palette.TextPrimary;
        }
    }
}
