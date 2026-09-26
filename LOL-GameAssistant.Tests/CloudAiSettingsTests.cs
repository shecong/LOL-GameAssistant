using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CloudAiSettingsTests
{
    [Theory]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://127.0.0.1:11434/v1", true)]
    [InlineData("http://api.example.com/v1", false)]
    [InlineData("http://localhost.evil.example/v1", false)]
    [InlineData("https://user:pass@api.example.com/v1", false)]
    [InlineData("https://api.example.com/v1?token=secret", false)]
    public void TryGetSafeBaseUri_EnforcesKeyTransportPolicy(string address, bool expected)
    {
        var settings = new CloudAiSettings { Provider = AiProvider.CustomOpenAiCompatible, BaseUrl = address };

        bool accepted = settings.TryGetSafeBaseUri(out _, out _);

        Assert.Equal(expected, accepted);
    }
}