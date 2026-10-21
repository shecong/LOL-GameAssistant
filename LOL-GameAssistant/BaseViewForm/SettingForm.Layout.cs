using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    private Control CreateAiDashboard(out TableLayoutPanel content)
    {
        var page = new SettingsDashboard
        {
            Name = "aiSettingsDashboard", AutoScroll = true,
            Padding = new Padding(20, 12, 20, 20)
        };
        content = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };
        content.Controls.Add(new AntdUI.Label
        {
            Text = "AI 与推荐", AutoSize = true,
            Font = new Font(UiMetrics.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        });
        var note = CreateAiNote("按功能配置选人辅助、局内面板、战绩评估与 AI 建议。开关即时保存，AI 连接单独保存。");
        note.Margin = new Padding(0, 0, 0, 20);
        content.Controls.Add(note);
        content.SizeChanged += (sender, _) =>
        {
            var container = (Control)sender!;
            note.MaximumSize = new Size(Math.Max(1,
                container.ClientSize.Width - container.Padding.Horizontal - note.Margin.Horizontal), 0);
        };
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel CreateAiSettingsCard(TableLayoutPanel content, string name, string title)
    {
        var card = new SettingsCard
        {
            Name = name, Dock = DockStyle.Top, Radius = 12,
            Padding = new Padding(16), Margin = new Padding(0, 0, 0, 16)
        };
        var rows = CreateSegmentLayout();
        rows.Padding = Padding.Empty;
        rows.Margin = Padding.Empty;
        rows.Font = new Font(UiMetrics.FontFamily, 10F);
        AddSectionHeader(rows, 0, title);
        rows.Controls[0].Padding = new Padding(0, 0, 0, 12);
        rows.Layout += (_, _) =>
        {
            int[] widths = rows.GetColumnWidths();
            if (widths.Length < 2) return;
            foreach (System.Windows.Forms.Label note in rows.Controls.OfType<System.Windows.Forms.Label>())
            {
                // Layout fires before the table recalculates its percent column. Use the
                // current client width so an early narrow measurement cannot stick.
                var maximum = new Size(Math.Max(1, rows.ClientSize.Width - rows.Padding.Horizontal -
                    widths[0] - note.Margin.Horizontal), 0);
                if (note.MaximumSize != maximum) note.MaximumSize = maximum;
            }
        };
        card.Controls.Add(rows);
        rows.SizeChanged += (_, _) => card.Height = rows.Height + card.Padding.Vertical;
        content.Controls.Add(card);
        return rows;
    }

    // WinForms labels measure wrapped text at MaximumSize; AntdUI auto-size labels measure one line.
    private static System.Windows.Forms.Label CreateAiNote(string text) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Top,
        ForeColor = Color.DimGray, UseMnemonic = false
    };

    /// <summary>各列独立向下排列，避免英雄列表把另一列的设置挤出首屏。</summary>
    private Control CreateMatchDashboard()
    {
        var page = new SettingsDashboard { AutoScroll = true };
        page.Name = "matchSettingsDashboard";
        page.Padding = new Padding(20, 12, 20, 20);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = new AntdUI.Label
        {
            Text = "对局自动化", AutoSize = true,
            Font = new Font(UiMetrics.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
        content.Controls.Add(heading);
        var note = CreateNote("按对局流程配置自动操作，修改后自动保存。");
        note.Margin = new Padding(0, 0, 0, 20);
        content.Controls.Add(note);

        var columns = new TableLayoutPanel
        {
            Name = "matchSettingsColumns", Dock = DockStyle.Top, AutoSize = true,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        TableLayoutPanel Stack() => new()
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };
        var left = Stack();
        var right = Stack();
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        content.Controls.Add(columns);
        page.Controls.Add(content);

        TableLayoutPanel Card(TableLayoutPanel stack, string name, string title)
        {
            var card = new SettingsCard
            {
                Name = name, Dock = DockStyle.Top, AutoSize = false,
                Radius = 12, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 16)
            };
            var rows = CreateSegmentLayout();
            rows.Padding = Padding.Empty;
            rows.Font = new Font(UiMetrics.FontFamily, 10F);
            AddSectionHeader(rows, 0, title);
            rows.Controls[0].Padding = new Padding(0, 0, 0, 12);
            card.Controls.Add(rows);
            rows.SizeChanged += (_, _) => card.Height = rows.Height + card.Padding.Vertical;
            stack.Controls.Add(card);
            return rows;
        }
        void Row(TableLayoutPanel rows, string text, Control control, string? tip = null)
        {
            AddSegmentRow(rows, rows.Controls.Count / 2 + 1, text, control, tip);
            control.Dock = DockStyle.None;
            control.Margin = new Padding(0, 6, 0, 6);
            if (control is AntdUI.Switch)
            {
                control.Anchor = AnchorStyles.Right;
                control.Size = new Size(64, 32);
            }
        }
        void Preview(TableLayoutPanel rows, FlowLayoutPanel preview)
        {
            preview.Dock = DockStyle.None;
            preview.BorderStyle = BorderStyle.None;
            preview.Padding = new Padding(4);
            preview.Height = 52;
            Row(rows, "英雄预览", preview);
        }

        var match = Card(left, "matchQueueCard", "匹配与对局");
        Row(match, "自动匹配", swi_open);
        Row(match, "自动接受", swi_gametrue);
        Row(match, "接受延迟（毫秒）", CreateAcceptDelayRow(), "在最小与最大值之间随机延迟；均为 0 时立即接受。");
        Row(match, "自动刷新对局", swi_auto_refresh);
        var refresh = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        refresh.Controls.Add(input_auto_refresh);
        input_auto_refresh.Dock = DockStyle.None;
        input_auto_refresh.Width = 100;
        refresh.Controls.Add(new AntdUI.Label { Text = "秒", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        Row(match, "刷新间隔", refresh);
        Row(match, "对局结束提醒", swi_notify_end);

        var after = Card(left, "matchAfterCard", "赛后操作");
        Row(after, "自动点赞", _autoHonor);
        Row(after, "点赞策略", _honorStrategy, "候选与票数以客户端为准；手动投票会阻止自动返回大厅打断操作。");
        Row(after, "自动返回大厅", _autoReturn);
        Row(after, "返回后继续匹配", _returnAndSearch, "需同时开启自动返回大厅。");

        var tools = Card(left, "matchActionsCard", "大厅与选人操作");
        Row(tools, "队列 ID", _quickQueue, "快速创建大厅使用的队列 ID，默认 430 为匹配模式。");
        Row(tools, "快捷操作", CreateMatchActions());
        Row(tools, "操作结果", _matchToolsStatus);

        var ban = Card(right, "matchBanCard", "自动禁用英雄");
        Row(ban, "启用自动禁用", swi_jyyx);
        Row(ban, "禁用优先级", setting_select_jyx);
        Preview(ban, flow_ban_preview);

        var pick = Card(right, "matchPickCard", "自动选择英雄");
        Row(pick, "启用自动选择", swi_xyx);
        Row(pick, "仅预选，不锁定", _preselectOnly, "开启后只预选英雄，不会自动锁定。");
        Row(pick, "补位时跳过", _skipFill, "客户端明确标记补位时，跳过自动选人。");
        Row(pick, "选用优先级", setting_select_xyx);
        Preview(pick, flow_pick_preview);

        var aram = Card(right, "matchAramCard", "大乱斗备战席");
        Row(aram, "自动交换英雄", _autoSwapAramBench, "备战席有更高优先级英雄时自动交换。");
        Row(aram, "备战席优先级", _aramBenchPriority);
        foreach (var rows in new[] { match, after, tools, ban, pick, aram }) rows.ResumeLayout(true);

        bool? wideLayout = null;
        void Reflow()
        {
            bool wide = page.ClientSize.Width >= (UiLanguage.IsEnglish ? 1160 : 960) * page.DeviceDpi / 96;
            if (wideLayout == wide) return;
            wideLayout = wide;
            columns.SuspendLayout();
            left.SuspendLayout();
            right.SuspendLayout();
            foreach (var rows in new[] { ban, pick, aram })
                (wide ? right : left).Controls.Add(rows.Parent!);
            if (!wide)
            {
                var ordered = new[] { match, ban, pick, aram, after, tools };
                for (int index = 0; index < ordered.Length; index++)
                    left.Controls.SetChildIndex(ordered[index].Parent!, index);
            }
            right.Visible = wide;
            columns.SetCellPosition(right, wide ? new TableLayoutPanelCellPosition(1, 0) : new TableLayoutPanelCellPosition(0, 1));
            columns.ColumnStyles[0].Width = wide ? 50 : 100;
            columns.ColumnStyles[1].Width = wide ? 50 : 0;
            left.Margin = wide ? new Padding(0, 0, 8, 0) : Padding.Empty;
            right.Margin = wide ? new Padding(8, 0, 0, 0) : Padding.Empty;
            left.ResumeLayout(true);
            right.ResumeLayout(true);
            columns.ResumeLayout(true);
        }
        page.SizeChanged += (_, _) => Reflow();
        page.Layout += (_, _) => Reflow();
        Reflow();
        return page;
    }

    private sealed class SettingsCard : AntdUI.Panel, IThemeAware
    {
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceRaised;
            BorderColor = palette.Border;
            BorderWidth = 1;
            foreach (Control child in Controls) SetBackground(child, palette);
        }

        private static void SetBackground(Control control, ThemePalette palette)
        {
            control.BackColor = palette.SurfaceRaised;
            if (control is FlowLayoutPanel { AutoScroll: true }) control.BackColor = palette.SurfaceMuted;
            foreach (Control child in control.Controls) SetBackground(child, palette);
        }
    }

    private sealed class SettingsDashboard : AntdUI.Panel, IThemeAware
    {
        public void ApplyTheme(ThemePalette palette) => SetSurface(this, palette);

        private static void SetSurface(Control control, ThemePalette palette)
        {
            if (control is SettingsCard) return;
            control.BackColor = palette.Surface;
            foreach (Control child in control.Controls) SetSurface(child, palette);
        }
    }
}
