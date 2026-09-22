namespace LOL_GameAssistant.Domain.GameData;

/// <summary>与展示技术无关的游戏静态资源内容。</summary>
public sealed record GameAsset(byte[] Content)
{
    public bool IsEmpty => Content.Length == 0;
}