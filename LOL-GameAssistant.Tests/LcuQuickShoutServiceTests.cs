using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class LcuQuickShoutServiceTests
{
    [Fact]
    public async Task SendsToChampionSelectGroupInsteadOfPrivateConversation()
    {
        var sender = new FakeSender
        {
            Conversations = """[{"id":"friend@example","type":"chat"},{"id":"party@example","type":"party"},{"id":"champ@example","type":"championSelect"}]"""
        };

        string result = await new LcuQuickShoutService(sender).SendAsync("准备打龙");

        Assert.Equal("已发送到客户端群聊。", result);
        Assert.Equal("/lol-chat/v1/conversations/champ%40example/messages", sender.PostedEndpoint);
        Assert.Equal("准备打龙", JObject.Parse(sender.PostedBody!).Value<string>("body"));
    }

    [Fact]
    public async Task DoesNotSendWhenOnlyPrivateConversationExists()
    {
        var sender = new FakeSender { Conversations = """[{"id":"friend@example","type":"chat"}]""" };

        string result = await new LcuQuickShoutService(sender).SendAsync("准备打龙");

        Assert.Contains("不会发送到私人聊天", result);
        Assert.Null(sender.PostedEndpoint);
    }

    [Fact]
    public async Task PerCharacterModeSendsSeparateMessagesToTheSameGroup()
    {
        var sender = new FakeSender { Conversations = """[{"id":"party@example","type":"party"}]""" };

        string result = await new LcuQuickShoutService(sender).SendAsync("集合", perCharacter: true);

        Assert.Contains("共 2 字", result);
        Assert.Equal(new[] { "集", "合" }, sender.PostedBodies
            .Select(body => JObject.Parse(body).Value<string>("body")));
    }

    [Fact]
    public async Task BatchSendsSelectedPhrasesInOrderUsingOneConversationLookup()
    {
        var sender = new FakeSender { Conversations = """[{"id":"party@example","type":"party"}]""" };

        string result = await new LcuQuickShoutService(sender)
            .SendBatchAsync(["准备打龙", "先做视野", "一起集合"]);

        Assert.Contains("共 3 条", result);
        Assert.Equal(1, sender.ConversationLookupCount);
        Assert.Equal(new[] { "准备打龙", "先做视野", "一起集合" }, sender.PostedBodies
            .Select(body => JObject.Parse(body).Value<string>("body")));
    }

    [Fact]
    public async Task BatchStopsAfterClientRejectsMessageAndReportsProgress()
    {
        var sender = new FakeSender
        {
            Conversations = """[{"id":"party@example","type":"party"}]""",
            FailAtPost = 2
        };

        string result = await new LcuQuickShoutService(sender)
            .SendBatchAsync(["第一句", "第二句", "第三句"]);

        Assert.Contains("已发送 1/3", result);
        Assert.Equal(2, sender.PostedBodies.Count);
    }

    private sealed class FakeSender : ILcuRequestSender
    {
        public string Conversations { get; set; } = "[]";
        public string? PostedEndpoint { get; private set; }
        public string? PostedBody { get; private set; }
        public List<string> PostedBodies { get; } = new();
        public int ConversationLookupCount { get; private set; }
        public int FailAtPost { get; set; } = -1;

        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            ConversationLookupCount++;
            return Task.FromResult<string?>(Conversations);
        }

        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            PostedEndpoint = endpoint;
            PostedBody = jsonBody;
            PostedBodies.Add(jsonBody);
            return Task.FromResult(PostedBodies.Count != FailAtPost);
        }

        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
