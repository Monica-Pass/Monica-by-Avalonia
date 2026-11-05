namespace Monica.UiTests;

/// <summary>
/// The tray, global hotkeys, auto-type and the loopback browser bridge only have Windows adapters.
/// Elsewhere the app deliberately registers capability-only services that report "platform limited",
/// so tests that drive those adapters skip instead of failing for the wrong reason.
/// </summary>
internal static class DesktopIntegrationPlatform
{
    public static void RequireWindows() =>
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The tray, global hotkey, auto-type and browser bridge adapters only exist on Windows.");
}
