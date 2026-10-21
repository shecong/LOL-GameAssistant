using LOL_GameAssistant.Infrastructure.LiveGame;
using LOL_GameAssistant.Domain.Coaching;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class CoachingTelemetryTests
{
    internal const string Sample = """
        {"activePlayer":{"riotId":"private-player#CN","summonerName":"private-old-name","currentGold":1450.7,"level":7,
          "championStats":{"currentHealth":250,"maxHealth":1000,"resourceType":"MANA","resourceValue":120,"resourceMax":600},
          "abilities":{"Q":{"displayName":"碎裂之火","abilityLevel":4}}},
         "gameData":{"gameTime":610.4,"gameMode":"CLASSIC"},
         "allPlayers":[{"riotId":"enemy#CN","summonerName":"enemy-old","championName":"盖伦","team":"CHAOS","isDead":true,"respawnTimer":20,"items":[{"itemID":1001,"displayName":"鞋子"}],"level":6},
           {"riotId":"private-player#CN","summonerName":"private-old-name","championName":"安妮","team":"ORDER","isDead":false,"level":7,
            "items":[{"itemID":3020,"displayName":"法师之靴","count":1}],"scores":{"kills":2,"deaths":1,"assists":3,"creepScore":65,"wardScore":4.5}}],
         "events":{"Events":[{"EventID":1,"EventName":"ChampionKill","EventTime":600,"KillerName":"private-player#CN","VictimName":"enemy-old"},
           {"EventID":2,"EventName":"DragonKill","EventTime":200,"KillerName":"enemy-old"}]}}
        """;

    [Fact]
    public void GoldComesFromActivePlayerAndItemsFromMatchedRosterMember()
    {
        var snapshot = Assert.IsType<LocalLiveClientSnapshot>(CoachingTelemetryParser.Parse(Sample));
        Assert.Equal(1450, snapshot.CurrentGold);
        Assert.Equal("法师之靴", Assert.Single(snapshot.Items));
        Assert.Equal(610, snapshot.GameTimeSeconds);
        Assert.Equal(25, snapshot.Telemetry!.HealthPercent);
        Assert.Equal(20, snapshot.Telemetry.ManaPercent);
        Assert.Equal(65, snapshot.Telemetry.Self!.CreepScore);
        Assert.Equal("p1", Assert.Single(snapshot.Telemetry.Events).Actor);
        string serialized = System.Text.Json.JsonSerializer.Serialize(snapshot.Telemetry);
        Assert.DoesNotContain("private", serialized);
        Assert.DoesNotContain("enemy-old", serialized);
    }

    [Fact]
    public void MissingAndAmbiguousIdentityFailClosedInsteadOfUsingFirstPlayer()
    {
        var root = JObject.Parse(Sample);
        root["activePlayer"]!["riotId"] = "missing";
        root["activePlayer"]!["summonerName"] = "missing";
        Assert.Null(CoachingTelemetryParser.Parse(root.ToString()));
        root = JObject.Parse(Sample);
        ((JArray)root["allPlayers"]!).Add(root["allPlayers"]![1]!.DeepClone());
        Assert.Null(CoachingTelemetryParser.Parse(root.ToString()));
    }

    [Fact]
    public void MissingStatsStayUnknownAndZeroGoldIsStillARealObservation()
    {
        var root = JObject.Parse(Sample);
        ((JObject)root["activePlayer"]!).Remove("championStats");
        root["activePlayer"]!["currentGold"] = 0;
        var snapshot = CoachingTelemetryParser.Parse(root.ToString())!;
        Assert.Null(snapshot.Telemetry!.HealthPercent);
        Assert.Equal(0, snapshot.Telemetry.Gold);
        ((JObject)root["activePlayer"]!).Remove("currentGold");
        Assert.Null(CoachingTelemetryParser.Parse(root.ToString())!.Telemetry!.Gold);
    }
}
