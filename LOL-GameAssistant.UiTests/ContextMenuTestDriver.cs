using System.Reflection;
using Xunit;

namespace LOL_GameAssistant.UiTests;

internal static class ContextMenuTestDriver
{
    internal static void Click(Control owner, string text)
    {
        typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, [new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0)]);
        System.Windows.Forms.Application.DoEvents();
        var menus = System.Windows.Forms.Application.OpenForms.Cast<Form>()
            .SelectMany(form => form.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(AntdUI.ContextMenuStrip.Config))
                .Select(field => (Form: form, Config: (AntdUI.ContextMenuStrip.Config)field.GetValue(form)!)))
            .Where(menu => menu.Config.Control == owner).ToArray();
        var menu = Assert.Single(menus);
        var item = Assert.Single(menu.Config.Items.OfType<AntdUI.ContextMenuStripItem>(), item => item.Text == text);
        Assert.True(item.Enabled);
        menu.Form.Close();
        menu.Config.Call(item);
        System.Windows.Forms.Application.DoEvents();
    }
}
