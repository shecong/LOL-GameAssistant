using System.Globalization;
using System.Text.Json;

namespace LOL_GameAssistant.Domain.Coaching;

public sealed record CoachingFact(string Id, string Value, string Version);
public sealed record CoachingOpportunity(string Id, string Category, string Question, string[] RequiredFacts,
    string[] AllowedActions, RecommendationPriority MaxPriority, int LifetimeSeconds);

/// <summary>先在本机建立可追溯事实与可回答的问题，模型只负责在此边界内制定具体计划。</summary>
public sealed record CoachingDecisionFrame(IReadOnlyList<CoachingFact> Facts, IReadOnlyList<CoachingOpportunity> Opportunities)
{
    public string ToPromptText() => JsonSerializer.Serialize(new
    {
        facts = Facts.Select(f => new { id = f.Id, value = f.Value }),
        opportunities = Opportunities.Select(o => new { id = o.Id, category = o.Category, question = o.Question,
            requiredFacts = o.RequiredFacts, allowedActions = o.AllowedActions, maxPriority = o.MaxPriority.ToString().ToLowerInvariant(), lifetimeSeconds = o.LifetimeSeconds })
    });

    public static CoachingDecisionFrame Create(AiGameContext c)
    {
        var facts = new List<CoachingFact>();
        var opportunities = new List<CoachingOpportunity>();
        void Fact(string id, string value, string? version = null) => facts.Add(new(id, value, version ?? value));
        void Opportunity(string id, string category, string question, string[] required, string[] actions,
            RecommendationPriority priority = RecommendationPriority.Attention, int ttl = 90)
        {
            if (required.All(id => facts.Any(f => f.Id == id))) opportunities.Add(new(id, category, question, required, actions, priority, ttl));
        }
        string List(IEnumerable<string> values) => string.Join("、", values);
        if (c.MyChampionId > 0) Fact("champion", c.MyChampion);
        if (c.MyRole is not ("" or "通用" or "未知位置")) Fact("role", c.MyRole);
        if (c.AlliedChampions.Count > 0) Fact("allies", List(c.AlliedChampions));
        if (c.EnemyChampions.Count > 0) Fact("enemies", List(c.EnemyChampions));
        Fact("phase", c.Phase + " / " + c.Mode);
        if (c.HasLiveClientData)
        {
            Fact("time", c.GameTimeText, (c.GameTimeSeconds / 180).ToString(CultureInfo.InvariantCulture));
            if (c.Telemetry == null || c.Telemetry.Self?.HasItems == true) Fact("items", c.CurrentItems.Count > 0 ? List(c.CurrentItems) : "无装备");
            if (c.Telemetry == null || c.Telemetry.Gold.HasValue) Fact("gold", c.CurrentGold.ToString(CultureInfo.InvariantCulture), (c.CurrentGold / 500).ToString(CultureInfo.InvariantCulture));
        }
        if (c.MatchupChampion.Length > 0) Fact("matchup", c.MatchupChampion + "（玩家确认）");
        if (c.ConfirmedAugmentOptions.Count > 0) Fact("augments", List(c.ConfirmedAugmentOptions) + "（候选，未选定）");
        if (c.ConfirmedAugmentDescriptions.Count > 0) Fact("augments.effects", List(c.ConfirmedAugmentDescriptions) + "（内置说明，数值以游戏面板为准）");
        if (c.Phase == "ChampSelect")
        {
            Opportunity("draft.plan", "选人", "用本英雄、己方搭档和敌方威胁明确开局分工；列出先手/反手对象和触发条件，说明配置方向的取舍。禁止假定敌方分路、队友已同意或实际符文。", ["champion", "allies", "enemies"], ["draft:role", "draft:plan"], ttl: 120);
            return new(facts, opportunities);
        }
        if (c.Phase != "InProgress" || !c.HasLiveClientData) return new(facts, opportunities);
        bool rift = c.GameMode.Trim().ToUpperInvariant() is "CLASSIC" or "CLASSIC SR";
        var t = c.Telemetry;
        if (t != null)
        {
            if (t.Level.HasValue) Fact("self.level", t.Level.Value.ToString());
            if (t.HealthPercent is { } hp) Fact("self.health", $"{t.Health:0}/{t.MaxHealth:0}（{hp}%）", HealthBand(hp));
            if (t.ManaPercent is { } mana) Fact("self.mana", $"{t.Resource:0}/{t.MaxResource:0} 法力（{mana}%）", mana <= 25 ? "low" : "normal");
            if (t.Abilities.Count > 0) Fact("self.abilities", List(t.Abilities.Select(a => $"{a.Slot} {a.Name} {a.Rank}级（冷却未知）")));
            foreach (var p in t.Players)
            {
                string prefix = p.IsSelf ? "self" : p.Key;
                Fact(prefix + ".champion", (p.IsAlly ? "己方 " : "敌方 ") + p.Champion);
                if (p.IsDead.HasValue) Fact(prefix + ".life", p.IsDead.Value ? $"阵亡，剩余复活 {p.RespawnSeconds?.ToString() ?? "未知"} 秒" : "存活", p.IsDead.Value ? "dead" : "alive");
                if (p.HasItems) Fact(prefix + ".items", List(p.Items.Select(i => $"{i.Name} #{i.Id} ×{i.Count}")));
                if (p.Level.HasValue && !p.IsSelf) Fact(prefix + ".level", p.Level.ToString()!);
                if (p.Kills.HasValue && p.Deaths.HasValue && p.Assists.HasValue)
                    Fact(prefix + ".score", $"KDA {p.Kills}/{p.Deaths}/{p.Assists}（不代表金币差）");
                if (p.IsSelf && p.CreepScore.HasValue) Fact("self.cs", $"补刀 {p.CreepScore}", (p.CreepScore.Value / 10).ToString());
                if (p.SummonerSpells.Count > 0) Fact(prefix + ".spells", List(p.SummonerSpells) + "（冷却未知）");
                if (p.IsSelf && p.Runes.Count > 0) Fact("self.runes", List(p.Runes));
            }
            var allies = t.Players.Where(p => p.IsAlly).ToArray();
            var enemies = t.Players.Where(p => !p.IsAlly).ToArray();
            if (t.HasCompleteRoster && allies.Length > 0 && enemies.Length > 0 && t.Players.All(p => p.IsDead.HasValue))
                Fact("team.life", $"全图存活：己方 {allies.Count(p => p.IsDead == false)}/{allies.Length}，敌方 {enemies.Count(p => p.IsDead == false)}/{enemies.Length}。位置未知，不是局部人数。",
                    List(t.Players.Select(p => p.Key + ":" + p.IsDead)));
            foreach (var e in t.Events.Where(e => c.GameTimeSeconds - e.AtSeconds <= 90))
                Fact("event." + e.Id, $"{e.AtSeconds / 60}:{e.AtSeconds % 60:D2} {e.Kind}，来源席位 {e.Actor}，目标席位 {e.Target}；不代表当前位置或下次刷新时间");
        }
        if (t?.Self?.IsDead == true)
        {
            Opportunity("death.reset", "复活计划", "复活前准备下一轮行动，复活后先核对安全兵线/队友位置；仅当该模式当前允许购物时讨论购买。不要给阵亡玩家移动、换血或开团指令，不确定玩法规则时只做队伍协同准备。", ["champion", "self.life", "items", "gold"], ["reset:respawn"], ttl: 45);
            return new(facts, opportunities);
        }
        if (t?.HealthPercent <= 30)
            Opportunity("survival.health", "生存", c.IsAram || !rift
                ? "低生命下指定退到哪类队友身后、保留哪个保命技能、何种可见条件下停止消耗；大乱斗不得建议回城补给或主动送死。"
                : "低生命时给出停止换血、交接兵线和寻找安全回城位置的步骤；兵线、敌人位置未知，必须写明现场检查条件。", ["champion", "self.health"], ["survival:disengage"], RecommendationPriority.Important, 25);
        if (t?.ManaPercent <= 25)
            Opportunity("resource.mana", "技能资源", "按本英雄留给逃生/关键控制的技能与清线取舍说明如何节省法力；不知道技能消耗，不得声称剩余法力能施放几次。", ["champion", "self.mana", "self.abilities"], ["resource:conserve"], ttl: 40);
        if (t?.HealthPercent is null or > 30 && t?.Abilities.Count > 0)
            Opportunity("skills.sequence", "技能与配合", "结合已学技能、英雄和敌我阵容，指定应保留的技能、先后手顺序与出手对象；技能等级只证明已学，不证明冷却完毕。只有比当前团战计划更具体时才输出，不能重复同一策略。",
                ["champion", "self.abilities", "enemies"], ["skills:sequence", "skills:reserve"], ttl: 75);
        var equippedEnemy = t?.Players.Where(p => !p.IsAlly && p.HasItems && p.Items.Count > 0).OrderByDescending(p => p.Items.Count).FirstOrDefault();
        if (equippedEnemy != null && c.CurrentGold >= 1000)
            Opportunity("build.response", "装备针对", "结合这名敌人的已购装备和具体技能，解释本英雄下一次购物为什么应选某类防御/穿透/功能属性、会牺牲什么收益。不要仅说看阵容出装，不根据KDA推算经济，不给未知价格和合成清单。大乱斗仅作下次可购物时的规划。",
                ["champion", "items", "gold", equippedEnemy.Key + ".champion", equippedEnemy.Key + ".items"], ["build:damage", "build:survival", "build:utility"], ttl: 90);
        if (rift && !c.IsAram && c.CurrentGold >= 1000)
            Opportunity("economy.spend", "回城与装备", "这笔未花金币如何转化为下一波战斗力：先确认回城窗口，再按敌方威胁和现有装备选择属性/组件方向；1000是提醒阈值不是装备价格，禁止编造购买清单价格或断言合成得起。", ["champion", "gold", "items", "enemies"], ["economy:reset", "build:damage", "build:survival", "build:utility"], ttl: 60);
        if (rift && !c.IsAram && c.MatchupChampion.Length > 0 && c.GameTimeSeconds < 900 && t?.HealthPercent is null or > 30)
            Opportunity("lane.matchup", "对线", "结合确认对线英雄，指出一个具体技能交互、换血顺序/补刀站位和撤退条件；关键技能是否已交只能作为玩家现场确认条件。禁止声称兵线正在推进或打野位置。", ["champion", "matchup", "items"], ["lane:trade", "lane:farm", "lane:wave"], ttl: 75);
        if (c.FarmWindow is { Seconds: >= 90, DeathsAdded: 0, CreepScoreGained: <= 3 } farm && rift && !c.IsAram && c.MyRole.ToUpperInvariant() is "上单" or "中单" or "下路" or "TOP" or "MIDDLE" or "MID" or "BOTTOM" or "ADC")
        {
            Fact("farm.window", $"最近 {farm.Seconds} 秒补刀增加 {farm.CreepScoreGained}，期间死亡增加 0；不包含兵线位置，未判断是否漏刀。", "low-income");
            Opportunity("economy.farm", "发育", "近期补刀增长很少：先检查是否存在无人接的安全兵线，再决定接线或继续跟团；不能把支援期间低补刀直接判为失误，也不能硬套职业选手补刀标准。", ["champion", "role", "farm.window"], ["lane:farm", "macro:catchwave"], ttl: 60);
        }
        if (facts.Any(f => f.Id == "team.life"))
            Opportunity("team.numbers", "人数与资源", "只根据全图存活名单规划条件性推进/防守：先核对附近队友和目标区视野；明确谁复活后窗口结束。人数不足时不得建议强开，人数占优也不等于附近人多。", ["champion", "team.life"], ["macro:objective", "macro:defend", "teamfight:protect"], ttl: 30);
        var objective = t?.Events.LastOrDefault(e => e.Kind is "DragonKill" or "BaronKill" or "HeraldKill" or "TurretKilled" or "InhibKilled" && c.GameTimeSeconds - e.AtSeconds <= 90);
        if (objective != null)
            Opportunity("macro.transition", "资源后转线", "刚发生资源/建筑事件，给出接下来的回补、接线、布视野或防守顺序；不推算版本相关刷新计时，不在缺少归属时断言我方拿到资源。", ["champion", "event." + objective.Id], ["macro:rotate", "macro:vision", "macro:defend"], ttl: 45);
        if (t?.HealthPercent is null or > 30)
            Opportunity("fight.plan", "团战职责", "指定本英雄本轮要保护的己方英雄或需要规避的敌方英雄与技能、进场次序和退场条件。阵容优势只是战术推断；具体技能是否可用须现场确认。不要仅写注意站位/配合队友。", ["champion", "allies", "enemies", "items"], ["teamfight:engage", "teamfight:protect", "teamfight:position"], ttl: 90);
        if (c.ConfirmedAugmentOptions.Count > 0)
            Opportunity("augment.choice", "增幅选择", "只在提供了效果说明的已确认候选中比较，与当前装备/英雄技能的收益关联；首选和替代选择须给出条件，不编造效果或数值。不完整说明时不作全候选排名。", ["champion", "items", "augments", "augments.effects"], ["augment:synergy"], ttl: 30);
        return new(facts, opportunities);
    }

    public static string HealthBand(int percent) => percent <= 30 ? "critical" : percent <= 60 ? "wounded" : "healthy";
}
