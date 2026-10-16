using System.Reflection;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UiLanguageConcurrencyTests
{
    [Fact]
    public void IdleOnlyTranslatesControlsOwnedByItsThread() => MatchListScrollingTests.OnUiThread(() =>
    {
        bool english = UiLanguage.IsEnglish;
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
        finally { UiLanguage.SetMode(english ? "en-US" : "zh-CN"); }
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
