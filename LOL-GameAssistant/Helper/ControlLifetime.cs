namespace LOL_GameAssistant.Helper;

/// <summary>清空动态区域时同时释放子控件持有的计时器、提示框和图像。</summary>
public static class ControlLifetime
{
    public static void ClearAndDispose(Control parent)
    {
        foreach (Control child in parent.Controls.Cast<Control>().ToArray())
        {
            parent.Controls.Remove(child);
            child.Dispose();
        }
    }
}