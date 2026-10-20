using System.Reflection;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UiLanguageConcurrencyTests
{
    [Fact]
    public void TraditionalChinesePreservesDynamicValuesAndSupportsLanguageRoundTrips() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiLanguage.Mode;
        try
        {
            UiLanguage.SetMode("zh-CN");
            using var form = new Form();
            using var label = new Label { Text = "设置" };
            using var input = new TextBox { Text = "用户输入的简体文字", PlaceholderText = "查询玩家" };
            using var combo = new ComboBox();
            combo.Items.AddRange(["查询玩家", "设置"]);
            combo.SelectedIndex = 1;
            form.Controls.AddRange([label, input, combo]);
            form.Show();
            Attach(form);

            UiLanguage.SetMode("zh-TW");
            Assert.True(UiLanguage.IsTraditionalChinese);
            Assert.False(UiLanguage.IsEnglish);
            Assert.Equal("設置", label.Text);
            Assert.Equal("查詢玩家", input.PlaceholderText);
            Assert.Equal("用户输入的简体文字", input.Text);
            Assert.Equal("設置", combo.SelectedItem);
            Assert.Equal("正在下載更新：42% · v1.2.3", UiLanguage.T("正在下载更新：42% · v1.2.3"));
            Assert.Equal("https://example.com/a?b=42", UiLanguage.T("https://example.com/a?b=42"));
            Assert.Equal("", UiLanguage.T(null));

            // Controls created with already localized text must retain a recoverable source.
            using var added = new Label { Text = UiLanguage.T("查询玩家") };
            form.Controls.Add(added);
            UiLanguage.SetMode("en-US");
            Assert.Equal("Settings", label.Text);
            Assert.Equal("Search Player", added.Text);
            Assert.Equal("Search Player", input.PlaceholderText);
            Assert.Equal("Settings", combo.SelectedItem);
            UiLanguage.SetMode("zh-CN");
            Assert.Equal("设置", label.Text);
            Assert.Equal("查询玩家", added.Text);
            Assert.Equal("查询玩家", input.PlaceholderText);
            Assert.Equal("设置", combo.SelectedItem);
            Assert.Equal(1, combo.SelectedIndex);
            UiLanguage.SetMode("zh-TW");
            label.Text = "下载回放";
            Assert.Equal("下載回放", label.Text);
            UiLanguage.SetMode("zh-CN");
            Assert.Equal("下载回放", label.Text);
        }
        finally { UiLanguage.SetMode(previous); }
    });

    [Fact]
    public void TraditionalChineseSurvivesSettingsSerializationAndNormalization()
    {
        var settings = new AssistantSettings { LanguageMode = "zh-TW" };
        settings.Normalize();
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(settings))!;
        restored.Normalize();
        Assert.Equal("zh-TW", restored.LanguageMode);
        var config = new SettingConfig { LanguageMode = restored.LanguageMode };
        config.Normalize();
        Assert.Equal("zh-TW", config.LanguageMode);
    }

    [Fact]
    public void IdleOnlyTranslatesControlsOwnedByItsThread() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiLanguage.Mode;
        try
        {
            UiLanguage.SetMode("en-US");
            using var control = new LocalizedControl();
            Attach(control);
            control.PlaceholderText = "查询玩家";
            control.QueueTranslation();
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { Idle(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(error);
            Assert.Equal("查询玩家", control.PlaceholderText);
            Idle();
            Assert.Equal("Search Player", control.PlaceholderText);
        }
        finally { UiLanguage.SetMode(previous); }
    });

    [Fact]
    public void ConcurrentInvalidationAndDisposalDoNotCorruptIdleQueue() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var control = new LocalizedControl();
        Attach(control);
        using var start = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            start.Wait();
            for (int index = 0; index < 10000; index++) control.QueueTranslation();
        });
        start.Set();
        for (int index = 0; index < 1000; index++)
        {
            using var transient = new LocalizedControl();
            Attach(transient);
            transient.QueueTranslation();
            transient.Dispose();
            Idle();
        }
        Assert.True(producer.Wait(TimeSpan.FromSeconds(10)));
        Idle();
    });

    private static void Attach(Control control) => typeof(UiLanguage)
        .GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [control, false]);

    private static void Idle() => typeof(UiLanguage)
        .GetMethod("ProcessIdle", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [null, EventArgs.Empty]);

    private sealed class LocalizedControl : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string PlaceholderText { get; set; } = "";
        internal void QueueTranslation() => OnInvalidated(new InvalidateEventArgs(ClientRectangle));
    }
}
