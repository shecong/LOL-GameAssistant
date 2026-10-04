using Xunit;

namespace LOL_GameAssistant.UiTests;

// Theme and non-client pixel checks use process-global colors and visible desktop windows.
[CollectionDefinition("Window theme", DisableParallelization = true)]
public sealed class WindowThemeCollection;
