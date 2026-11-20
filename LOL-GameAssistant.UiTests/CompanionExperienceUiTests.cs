using System.Reflection;
using System.Text.Json;
using LOL.Lounge;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CompanionExperienceUiTests
{
    private static void Capture(Control control, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("EXPERIENCE_PREVIEWS"); if (folder == null) return;
        Directory.CreateDirectory(folder); using var image = new Bitmap(control.Width, control.Height); control.DrawToBitmap(image, control.ClientRectangle); image.Save(Path.Combine(folder, name + ".png"));
    }

    [Fact]
    public void ApplyingPresetSavesOnceAndPreservesUnsavedAiConnection() => MatchListScrollingTests.OnUiThread(() =>
    {
        int saves = 0; var settings = new AssistantSettings { Ai = new() { EncryptedApiKey = "secret" } };
        var store = Service<IApplicationSettingsStore>((m, a) =>
        {
            if (m.Name == "GetStoragePath") return "test-settings.json";
            if (m.Name == "Load") return JsonSerializer.Deserialize<AssistantSettings>(JsonSerializer.Serialize(settings));
            if (m.Name == "Save") { settings = (AssistantSettings)a[0]!; saves++; } return null;
        });
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        Field<AntdUI.Input>(form, "_apiKey").Text = "unsaved-key"; Field<AntdUI.Select>(form, "_model").Text = "unsaved-model";
        form.ApplyPreset(AssistantPreset.Aram);
        Assert.Equal(1, saves); Assert.True(settings.MayhemOverlayEnabled); Assert.Equal("secret", settings.Ai.EncryptedApiKey);
        Assert.Equal("unsaved-key", Field<AntdUI.Input>(form, "_apiKey").Text); Assert.Equal("unsaved-model", Field<AntdUI.Select>(form, "_model").Text);
    });

    [Fact]
    public void RecruitmentFiltersExcludeClosedAndExpiredAndRespectStyle() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
        var receive = typeof(LoungeClient).GetMethod("ReceiveRecruitment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 1; i <= 4; i++) receive.Invoke(client, [new LoungeRecruitmentState(i, "owner" + i, "队友" + i,
            new("艾欧尼亚", 420, "不限段位", "辅助", 2, RecruitmentPreferences.Compose(i == 1 ? "认真上分" : "轻松娱乐", false, "", "")), [], i == 3,
            i == 4 ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(20))]);
        using var view = new LoungeForm(client); using var dialog = view.CreateRecruitmentListDialog(); dialog.Show();
        var table = (AntdUI.Table)dialog.Controls.Find("recruitmentResults", true).Single();
        Assert.Equal(2, ((Array)table.DataSource!).Length);
        var filter = (AntdUI.Select)dialog.Controls.Find("recruitmentStyle", true).Single(); filter.SelectedValue = "认真上分";
        Assert.Single(((Array)table.DataSource!).Cast<object>());
        Assert.Contains("队友1", ((Array)table.DataSource!).GetValue(0)!.ToString());
        Capture(dialog, "recruitment-filter");
    });
}
