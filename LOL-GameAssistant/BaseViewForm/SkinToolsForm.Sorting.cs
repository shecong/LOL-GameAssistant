using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly AntdUI.Select _skinSort = new() { Name = "SkinSort", Dock = DockStyle.Top, Height = 44, List = true, PrefixText = "排序" };
    private readonly AntdUI.Label _ratingStatus = new() { Name = "SkinRatingStatus", Dock = DockStyle.Top, Height = 56, Text = "可按 OP.GG 统计的使用热度排序，优先展示使用场数高的皮肤。" };
    private readonly ISkinPopularityService _skinPopularity;
    private readonly AntdUI.Button _retryRatings = new() { Name = "RetrySkinStatistics", Text = "重试统计", Dock = DockStyle.Fill, Visible = false, Margin = new Padding(8, 8, 0, 8) };
    private readonly Dictionary<int, SkinUsageStatistics> _skinUsage = [];
    private CancellationTokenSource? _ratingRequests;
    private string _ratingModel = "";
    private bool CommunitySort => _skinSort.SelectedIndex == 1;

    private void InitializeSkinSort(bool communityFirst)
    {
        _retryRatings.Click += async (_, _) => await LoadSkinPopularityAsync();
        _skinSort.Items.AddRange(["默认顺序", "使用热度优先（OP.GG）"]);
        _skinSort.SelectedIndex = communityFirst ? 1 : 0;
        _skinSort.SelectedIndexChanged += async (_, _) =>
        {
            try { var settings = _settings.Load(); settings.SkinCommunitySortEnabled = CommunitySort; _settings.Save(settings); }
            catch (Exception error) { _status.Text = $"排序设置保存失败：{error.Message}"; }
            FilterEntries();
            if (!CommunitySort) ResetSkinPopularity();
            await LoadSkinPopularityAsync();
        };
    }

    private Control CreateSkinRatingRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 56, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _ratingStatus.Dock = DockStyle.Fill;
        row.Controls.Add(_ratingStatus, 0, 0); row.Controls.Add(_retryRatings, 1, 0);
        _surfaceRows.Add(row);
        return row;
    }

    private bool RatingsMatchChampion => _ratingModel == _catalog?.Model;

    private IEnumerable<SkinEntry> SortSkinEntries(IEnumerable<SkinEntry> entries) => CommunitySort && RatingsMatchChampion
        ? entries.OrderByDescending(e => _skinUsage.ContainsKey(e.SkinNum))
            .ThenByDescending(e => _skinUsage.GetValueOrDefault(e.SkinNum)?.Plays ?? 0)
        : entries;

    private string GetSkinListName(SkinEntry entry, string? displayName = null) => (displayName ?? GetSkinDisplayName(entry)) + (!CommunitySort ? ""
        : RatingsMatchChampion && _skinUsage.TryGetValue(entry.SkinNum, out var rating)
            ? $" · 选用率 {rating.PickRate:0.00}% / {rating.Plays:N0}场"
            : " · 暂无使用数据");

    private void ResetSkinPopularity()
    {
        var requests = _ratingRequests; _ratingRequests = null;
        requests?.Cancel(); requests?.Dispose();
        _skinUsage.Clear(); _ratingModel = "";
        _retryRatings.Visible = false;
        _ratingStatus.Text = "可按 OP.GG 统计的使用热度排序，优先展示使用场数高的皮肤。";
    }

    private async Task LoadSkinPopularityAsync()
    {
        if (_disposing || !CommunitySort || _catalog == null || _ratingModel == _catalog.Model) return;
        ResetSkinPopularity();
        string model = _catalog.Model;
        _ratingModel = model;
        var requests = _ratingRequests = new CancellationTokenSource();
        var token = requests.Token;
        _ratingStatus.Text = "正在读取 OP.GG 皮肤使用统计…";
        try
        {
            var ratings = await _skinPopularity.GetStatisticsAsync(model, token);
            if (token.IsCancellationRequested || _disposing || _catalog?.Model != model) return;
            foreach (var rating in ratings) _skinUsage[rating.SkinNumber] = rating;
            _retryRatings.Visible = ratings.Count == 0;
            _ratingStatus.Text = ratings.Count == 0 ? "OP.GG 未返回可用的皮肤使用数据，暂按默认顺序显示。"
                : "来源：OP.GG 样本；按使用场数排序，选用率为该英雄样本占比。无数据条目排在后面。";
            FilterEntries();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && !_disposing)
            {
                _ratingStatus.Text = DescribeSkinStatisticsFailure(error);
                _retryRatings.Visible = true;
                RuntimeDiagnostics.Report("OP.GG 皮肤统计", "读取失败", $"{model} | {error.GetType().Name} | {error.Message}");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested && _skinUsage.Count == 0) _ratingModel = "";
        }
    }

    internal static string DescribeSkinStatisticsFailure(Exception error) => error switch
    {
        OperationCanceledException => "OP.GG 请求超时，暂按默认顺序显示。请重试统计。",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => "OP.GG 拒绝访问（403），暂按默认顺序显示。请稍后重试。",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "OP.GG 请求过于频繁（429），请稍后重试统计。",
        HttpRequestException { StatusCode: { } status } => $"OP.GG 服务异常（{(int)status}），暂按默认顺序显示。",
        HttpRequestException when !NetworkTrafficService.Shared.IsEnabled(NetworkModule.Assets) => "资源网络访问已关闭，请在「日志 → 流量监控」开启「图片与游戏资源」。",
        HttpRequestException => "OP.GG 网络暂不可用，暂按默认顺序显示。请检查网络后重试。",
        _ => "OP.GG 统计读取失败，暂按默认顺序显示。详情见日志，可重试统计。"
    };
}
