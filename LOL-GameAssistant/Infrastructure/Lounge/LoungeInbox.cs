using LOL.Lounge;

namespace LOL_GameAssistant.Infrastructure.Lounge;

// Called under the LoungeClient gate; replay never creates duplicate unread entries.
internal sealed class LoungeInbox
{
    private readonly HashSet<long> _unread = [];
    public int Count => _unread.Count;
    public int Mentions { get; private set; }
    public bool Reading { get; set; }
    public bool Accept(LoungeMessage message, string ownPlayer, string ownSession, bool blocked, bool notify)
    {
        if (!notify || blocked || message.Kind == "deleted" || message.SenderId == ownSession || Reading) return false;
        if (!_unread.Add(message.Id)) return false;
        while (_unread.Count > 300) _unread.Remove(_unread.Min());
        bool mentioned = LoungeContent.Mentions(message).Contains(ownPlayer);
        if (mentioned) Mentions = Math.Min(300, Mentions + 1);
        return mentioned;
    }
    public void Clear() { _unread.Clear(); Mentions = 0; }
}
