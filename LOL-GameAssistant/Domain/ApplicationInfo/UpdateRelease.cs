namespace LOL_GameAssistant.Domain.ApplicationInfo;

/// <summary>可展示的软件发布信息。</summary>
public sealed record UpdateRelease(Version Version, string TagName, string ReleaseUrl, string ReleaseNotes);
