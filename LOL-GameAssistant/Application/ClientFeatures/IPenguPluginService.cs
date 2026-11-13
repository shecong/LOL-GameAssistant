namespace LOL_GameAssistant.Application.ClientFeatures;

public sealed record PenguPluginStatus(bool Installed, bool Enabled, bool Loaded, string Message);

/// <summary>管理用户选定的 Pengu 插件目录，不安装或激活 Loader。</summary>
public interface IPenguPluginService
{
    string PluginsDirectory { get; }
    void SelectDirectory(string directory);
    PenguPluginStatus Inspect();
    PenguPluginStatus Check();
    void Install();
    void SetEnabled(bool enabled);
    void Uninstall();
}
