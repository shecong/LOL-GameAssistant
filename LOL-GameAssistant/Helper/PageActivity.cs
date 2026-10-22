namespace LOL_GameAssistant.Helper;

internal static class PageActivity
{
    internal static bool IsActive(Control control)
    {
        if (!control.Visible || control.IsDisposed) return false;
        for (Control? node = control.Parent; node != null; node = node.Parent)
            if (node is AntdUI.TabPage page && page.Parent is AntdUI.Tabs tabs &&
                (tabs.SelectedIndex < 0 || tabs.SelectedIndex >= tabs.Pages.Count || tabs.Pages[tabs.SelectedIndex] != page)) return false;
        return true;
    }
}
