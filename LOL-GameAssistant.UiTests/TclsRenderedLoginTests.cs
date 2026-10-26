using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class TclsRenderedLoginTests
{
    [Fact]
    public void PackagedOcr_RecognizesRenderedFormWithoutAccessibleControls()
    {
        using var image = new Bitmap(320, 768, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(6, 15, 31));
        using var labelFont = new Font("Microsoft YaHei UI", 13, GraphicsUnit.Pixel);
        using var buttonFont = new Font("Microsoft YaHei UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var gold = new SolidBrush(Color.FromArgb(195, 177, 112));
        graphics.DrawString("QQ账号", labelFont, gold, 88, 280);
        graphics.DrawString("QQ密码", labelFont, gold, 88, 344);
        graphics.DrawString("进入游戏", buttonFont, Brushes.LightCyan, 128, 560);
        using var reader = new TclsRenderedLoginAutomation();
        var plan = TclsRenderedLoginAutomation.Plan(reader.ReadLabels(image), image.Size);
        Assert.NotNull(plan);
        Assert.NotNull(plan.Password);
        Assert.NotNull(plan.Submit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RenderedQqForm_UsesLabelsAtCurrentDpi(double scale)
    {
        var labels = Form(scale);
        var plan = TclsRenderedLoginAutomation.Plan(labels, new Size((int)(320 * scale), (int)(768 * scale)));
        Assert.NotNull(plan);
        Assert.Null(plan.QqLogin);
        Assert.Equal(new Point((int)(188 * scale), (int)(312 * scale)), plan.Account);
        Assert.Equal(new Point((int)(188 * scale), (int)(376 * scale)), plan.Password);
        Assert.Equal(new Point((int)(188 * scale), (int)(574 * scale)), plan.Submit);
    }

    [Fact]
    public void ProviderChooser_EntersQqOnlyWhenBothProvidersArePresent()
    {
        var qq = new TclsLoginText("QQ 登录", new Rectangle(140, 312, 70, 20));
        var wechat = new TclsLoginText("微信登录", new Rectangle(140, 384, 70, 20));
        Assert.NotNull(TclsRenderedLoginAutomation.Plan([qq, wechat], new Size(320, 768))?.QqLogin);
        Assert.Null(TclsRenderedLoginAutomation.Plan([qq], new Size(320, 768)));
    }

    [Fact]
    public void IncompleteOrAmbiguousForm_DoesNotReceiveCredentials()
    {
        var labels = Form(1);
        Assert.Null(TclsRenderedLoginAutomation.Plan(labels.Take(2).ToArray(), new Size(320, 768)));
        Assert.Null(TclsRenderedLoginAutomation.Plan([.. labels, labels[1]], new Size(320, 768)));
        Assert.Null(TclsRenderedLoginAutomation.Plan([labels[0], labels[1] with { Confidence = .6f }, labels[2]], new Size(320, 768)));
        Assert.Null(TclsRenderedLoginAutomation.Plan([labels[0], labels[1] with { Bounds = new Rectangle(0, 10, 80, 16) }, labels[2]], new Size(320, 768)));
    }

    private static TclsLoginText[] Form(double scale)
    {
        Rectangle Box(int x, int y, int width, int height) => new((int)(x * scale), (int)(y * scale), (int)(width * scale), (int)(height * scale));
        return [new("QQ账号", Box(88, 280, 56, 16)), new("QQ密码", Box(88, 344, 56, 16)), new("进入游戏", Box(128, 560, 120, 28))];
    }
}
