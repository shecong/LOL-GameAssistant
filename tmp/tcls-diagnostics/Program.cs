using LOL_GameAssistant.Infrastructure.Settings;
var config = new LegacyApplicationSettingsStore().Load();
Console.WriteLine($"Effective auto login: {config.TclsAutoLoginEnabled}; account selected: {!string.IsNullOrEmpty(config.TclsSelectedAccountId)}; auto launch: {config.AutoLaunchGameClient}");
