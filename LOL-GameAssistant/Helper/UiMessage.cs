namespace LOL_GameAssistant.Helper;

/// <summary>Translate notifications at display time so messages also follow the active language.</summary>
internal static class UiMessage
{
    /// <summary>以当前语言显示成功提示。</summary>
    public static void success(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.success(form, UiLanguage.T(message), font, duration);

    /// <summary>以当前语言显示信息提示。</summary>
    public static void info(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.info(form, UiLanguage.T(message), font, duration);

    /// <summary>以当前语言显示警告提示。</summary>
    public static void warn(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.warn(form, UiLanguage.T(message), font, duration);

    /// <summary>以当前语言显示错误提示。</summary>
    public static void error(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.error(form, UiLanguage.T(message), font, duration);
}
