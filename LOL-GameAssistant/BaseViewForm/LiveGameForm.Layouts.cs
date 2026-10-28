using Panel = AntdUI.Panel;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LiveGameForm
{
    private LiveGameWorkspace? _layoutWorkspace;
    private TableLayoutPanel? _classicLayout;
    public LiveGameLayout DisplayLayout { get; private set; } = LiveGameLayout.Classic;

    public void SetDisplayLayout(LiveGameLayout layout)
    {
        if (!Enum.IsDefined(layout)) layout = LiveGameLayout.Classic;
        if (DisplayLayout == layout && (layout == LiveGameLayout.Classic || _layoutWorkspace != null)) return;
        DisplayLayout = layout;
        if (layout != LiveGameLayout.Classic && _layoutWorkspace == null)
        {
            // A single cell host prevents TableLayoutPanel from moving controls out of occupied cells.
            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Name = "liveLayoutHost" };
            var classic = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            _classicLayout = classic;
            classic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            classic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            classic.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootGrid.Controls.Remove(column1);
            rootGrid.Controls.Remove(column2);
            classic.Controls.Add(column1, 0, 0);
            classic.Controls.Add(column2, 1, 0);
            host.Controls.Add(classic);
            rootGrid.Controls.Add(host, 0, 1);
            rootGrid.SetColumnSpan(host, 2);
            _layoutWorkspace = new LiveGameWorkspace { Dock = DockStyle.Fill, Margin = new Padding(3), Name = "liveGameWorkspace" };
            host.Controls.Add(_layoutWorkspace);
        }
        column1.Visible = column2.Visible = layout == LiveGameLayout.Classic;
        if (_classicLayout != null) _classicLayout.Visible = layout == LiveGameLayout.Classic;
        if (_layoutWorkspace != null)
        {
            _layoutWorkspace.Visible = layout != LiveGameLayout.Classic;
            if (layout != LiveGameLayout.Classic)
            {
                _layoutWorkspace.SetLayout(layout);
                SyncDisplayLayout();
                _layoutWorkspace.BringToFront();
            }
        }
        LayoutPlayerCards();
    }

    private void SyncDisplayLayout()
    {
        if (_layoutWorkspace == null || DisplayLayout == LiveGameLayout.Classic) return;
        _layoutWorkspace.SetPlayers(panelTeam1.Controls.OfType<LivePlayerForm>().ToArray(),
            panelTeam2.Controls.OfType<LivePlayerForm>().ToArray(), lblTeamTitle1.Text ?? "蓝方", lblTeamTitle2.Text ?? "红方");
    }
}
