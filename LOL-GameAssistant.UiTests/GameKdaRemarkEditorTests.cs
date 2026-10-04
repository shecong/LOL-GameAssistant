using System.Reflection;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class GameKdaRemarkEditorTests
{
    [Fact]
    public void EditorShowsFourCategoriesAndSavesEachTemplate() => MatchListScrollingTests.OnUiThread(() =>
    {
        Dictionary<string, string>? saved = null;
        var type = typeof(QuickShoutForm).Assembly.GetType("LOL_GameAssistant.BaseViewForm.GameKdaRemarkEditor")!;
        var initial = new Dictionary<string, string> { ["Lower"] = "{name} 第一条\n第二条 {kda}" };
        Action<Dictionary<string, string>> save = value => saved = value;
        using var editor = (Form)Activator.CreateInstance(type, [initial, save])!;
        editor.StartPosition = FormStartPosition.Manual;
        editor.Location = new Point(-20000, -20000);
        UiTheme.SetMode("Dark");
        UiTheme.Apply(editor);
        editor.Show();
        System.Windows.Forms.Application.DoEvents();
        var inputs = (Dictionary<string, AntdUI.Input>)type.GetField("_inputs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Assert.Equal(4, inputs.Count);
        Assert.Equal(initial["Lower"], inputs["Lower"].Text);
        foreach (var input in inputs.Values)
        {
            Assert.True(input.Visible);
            Assert.True(input.Height >= 70);
            Assert.True(input.Parent!.ClientRectangle.Contains(input.Bounds));
            Assert.Equal(UiTheme.Palette.TextPrimary, input.ForeColor);
        }
        using (var bitmap = new Bitmap(editor.Width, editor.Height))
        {
            editor.DrawToBitmap(bitmap, editor.ClientRectangle);
            bitmap.Save(Path.Combine(Path.GetTempPath(), "kda-remark-editor.png"));
        }
        inputs["Human"].Text = "自定义一\n自定义二";
        var apply = editor.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<AntdUI.Button>()
            .Single(button => button.Text == "保存文案并应用");
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(apply, [EventArgs.Empty]);
        Assert.NotNull(saved);
        Assert.Equal(initial["Lower"], saved["Lower"]);
        Assert.Equal("自定义一\n自定义二", saved["Human"]);
        Assert.Equal("", saved["Upper"]);
    });
}
