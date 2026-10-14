using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class RecentMatchRow
{
    private bool _referenceLayout;
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
