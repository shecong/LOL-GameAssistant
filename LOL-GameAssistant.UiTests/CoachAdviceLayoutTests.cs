using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CoachAdviceLayoutTests
{
    [Fact]
    public void OverlayDeduplicatesDirectionAndHidesWhenAdviceExpires() => MatchListScrollingTests.OnUiThread(() =>
    {
        int published = 0, hidden = 0;
        var bridge = Proxy<IGameOverlayBridge>((method, _) => { if (method.Name == "PublishPanel") published++; if (method.Name == "HidePanel") hidden++; return null; });
        using var form = new CoachForm(Proxy<IAiCoachingService>((_, _) => null),
            Proxy<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Proxy<IApplicationSettingsStore>((_, _) => new AssistantSettings { Ai = new() { RecommendationOverlayEnabled = true } }),
            Proxy<IOpggBuildApplyService>((_, _) => null), overlayBridge: bridge);
        var card = new CoachRecommendation("first", "装备", RecommendationPriority.Important, "提高生存能力", "可考虑生存方向。", "当前装备", RecommendationSource.CloudAi, DateTimeOffset.UtcNow.AddMinutes(2)) { ActionKey = "build:survival" };
        var state = new RecommendationState(RecommendationStatus.AiReady, new AiGameContext { Phase = "InProgress", MyChampionId = 1 }, [card], "", DateTimeOffset.UtcNow, null);
        void Render(RecommendationState value) => typeof(CoachForm).GetMethod("RenderState", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [value]);
        Render(state);
        Assert.Equal(1, published);
        Render(state with { Recommendations = [card with { Id = "rephrased", Title = "补充生存属性", Body = "需要容错时可补生存方向。" }] });
        Assert.Equal(1, published);
        Assert.True(hidden > 0);
        Render(state with { Recommendations = [card with { Id = "info", Priority = RecommendationPriority.Info, ActionKey = "lane:farm" }] });
        Assert.Equal(1, published);
        Render(state with { SessionVersion = 1 });
        Assert.Equal(2, published);
        int beforeExpiry = hidden;
        Render(state with { SessionVersion = 1, Recommendations = [] });
        Assert.True(hidden > beforeExpiry);
    });

    [Theory]
    [InlineData(760, "Dark")]
    [InlineData(1280, "Light")]
    [InlineData(1280, "Dark")]
    public void CardsWrapAndPreserveSeparateBuildStatus(int width, string mode) => MatchListScrollingTests.OnUiThread(() =>
    {
        string original = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(mode);
            var now = DateTimeOffset.UtcNow;
            var context = new AiGameContext { Phase = "InProgress", MyChampionId = 1, MyChampion = "安妮", MyRole = "MID", Mode = "峡谷对局",
                CurrentItems = ["鞋子", "多兰之戒"], CurrentGold = 1250, GameTimeSeconds = 952, HasLiveClientData = true, EnemyChampions = ["盖伦", "拉克丝"] };
            var card = new CoachRecommendation("preview", "团战", RecommendationPriority.Attention, "先避开控制，再配合队友反打",
                "当前输出路线已经起步；如果需要提高团战容错，可以考虑补充适合当前职责的生存属性。", "已购：鞋子、多兰之戒；敌方：盖伦、拉克丝", RecommendationSource.CloudAi, now.AddMinutes(2))
            { GeneratedAt = now, Conditions = "看到盖伦交出加速且队友能跟进时再出手", ActionKey = "teamfight:position",
                Steps = ["安妮先站在队友侧后方，留住控制应对盖伦近身。", "等拉克丝关键控制落空且队友能跟进，再用控制反打；无法跟进时不追击。"],
                Why = "盖伦近身沉默会限制安妮的技能连招；先避开拉克丝的控制能保留反打机会。",
                ExpectedOutcome = "保住关键控制并争取一轮安全输出。", Fallback = "队友距离过远时先退回己方前排后侧，停止追击。",
                InvalidWhen = "自身控制已交、队友离开或撤退路线被封时停止。" };
            var state = new RecommendationState(RecommendationStatus.AiReady, context, [card, card with { Id = "secondary", Priority = RecommendationPriority.Info, Category = "团战", Title = "团战中优先保持输出位置", Body = "根据己方阵容承担输出职责，进场前保留足够的安全距离。", ActionKey = "teamfight:position" }],
                "建议已更新；展开卡片可查看适用条件和分析依据。", now, null) { AnalyzedAt = now, RequestCount = 4, RequestBudget = 30 };
            string? confirmedMatchup = null;
            using var form = new CoachForm(Proxy<IAiCoachingService>((_, _) => Task.FromResult(context)),
                Proxy<IRecommendationCoordinator>((method, args) =>
                {
                    if (method.Name == "ConfirmMatchup") confirmedMatchup = (string?)args[0];
                    return method.Name == "get_Current" ? state : null;
                }),
                Proxy<IApplicationSettingsStore>((_, _) => new AssistantSettings { OpggBuildAssistantEnabled = true }),
                Proxy<IOpggBuildApplyService>((_, _) => null));
            using var host = new Form { ClientSize = new Size(width, 760), Opacity = 0, ShowInTaskbar = false };
            host.Controls.Add(form); host.Show(); UiTheme.Apply(form);
            System.Windows.Forms.Application.DoEvents(); form.PerformLayout(); System.Windows.Forms.Application.DoEvents();
            var matchup = Descendants(form).OfType<AntdUI.Select>().Single(c => c.Name == "coachMatchup");
            Assert.True(matchup.Height >= 32);
            matchup.SelectedIndex = 1;
            Assert.Equal("盖伦", confirmedMatchup);
            matchup.SelectedIndex = 0;
            Assert.Null(confirmedMatchup);
            var cards = Descendants(form).OfType<TableLayoutPanel>().Where(c => c.Name.Contains("Card")).ToArray();
            Assert.Equal(2, cards.Length);
            Assert.False(cards[1].Visible);
            Descendants(form).OfType<AntdUI.Button>().Single(b => b.Name == "moreAdvice").PerformClick();
            System.Windows.Forms.Application.DoEvents();
            Assert.True(cards[1].Visible);
            Assert.All(cards, c => Assert.InRange(c.Right, 1, c.Parent!.ClientSize.Width));
            Assert.True(cards[1].Top >= cards[0].Bottom);
            var expand = Descendants(cards[0]).OfType<AntdUI.Button>().Single(b => b.Text == "查看分析依据");
            expand.PerformClick(); System.Windows.Forms.Application.DoEvents();
            Assert.True(Descendants(cards[0]).Single(c => c.Name == "coachCardEvidence").Visible);
            foreach (var label in cards.SelectMany(Descendants).OfType<AntdUI.Label>())
            {
                Assert.True(label.TextMultiLine);
                Assert.True(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height);
            }
            var buildStatus = (Control)typeof(CoachForm).GetField("_buildStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            Assert.NotSame(buildStatus, typeof(CoachForm).GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form));
            string? directory = Environment.GetEnvironmentVariable("COACH_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, form.ClientRectangle);
                bitmap.Save(Path.Combine(directory, $"coach-{width}-{mode}.png"));
            }
        }
        finally { UiTheme.SetMode(original); }
    });
    private static IEnumerable<Control> Descendants(Control parent) => new[] { parent }.Concat(parent.Controls.Cast<Control>().SelectMany(Descendants));
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler; return proxy;
    }
}
