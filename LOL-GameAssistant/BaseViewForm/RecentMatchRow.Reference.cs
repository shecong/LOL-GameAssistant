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
        Height = 52;
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
        LayoutRow();
    }
    private void LayoutWorkspaceRow()
    {
        int w = ClientSize.Width, y = (Height - 30) / 2;
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
        lblResult.SetBounds(10, 14, 38, 24);
        picChampion.SetBounds(54, 8, 34, 34);
        lblChampion.SetBounds(96, 4, Math.Max(20, right - 222), 22);
        lblMode.Visible = true;
        lblMode.SetBounds(96, 26, lblChampion.Width, 20);
        lblMode.Font = new Font(UiMetrics.FontFamily, 8F);
        lblMode.ForeColor = UiTheme.Palette.TextSecondary;
        lblKda.SetBounds(right - 122, 14, 72, 24);
        lblKda.Font = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold);
        lblDuration.SetBounds(right - 48, 14, 36, 24);
        lblDuration.Font = new Font(UiMetrics.FontFamily, 7.5F);
        lblDate.Visible = false;
        _detailArrow?.SetBounds(right - 10, 12, 16, 26);
    }
}
