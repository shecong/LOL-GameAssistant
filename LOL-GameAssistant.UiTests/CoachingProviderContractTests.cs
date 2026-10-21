using System.Net;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Ai;
using LOL_GameAssistant.Infrastructure.LiveGame;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class CoachingProviderContractTests
{
    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Claude)]
    public async Task BothProtocolsSendAnonymousFactsAndRequirePlansEvenForLegacyTextSettings(AiProvider provider)
    {
        var snapshot = CoachingTelemetryParser.Parse(CoachingTelemetryTests.Sample)!;
        var context = new AiGameContext { Phase = "InProgress", MyChampionId = 1, MyChampion = "安妮", MyRole = "MIDDLE",
            HasLiveClientData = true, CurrentGold = snapshot.CurrentGold, CurrentItems = snapshot.Items,
            GameTimeSeconds = snapshot.GameTimeSeconds, Telemetry = snapshot.Telemetry,
            AlliedChampions = ["安妮"], EnemyChampions = ["盖伦"] };
        string reply = """
            {"recommendations":[{"opportunityId":"survival.health","priority":"important","title":"低血先退出近身范围","actionKey":"survival:disengage","factIds":["champion","self.health"],"steps":["安妮先退到己方队友或防御塔一侧，停止向前换血。","留住控制技能保护撤退路线，确认安全后再处理补给。"],"conditions":"确认撤退路线可见且没有敌人正在包夹时","why":"当前生命只剩四分之一，安妮继续近身换血难以承受对方反击，应先保留控制换取退路。","expectedOutcome":"避免再承受一轮换血并保住回补机会","fallback":"退路被堵时先靠近可支援队友，不穿过无视野区域。","invalidWhen":"生命恢复到安全区间或退路发生变化时停止"}]}
            """;
        using var handler = new StubHandler(provider, reply);
        using var http = new HttpClient(handler);
        var service = new CloudAiRecommendationService(http);
        var result = await service.GetRecommendationAsync(new CloudAiSettings
        {
            Provider = provider, Model = "fixture-model", BaseUrl = "https://fixture.invalid",
            EncryptedApiKey = SettingSecretProtector.Protect("fixture-only-not-a-real-key"), StructuredOutputEnabled = false
        }, context);
        Assert.True(result.FromAi);
        Assert.Equal(1, handler.Calls);
        using var request = JsonDocument.Parse(handler.Body);
        string messages = string.Join("\n", request.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
        string system = provider == AiProvider.Claude ? request.RootElement.GetProperty("system").GetString()! : messages;
        Assert.Contains("factIds", system);
        Assert.Contains("fallback", system);
        Assert.Contains("1450", messages);
        Assert.Contains("法师之靴", messages);
        Assert.DoesNotContain("private-player", handler.Body);
        Assert.DoesNotContain("enemy-old", handler.Body);
        var card = Assert.Single(CloudRecommendationParser.Parse(result.Recommendation, DateTimeOffset.UtcNow, context, true));
        Assert.False(card.IsFormatError);
        Assert.Equal(2, card.Steps.Count);
    }

    private sealed class StubHandler(AiProvider provider, string reply) : HttpMessageHandler
    {
        public int Calls;
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(token);
            string json = provider == AiProvider.Claude
                ? JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = reply } } })
                : JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
