namespace LOL_GameAssistant.Domain.GameData;

/// <summary>与展示技术无关的游戏静态资源内容。</summary>
public sealed record GameAsset(byte[] Content)
{
    /// <summary>判断资源是否缺少可使用的二进制内容。</summary>
    public bool IsEmpty => Content.Length == 0;
}