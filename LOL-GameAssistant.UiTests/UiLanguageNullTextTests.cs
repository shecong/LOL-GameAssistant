using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UiLanguageNullTextTests
{
    private sealed class NullTextControl : Control
    {
        [AllowNull]
        public override string Text { get => null!; set { } }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    public void RefreshAcceptsNullControlText(string language) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiLanguage.Mode;
        using var host = new Form();
        try
        {
            UiLanguage.SetMode(language);
            host.Controls.Add(new NullTextControl()); host.Controls.Add(new Label { Text = "设置" }); host.Show();
            Assert.Null(Record.Exception(UiLanguage.Refresh));
            Assert.Null(Record.Exception(UiLanguage.Refresh));
        }
        finally { host.Dispose(); UiLanguage.SetMode(previous); }
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    public void DeferredLoungeSurvivesActualLanguageRefresh(string language) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiLanguage.Mode;
        using var client = new LoungeClient(new LoungeLcuReader(Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
        using var host = new Form { ClientSize = new(1100, 760) };
        try
        {
            UiLanguage.SetMode(language);
            using var deferred = new DeferredPageHost(() => new LoungeForm(client));
            host.Controls.Add(deferred); host.Show();
            typeof(DeferredPageHost).GetMethod("LoadPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(deferred, null);
            Assert.True(Field<bool>(deferred, "_loaded"), Field<Label>(deferred, "_status").Text);
            Assert.Single(deferred.Controls.OfType<LoungeForm>());
            UiLanguage.Refresh();
            Assert.False(deferred.Controls.OfType<LoungeForm>().Single().IsDisposed);
        }
        finally { host.Dispose(); UiLanguage.SetMode(previous); }
    });
}
