namespace LOL_GameAssistant.Domain.GameData;

/// <summary>属性碎片不在 runesReforged 索引中，使用共享 StatMods 资源。</summary>
internal static class RuneStatShards
{
    internal sealed record Shard(string Name, string ShortName, string Icon, string Description)
    {
        internal string IconPath => "perk-images/StatMods/StatMods" + Icon + "Icon.png";
        internal string Details => Name + "\n" + Description;
    }
    internal static Shard? Find(int id) => id switch
    {
        5001 => new("成长生命值", "生命", "HealthScaling", "生命值随等级成长。"),
        5002 => new("护甲", "护甲", "Armor", "增加护甲。"),
        5003 => new("魔法抗性", "魔抗", "MagicRes", "增加魔法抗性。"),
        5005 => new("攻击速度", "攻速", "AttackSpeed", "增加攻击速度。"),
        5007 => new("技能急速", "急速", "CDRScaling", "增加技能急速。"),
        5008 => new("适应之力", "适应", "AdaptiveForce", "提供攻击力或法术强度。"),
        5010 => new("移动速度", "移速", "MovementSpeed", "提高移动速度。"),
        5011 => new("生命值", "生命", "HealthPlus", "增加生命值。"),
        5013 => new("韧性与减速抗性", "韧性", "Tenacity", "提高韧性与减速抗性。"),
        _ => null
    };
}
