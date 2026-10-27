using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class RecentMatchRow
{
    private bool _referenceLayout;
    private bool _workspaceLayout;
    private AntdUI.Label? _detailArrow;
    internal void UseReferenceLayout()
    {
        _referenceLayout = true;
        Height = 48;
        Margin = new Padding(0, 0, 0, 3);
        _detailArrow = new AntdUI.Label { Text = "›", AutoSize = false, Cursor = Cursors.Hand,
            BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 14F) };
        _detailArrow.Click += (_, _) => OpenDetail();
        Controls.Add(_detailArrow);
        LayoutRow();
    }
    internal void UseWorkspaceLayout()
    {
        UseReferenceLayout();
        _workspaceLayout = true;
        Height = 72;
        LayoutRow();
    }
    private void LayoutWorkspaceRow()
    {
        int w = ClientSize.Width, y = (ContentHeight - 30) / 2;
        bool date = w >= 600;
        picChampion.SetBounds(10, y, 30, 30);
        lblChampion.SetBounds(48, y, Math.Max(25, (int)(w * .27) - 52), 30);
        lblResult.SetBounds((int)(w * .27), y, (int)(w * .10), 30);
        lblKda.SetBounds((int)(w * .38), y, (int)(w * .18), 30);
        lblMode.Visible = true;
        lblMode.SetBounds((int)(w * .57), y, (int)(w * (date ? .17 : .22)), 30);
        lblDuration.SetBounds((int)(w * (date ? .75 : .81)), y, (int)(w * .10), 30);
        lblDate.Visible = date;
        lblDate.SetBounds((int)(w * .85), y, Math.Max(1, (int)(w * .15) - 20), 30);
        _detailArrow?.SetBounds(w - 18, y, 16, 30);
    }
    private void LayoutReferenceRow()
    {
        int right = ClientSize.Width - 8;
        lblResult.SetBounds(8, 10, 34, 24);
        picChampion.SetBounds(46, 7, 30, 30);
        lblChampion.SetBounds(82, 2, Math.Max(20, right - 212), 22);
        lblMode.Visible = true;
        lblMode.SetBounds(82, 24, lblChampion.Width, 20);
        lblMode.Font = new Font(UiMetrics.FontFamily, 8F);
        lblMode.ForeColor = UiTheme.Palette.TextSecondary;
        lblKda.SetBounds(right - 124, 2, 78, 24);
        lblKda.Font = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold);
        lblDuration.SetBounds(right - 44, 2, 34, 24);
        lblDuration.Font = new Font(UiMetrics.FontFamily, 7.5F);
        lblDate.Visible = false;
        _detailArrow?.SetBounds(right - 10, 4, 16, 24);
    }
}
