namespace LOL_GameAssistant.Helper
{
    public class ChampionOption
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public string RealName { get; set; }
        public string Nickname { get; set; }

        public ChampionOption(string label, int value, string realName, string nickname)
        {
            Label = label;
            Value = value;
            RealName = realName;
            Nickname = nickname;
        }
    }

    /// <summary>
    /// 基础英雄数据映射
    /// </summary>
    public static class ChampionMap
    {
        private static readonly Dictionary<int, ChampionOption> _championMap;

        static ChampionMap()
        {
            _championMap = new Dictionary<int, ChampionOption>
        {
            { 0, new ChampionOption("全部", 0, "", "") },
            { 1, new ChampionOption("黑暗之女", 1, "安妮", "火女") },
            { 2, new ChampionOption("狂战士", 2, "奥拉夫", "大头") },
            { 3, new ChampionOption("正义巨像", 3, "加里奥", "城墙") },
            { 4, new ChampionOption("卡牌大师", 4, "崔斯特", "卡牌") },
            { 5, new ChampionOption("德邦总管", 5, "赵信", "菊花信|赵神王") },
            { 6, new ChampionOption("无畏战车", 6, "厄加特", "螃蟹") },
            { 7, new ChampionOption("诡术妖姬", 7, "乐芙兰", "LB") },
            { 8, new ChampionOption("猩红收割者", 8, "弗拉基米尔", "吸血鬼") },
            { 9, new ChampionOption("远古恐惧", 9, "费德提克", "稻草人") },
            { 10, new ChampionOption("正义天使", 10, "凯尔", "天使") },
            { 11, new ChampionOption("无极剑圣", 11, "易", "") },
            { 12, new ChampionOption("牛头酋长", 12, "阿利斯塔", "牛头") },
            { 13, new ChampionOption("符文法师", 13, "瑞兹", "光头") },
            { 14, new ChampionOption("亡灵战神", 14, "赛恩", "老司机") },
            { 15, new ChampionOption("战争女神", 15, "希维尔", "轮子妈") },
            { 16, new ChampionOption("众星之子", 16, "索拉卡", "奶妈") },
            { 17, new ChampionOption("迅捷斥候", 17, "提莫", "蘑菇") },
            { 18, new ChampionOption("麦林炮手", 18, "崔丝塔娜", "小炮") },
            { 19, new ChampionOption("祖安怒兽", 19, "沃里克", "狼人") },
            { 20, new ChampionOption("雪原双子", 20, "努努和威朗普", "雪人") },
            { 21, new ChampionOption("赏金猎人", 21, "厄运小姐", "女枪") },
            { 22, new ChampionOption("寒冰射手", 22, "艾希", "刮痧女王") },
            { 23, new ChampionOption("蛮族之王", 23, "泰达米尔", "蛮王") },
            { 24, new ChampionOption("武器大师", 24, "贾克斯", "武器") },
            { 25, new ChampionOption("堕落天使", 25, "莫甘娜", "") },
            { 26, new ChampionOption("时光守护者", 26, "基兰", "时光老头") },
            { 27, new ChampionOption("炼金术士", 27, "辛吉德", "炼金") },
            { 28, new ChampionOption("痛苦之拥", 28, "伊芙琳", "寡妇") },
            { 29, new ChampionOption("瘟疫之源", 29, "图奇", "老鼠") },
            { 30, new ChampionOption("死亡颂唱者", 30, "卡尔萨斯", "死歌") },
            { 31, new ChampionOption("虚空恐惧", 31, "科加斯", "大虫子") },
            { 32, new ChampionOption("殇之木乃伊", 32, "阿木木", "木乃伊") },
            { 33, new ChampionOption("披甲龙龟", 33, "拉莫斯", "龙龟") },
            { 34, new ChampionOption("冰晶凤凰", 34, "艾尼维亚", "凤凰") },
            { 35, new ChampionOption("恶魔小丑", 35, "萨科", "小丑") },
            { 36, new ChampionOption("祖安狂人", 36, "蒙多医生", "蒙多") },
            { 37, new ChampionOption("琴瑟仙女", 37, "娑娜", "琴女") },
            { 38, new ChampionOption("虚空行者", 38, "卡萨丁", "电耗子") },
            { 39, new ChampionOption("刀锋舞者", 39, "艾瑞莉亚", "刀妹") },
            { 40, new ChampionOption("风暴之怒", 40, "杰娜", "风女") },
            { 41, new ChampionOption("海洋之灾", 41, "普朗克", "船长") },
            { 42, new ChampionOption("英勇投弹手", 42, "库奇", "飞机") },
            { 43, new ChampionOption("天启者", 43, "卡尔玛", "扇子妈") },
            { 44, new ChampionOption("瓦洛兰之盾", 44, "塔里克", "宝石") },
            { 45, new ChampionOption("邪恶小法师", 45, "维迦", "小法") },
            { 48, new ChampionOption("巨魔之王", 48, "特朗德尔", "巨魔") },
            { 50, new ChampionOption("诺克萨斯统领", 50, "斯维因", "乌鸦") },
            { 51, new ChampionOption("皮城女警", 51, "凯特琳", "女警") },
            { 53, new ChampionOption("蒸汽机器人", 53, "布里茨", "机器人") },
            { 54, new ChampionOption("熔岩巨兽", 54, "墨菲特", "石头人") },
            { 55, new ChampionOption("不祥之刃", 55, "卡特琳娜", "卡特") },
            { 56, new ChampionOption("永恒梦魇", 56, "魔腾", "梦魇") },
            { 57, new ChampionOption("扭曲树精", 57, "茂凯", "大树") },
            { 58, new ChampionOption("荒漠屠夫", 58, "雷克顿", "鳄鱼") },
            { 59, new ChampionOption("德玛西亚皇子", 59, "嘉文四世", "皇子") },
            { 60, new ChampionOption("蜘蛛女皇", 60, "伊莉丝", "蜘蛛") },
            { 61, new ChampionOption("发条魔灵", 61, "奥莉安娜", "发条") },
            { 62, new ChampionOption("齐天大圣", 62, "孙悟空", "猴子") },
            { 63, new ChampionOption("复仇焰魂", 63, "布兰德", "火男") },
            { 64, new ChampionOption("盲僧", 64, "李青", "瞎子") },
            { 67, new ChampionOption("暗夜猎手", 67, "薇恩", "VN|uzi|UZI") },
            { 68, new ChampionOption("机械公敌", 68, "兰博", "机器人") },
            { 69, new ChampionOption("魔蛇之拥", 69, "卡西奥佩娅", "蛇女") },
            { 72, new ChampionOption("上古领主", 72, "斯卡纳", "蝎子") },
            { 74, new ChampionOption("大发明家", 74, "海默丁格", "大头") },
            { 75, new ChampionOption("沙漠死神", 75, "内瑟斯", "狗头") },
            { 76, new ChampionOption("狂野女猎手", 76, "奈德丽", "豹女") },
            { 77, new ChampionOption("兽灵行者", 77, "乌迪尔", "德鲁伊") },
            { 78, new ChampionOption("圣锤之毅", 78, "波比", "锤石") },
            { 79, new ChampionOption("酒桶", 79, "古拉加斯", "酒桶") },
            { 80, new ChampionOption("不屈之枪", 80, "潘森", "斯巴达") },
            { 81, new ChampionOption("探险家", 81, "伊泽瑞尔", "EZ") },
            { 82, new ChampionOption("铁铠冥魂", 82, "莫德凯撒", "铁男") },
            { 83, new ChampionOption("牧魂人", 83, "约里克", "掘墓者") },
            { 84, new ChampionOption("离群之刺", 84, "阿卡丽", "阿卡丽") },
            { 85, new ChampionOption("狂暴之心", 85, "凯南", "电耗子") },
            { 86, new ChampionOption("德玛西亚之力", 86, "盖伦", "草丛伦") },
            { 89, new ChampionOption("曙光女神", 89, "蕾欧娜", "日女") },
            { 90, new ChampionOption("虚空先知", 90, "玛尔扎哈", "蚂蚱") },
            { 91, new ChampionOption("刀锋之影", 91, "泰隆", "男刀") },
            { 92, new ChampionOption("放逐之刃", 92, "锐雯", "兔女郎") },
            { 96, new ChampionOption("深渊巨口", 96, "克格莫", "大嘴") },
            { 98, new ChampionOption("暮光之眼", 98, "慎", "慎") },
            { 99, new ChampionOption("光辉女郎", 99, "拉克丝", "光辉") },
            { 101, new ChampionOption("远古巫灵", 101, "泽拉斯", "死亡射线|挠头怪") },
            { 102, new ChampionOption("龙血武姬", 102, "希瓦娜", "龙女") },
            { 103, new ChampionOption("九尾妖狐", 103, "阿狸", "狐狸") },
            { 104, new ChampionOption("法外狂徒", 104, "格雷福斯", "男枪") },
            { 105, new ChampionOption("潮汐海灵", 105, "菲兹", "小鱼人") },
            { 106, new ChampionOption("不灭狂雷", 106, "沃利贝尔", "雷熊") },
            { 107, new ChampionOption("傲之追猎者", 107, "雷恩加尔", "狮子狗") },
            { 110, new ChampionOption("惩戒之箭", 110, "韦鲁斯", "维鲁斯") },
            { 111, new ChampionOption("深海泰坦", 111, "诺提勒斯", "泰坦") },
            { 112, new ChampionOption("奥术先驱", 112, "维克托", "三只手") },
            { 113, new ChampionOption("北地之怒", 113, "瑟庄妮", "猪妹") },
            { 114, new ChampionOption("无双剑姬", 114, "菲奥娜", "剑姬") },
            { 115, new ChampionOption("爆破鬼才", 115, "吉格斯", "炸弹人") },
            { 117, new ChampionOption("仙灵女巫", 117, "璐璐", "露露") },
            { 119, new ChampionOption("荣耀行刑官", 119, "德莱文", "德莱文") },
            { 120, new ChampionOption("战争之影", 120, "赫卡里姆", "人马") },
            { 121, new ChampionOption("虚空掠夺者", 121, "卡兹克", "螳螂") },
            { 122, new ChampionOption("诺克萨斯之手", 122, "德莱厄斯", "诺手") },
            { 126, new ChampionOption("未来守护者", 126, "杰斯", "杰斯") },
            { 127, new ChampionOption("冰霜女巫", 127, "丽桑卓", "冰女") },
            { 131, new ChampionOption("皎月女神", 131, "戴安娜", "皎月") },
            { 133, new ChampionOption("德玛西亚之翼", 133, "奎因", "鸟人") },
            { 134, new ChampionOption("暗黑元首", 134, "辛德拉", "球女") },
            { 136, new ChampionOption("铸星龙王", 136, "奥瑞利安·索尔", "龙王") },
            { 141, new ChampionOption("影流之镰", 141, "凯隐&拉亚斯特", "") },
            { 142, new ChampionOption("暮光星灵", 142, "佐伊", "佐a") },
            { 143, new ChampionOption("荆棘之兴", 143, "婕拉", "植物人") },
            { 145, new ChampionOption("虚空之女", 145, "卡莎", "") },
            { 147, new ChampionOption("星籁歌姬", 147, "萨勒芬妮", "轮椅人") },
            { 150, new ChampionOption("迷失之牙", 150, "纳尔", "") },
            { 154, new ChampionOption("生化魔人", 154, "扎克", "粑粑人") },
            { 157, new ChampionOption("疾风剑豪", 157, "亚索", "索子哥|孤儿索") },
            { 161, new ChampionOption("虚空之眼", 161, "维克兹", "大眼") },
            { 163, new ChampionOption("岩雀", 163, "塔莉垭", "") },
            { 164, new ChampionOption("青钢影", 164, "卡米尔", "") },
            { 166, new ChampionOption("影哨", 166, "阿克尚", "") },
            { 200, new ChampionOption("虚空女皇", 200, "卑尔维斯", "阿尔卑斯|棒棒糖") },
            { 201, new ChampionOption("弗雷尔卓德之心", 201, "布隆", "") },
            { 202, new ChampionOption("戏命师", 202, "烬", "瘸子") },
            { 203, new ChampionOption("永猎双子", 203, "千珏", "") },
            { 221, new ChampionOption("祖安花火", 221, "泽丽", "") },
            { 222, new ChampionOption("暴走萝莉", 222, "金克丝", "") },
            { 223, new ChampionOption("河流之王", 223, "塔姆", "") },
            { 233, new ChampionOption("狂厄蔷薇", 233, "狱卒", "") },
            { 234, new ChampionOption("破败之王", 234, "佛耶戈", "") },
            { 235, new ChampionOption("涤魂圣枪", 235, "塞纳", "") },
            { 236, new ChampionOption("圣枪游侠", 236, "卢锡安", "") },
            { 238, new ChampionOption("影流之主", 238, "劫", "幽默飞镖人") },
            { 240, new ChampionOption("暴怒骑士", 240, "克烈", "") },
            { 245, new ChampionOption("时间刺客", 245, "艾克", "") },
            { 246, new ChampionOption("元素女皇", 246, "奇亚娜", "超模") },
            { 254, new ChampionOption("皮城执法官", 254, "蔚", "") },
            { 266, new ChampionOption("暗裔剑魔", 266, "亚托克斯", "") },
            { 267, new ChampionOption("唤潮鲛姬", 267, "娜美", "") },
            { 268, new ChampionOption("沙漠皇帝", 268, "阿兹尔", "黄鸡") },
            { 350, new ChampionOption("魔法猫咪", 350, "悠米", "") },
            { 360, new ChampionOption("沙漠玫瑰", 360, "莎米拉", "") },
            { 412, new ChampionOption("魂锁典狱长", 412, "锤石", "") },
            { 420, new ChampionOption("海兽祭司", 420, "俄洛伊", "触手妈") },
            { 421, new ChampionOption("虚空遁地兽", 421, "雷克赛", "挖掘机") },
            { 427, new ChampionOption("翠神", 427, "艾翁", "小树") },
            { 429, new ChampionOption("复仇之矛", 429, "卡莉丝塔", "") },
            { 432, new ChampionOption("星界游神", 432, "巴德", "") },
            { 497, new ChampionOption("幻翎", 497, "洛", "") },
            { 498, new ChampionOption("逆羽", 498, "霞", "") },
            { 516, new ChampionOption("山隐之焰", 516, "奥恩", "山羊") },
            { 517, new ChampionOption("解脱者", 517, "塞拉斯", "") },
            { 518, new ChampionOption("万花通灵", 518, "妮蔻", "") },
            { 523, new ChampionOption("残月之肃", 523, "厄斐琉斯", "efls") },
            { 526, new ChampionOption("镕铁少女", 526, "芮尔", "") },
            { 555, new ChampionOption("血港鬼影", 555, "派克", "") },
            { 711, new ChampionOption("愁云使者", 711, "薇古斯", "") },
            { 777, new ChampionOption("封魔剑魂", 777, "永恩", "") },
            { 799, new ChampionOption("铁血狼母", 799, "安蓓萨", "") },
            { 800, new ChampionOption("流光镜影", 800, "梅尔", "三体人") },
            { 875, new ChampionOption("腕豪", 875, "瑟提", "") },
            { 876, new ChampionOption("含羞蓓蕾", 876, "莉莉娅", "") },
            { 887, new ChampionOption("灵罗娃娃", 887, "格温", "") },
            { 888, new ChampionOption("炼金男爵", 888, "烈娜塔・戈拉斯克", "") },
            { 893, new ChampionOption("双界灵兔", 893, "阿萝拉", "兔子") },
            { 895, new ChampionOption("不羁之悦", 895, "尼菈", "水米拉|水弥拉") },
            { 897, new ChampionOption("纳祖芒荣耀", 897, "奎桑提", "黑哥") },
            { 901, new ChampionOption("炽炎雏龙", 901, "斯莫德", "小火龙") },
            { 902, new ChampionOption("明烛", 902, "米利欧", "顶真|丁真") },
            { 910, new ChampionOption("异画师", 910, "慧", "毛笔人") },
            { 950, new ChampionOption("百裂冥犬", 950, "纳亚菲利", "狼狗|狗比") }
        };
        }

        public static Dictionary<int, ChampionOption> GetChampionMap()
        {
            return _championMap;
        }

        public static ChampionOption GetChampion(int id)
        {
            _championMap.TryGetValue(id, out var champion);
            return champion;
        }
    }

    /// <summary>
    /// 根据id获取英雄名
    /// </summary>
    public static class GetChampionHelper
    {
        public static ChampionOption GetChampionOption(int id)
        {
            return ChampionMap.GetChampion(id);
        }
    }
}