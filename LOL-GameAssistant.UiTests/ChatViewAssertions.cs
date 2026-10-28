namespace LOL_GameAssistant.UiTests;

internal static class ChatViewAssertions
{
    internal static string Text(AntdUI.Chat.ChatList chat) => string.Join("\n", chat.Items.Select(item => item switch
    {
        AntdUI.Chat.TextChatItem text => text.Name + "\n" + text.Text,
        AntdUI.Chat.TipChatItem tip => tip.Text,
        AntdUI.Chat.ImageChatItem image => image.Name,
        _ => ""
    }));

    internal static int MessageY(AntdUI.Chat.ChatList chat, long id) =>
        chat.Items.Single(item => item.Tag is LOL.Lounge.LoungeMessage message && message.Id == id).rect.Top - chat.ScrollBar.ValueY;
}
