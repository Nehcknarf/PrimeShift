namespace PrimeShift.Agent;

/// <summary>
/// Messages the settings window (a separate process) sends to the running tray agent through
/// its hidden window.
/// </summary>
internal static class AgentChannel
{
    public const string WindowClass = "PrimeShift.TrayAgent";

    /// <summary>Unregister all hotkeys until the process whose id is in wParam exits.</summary>
    public const uint Suspend = Win32.WM_APP + 1;

    /// <summary>Re-read config.json (profiles, hotkeys, options).</summary>
    public const uint Reload = Win32.WM_APP + 2;

    /// <summary>Internal: the suspending process exited; re-register hotkeys.</summary>
    public const uint Resume = Win32.WM_APP + 3;

    /// <summary>Open the settings window (sent by a second launch of the exe).</summary>
    public const uint OpenSettings = Win32.WM_APP + 4;

    public static nint FindAgent() => Win32.FindWindowW(WindowClass, null);

    public static bool IsAgentRunning => FindAgent() != 0;

    /// <summary>
    /// Ask the agent to release its hotkeys while settings are open, so they can be re-recorded
    /// and checked for conflicts. Synchronous so the hotkeys are free when this returns.
    /// </summary>
    public static void SuspendHotkeys()
    {
        var hwnd = FindAgent();
        if (hwnd != 0)
        {
            Win32.SendMessageTimeoutW(hwnd, Suspend, Environment.ProcessId, 0, Win32.SMTO_ABORTIFHUNG, 2000, out _);
        }
    }

    public static void NotifyConfigChanged()
    {
        var hwnd = FindAgent();
        if (hwnd != 0)
        {
            Win32.PostMessageW(hwnd, Reload, 0, 0);
        }
    }

    public static bool RequestOpenSettings()
    {
        var hwnd = FindAgent();
        return hwnd != 0 && Win32.PostMessageW(hwnd, OpenSettings, 0, 0);
    }
}
