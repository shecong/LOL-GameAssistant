using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LivePlayerForm
{
    internal int? PlayerLevel { get; private set; }
    internal int RecentWins { get; private set; }
    internal int RecentSampleSize { get; private set; }
    internal event EventHandler? OverviewChanged;
    private AntdUI.Label? _historyHeading;

    private void LayoutReferenceHeader()
    {
        if (_historyHeading == null)
        {
            _historyHeading = new AntdUI.Label { Text = "最近战绩", AutoSize = false,
                BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold) };
            headerPanel.Controls.Add(_historyHeading);
        }
        headerPanel.Height = 126;
        int right = Math.Max(220, ClientSize.Width - 12);
        picProfile.SetBounds(12, 12, 52, 52);
        lblName.Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold);
        int rankWidth = !string.IsNullOrWhiteSpace(_rankLabel.Text) ? Math.Max(60, TextRenderer.MeasureText(_rankLabel.Text, _rankLabel.Font).Width + 10) : 0;
        lblName.SetBounds(74, 10, Math.Max(60, right - 82 - rankWidth), 26);
        lblSummary.Visible = true;
        lblSummary.AutoEllipsis = true;
        lblSummary.Font = new Font(UiMetrics.FontFamily, 8F);
        lblSummary.SetBounds(right - 76, 40, 76, 22);
        _rankLabel.SetBounds(right - rankWidth, 12, rankWidth, 22);
        lblTeamTag.SetBounds(right - 40, 80, 40, 20);
        lblSub.SetBounds(74, 40, Math.Max(60, right - 158), 22);
        picCurrent.SetBounds(12, 70, 36, 36);
        lblChampionNow.Visible = !string.IsNullOrEmpty(lblChampionNow.Text);
        lblChampionNow.SetBounds(54, 78, Math.Max(110, right - 194), 20);
        lblPremadeTag.SetBounds(right - 132, 80, 86, 20);
        _historyHeading.SetBounds(12, 102, right - 12, 24);
        _historyHeading.ForeColor = UiTheme.Palette.TextPrimary;
        panelMatches.Padding = new Padding(8, 4, 8, 8);
    }
}
