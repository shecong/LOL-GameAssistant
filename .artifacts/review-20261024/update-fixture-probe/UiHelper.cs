using System.Runtime.ExceptionServices;
namespace LOL_GameAssistant.UiTests;
internal static class MatchListScrollingTests {
internal static void OnUiThread(Action action) {
Exception? error = null;
var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
}}
