using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class AiServiceErrorMessageTests
{
    [Fact]
    public void UnsupportedModel_KeepsServerReasonSoTheUserCanSeeWhatIsWrong()
    {
        const string body = """{"error":{"message":"Model Not Exist","type":"invalid_request_error"}}""";

        string message = AiServiceErrorMessage.Describe(400, body);

        Assert.Contains("模型名称", message);
        Assert.Contains("Model Not Exist", message);
    }

    [Fact]
    public void ClaudeStyleErrorBody_IsAlsoRead()
    {
        const string body = """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";

        string message = AiServiceErrorMessage.Describe(401, body);

        Assert.Contains("API Key 无效", message);
        Assert.Contains("invalid x-api-key", message);
    }

    [Fact]
    public void KeyEchoedByServer_IsMaskedBeforeShowing()
    {
        const string body = """{"error":{"message":"Authentication Fails, Your api key: sk-abcdef123456 is invalid"}}""";

        string message = AiServiceErrorMessage.Describe(401, body);

        Assert.DoesNotContain("sk-abcdef123456", message);
        Assert.Contains("***", message);
    }

    [Fact]
    public void NonJsonErrorPage_FallsBackToHintOnly()
    {
        string message = AiServiceErrorMessage.Describe(502, "<html>bad gateway</html>");

        Assert.Contains("服务端暂时不可用", message);
        Assert.DoesNotContain("html", message);
    }

    [Fact]
    public void OverlongDetail_IsTruncated()
    {
        string body = "{\"error\":{\"message\":\"" + new string('测', 400) + "\"}}";

        string message = AiServiceErrorMessage.Describe(400, body);

        Assert.EndsWith("…）。", message);
        Assert.True(message.Length < 260);
    }
}
