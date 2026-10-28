using System.Collections.Frozen;

namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>LCU/SGP 大区编号的展示名称。来源、历史别名和适用范围见 docs/league-region-codes.md。</summary>
public static class LeagueRegionNames
{
    private static readonly (string Code, string Name)[] TencentPlatforms =
    [
        ("HN1", "艾欧尼亚"), ("HN2", "祖安"), ("HN3", "诺克萨斯"), ("HN4", "班德尔城"),
        ("HN5", "皮尔特沃夫"), ("HN6", "战争学院"), ("HN7", "巨神峰"), ("HN8", "雷瑟守备"),
        ("HN9", "裁决之地"), ("HN10", "黑色玫瑰"), ("HN11", "暗影岛"), ("HN12", "钢铁烈阳"),
        ("HN13", "水晶之痕"), ("HN14", "均衡教派"), ("HN15", "影流"), ("HN16", "守望之海"),
        ("HN17", "征服之海"), ("HN18", "卡拉曼达"), ("HN19", "皮城警备"),
        ("WT1", "比尔吉沃特"), ("WT2", "德玛西亚"), ("WT3", "弗雷尔卓德"), ("WT4", "无畏先锋"),
        ("WT5", "恕瑞玛"), ("WT6", "扭曲丛林"), ("WT7", "巨龙之巢"), ("EDU1", "教育网专区"),
        ("BGP1", "男爵领域"), ("BGP2", "峡谷之巅"),
        ("NJ100", "联盟一区"), ("GZ100", "联盟二区"), ("CQ100", "联盟三区"),
        ("TJ100", "联盟四区"), ("TJ101", "联盟五区")
    ];

    private static readonly (string Code, string Name)[] InternationalPlatforms =
    [
        ("KR", "韩服"), ("NA1", "北美"), ("EUW1", "欧洲西部"), ("EUN1", "欧洲北部"),
        ("JP1", "日服"), ("BR1", "巴西"), ("LA1", "拉丁美洲北部"), ("LA2", "拉丁美洲南部"),
        ("OC1", "大洋洲"), ("RU", "俄罗斯"), ("TR1", "土耳其"), ("ME1", "中东"),
        ("PH2", "菲律宾"), ("SG2", "新加坡"), ("TH2", "泰国"), ("TW2", "台湾"), ("VN2", "越南"),
        ("PBE1", "测试服（PBE）")
    ];

    private static readonly (string Alias, string Code)[] TencentAliases =
    [
        ("HN4_NEW", "HN4"), ("HN14_NEW", "HN14"), ("HN16_NEW", "HN16"), ("HN18_NEW", "HN18"),
        ("WT1_NEW", "WT1"), ("WT2_NEW", "WT2"), ("WT3_NEW", "WT3"), ("WT4_NEW", "WT4")
    ];

    private static readonly (string Alias, string Code)[] InternationalAliases =
    [
        ("NA", "NA1"), ("BR", "BR1"), ("EUW", "EUW1"), ("EUNE", "EUN1"),
        ("JP", "JP1"), ("TR", "TR1"), ("LAN", "LA1"), ("LAS", "LA2"), ("OCE", "OC1"),
        ("RU1", "RU"), ("PH1", "PH2"), ("SG1", "SG2"), ("TH1", "TH2"),
        ("TW1", "TW2"), ("VN1", "VN2"), ("PBE", "PBE1")
    ];

    private static readonly FrozenDictionary<string, string> NamesByPlatform = BuildNames();

    /// <summary>招募等界面的去重选项；历史编号用于识别，不重复展示。</summary>
    public static IReadOnlyList<string> DisplayNames { get; } = Array.AsReadOnly(TencentPlatforms
        .Concat(InternationalPlatforms).Select(region => region.Name).Append("国服体验服").Distinct().ToArray());

    /// <summary>仅匹配有来源的编号，不将 TENCENT、AMERICAS 等聚合区域猜成具体大区。</summary>
    public static bool TryGetName(string? platform, out string name) =>
        NamesByPlatform.TryGetValue(platform?.Trim() ?? "", out name!);

    private static FrozenDictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in TencentPlatforms)
        {
            names.Add(region.Code, region.Name);
            names.Add("TENCENT_" + region.Code, region.Name);
        }
        foreach (var region in InternationalPlatforms) names.Add(region.Code, region.Name);
        foreach (var alias in TencentAliases)
        {
            names.Add(alias.Alias, names[alias.Code]);
            names.Add("TENCENT_" + alias.Alias, names[alias.Code]);
        }
        foreach (var alias in InternationalAliases) names.Add(alias.Alias, names[alias.Code]);
        // PBE 单独出现时无法区分腾讯体验服与国际测试服，保留通用名称。
        names.Add("TENCENT_PBE", "国服体验服");
        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
