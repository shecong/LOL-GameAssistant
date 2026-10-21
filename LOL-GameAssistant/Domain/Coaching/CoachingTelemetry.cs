namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>同一次本机采样的可见事实。缺失字段保留 null，玩家身份在进入领域层前移除。</summary>
public sealed record CoachingTelemetry
{
    public DateTimeOffset SampledAt { get; init; } = DateTimeOffset.UtcNow;
    public int? Gold { get; init; }
    public int? Level { get; init; }
    public double? Health { get; init; }
    public double? MaxHealth { get; init; }
    public string ResourceType { get; init; } = "";
    public double? Resource { get; init; }
    public double? MaxResource { get; init; }
    public IReadOnlyList<ObservedAbility> Abilities { get; init; } = [];
    public IReadOnlyList<ObservedPlayer> Players { get; init; } = [];
    public IReadOnlyList<ObservedGameEvent> Events { get; init; } = [];
    public bool HasEvents { get; init; }
    public bool HasCompleteRoster { get; init; }
    public ObservedPlayer? Self => Players.FirstOrDefault(p => p.IsSelf);
    public int? HealthPercent => MaxHealth > 0 && Health.HasValue ? (int)Math.Clamp(100 * Health.Value / MaxHealth.Value, 0, 100) : null;
    public int? ManaPercent => ResourceType == "MANA" && MaxResource > 0 && Resource.HasValue
        ? (int)Math.Clamp(100 * Resource.Value / MaxResource.Value, 0, 100) : null;
}

public sealed record ObservedAbility(string Slot, string Name, int Rank);
public sealed record ObservedItem(int Id, string Name, int Count);
public sealed record ObservedPlayer
{
    public string Key { get; init; } = "";
    public string Champion { get; init; } = "";
    public string Position { get; init; } = "";
    public bool IsAlly { get; init; }
    public bool IsSelf { get; init; }
    public int? Level { get; init; }
    public bool? IsDead { get; init; }
    public int? RespawnSeconds { get; init; }
    public int? Kills { get; init; }
    public int? Deaths { get; init; }
    public int? Assists { get; init; }
    public int? CreepScore { get; init; }
    public double? VisionScore { get; init; }
    public IReadOnlyList<ObservedItem> Items { get; init; } = [];
    public bool HasItems { get; init; }
    public IReadOnlyList<string> SummonerSpells { get; init; } = [];
    public IReadOnlyList<string> Runes { get; init; } = [];
}

/// <summary>Actor/Target 仅使用匿名席位；不保留原始召唤师名。</summary>
public sealed record ObservedGameEvent(int Id, string Kind, int AtSeconds, string Actor, string Target);
