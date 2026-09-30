namespace LOL_GameAssistant.Helper;

/// <summary>Translate notifications at display time so messages also follow the active language.</summary>
internal static class UiMessage
{
    public static void success(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.success(form, UiLanguage.T(message), font, duration);

    public static void info(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.info(form, UiLanguage.T(message), font, duration);

    public static void warn(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.warn(form, UiLanguage.T(message), font, duration);

    public static void error(Form form, string message, Font? font = null, int? duration = null) =>
        AntdUI.Message.error(form, UiLanguage.T(message), font, duration);
}
