using PrimeShift.Agent;

namespace PrimeShift.Settings;

internal static class SettingsHost
{
    public const string WindowTitle = "PrimeShift 设置";

    public static int Run()
    {
        using var mutex = new Mutex(true, @"Local\PrimeShift.Settings", out var createdNew);
        if (!createdNew)
        {
            BringExistingToFront();
            return 0;
        }
        // Free the agent's hotkeys so they can be re-recorded and probed for conflicts;
        // the agent takes them back as soon as this process exits.
        AgentChannel.SuspendHotkeys();
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static void BringExistingToFront()
    {
        var hwnd = Win32.FindWindowW(null, WindowTitle);
        if (hwnd == 0)
        {
            return;
        }
        if (Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }
        Win32.SetForegroundWindow(hwnd);
    }
}
