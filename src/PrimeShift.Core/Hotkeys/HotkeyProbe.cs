using System.Runtime.InteropServices;

namespace PrimeShift.Core.Hotkeys;

public enum HotkeyStatus
{
    Available,
    /// <summary>Registered by another program, or reserved by Windows (e.g. most Win+ shortcuts).</summary>
    InUse,
    Rejected,
}

/// <summary>Win32 hotkey registration shared by the tray agent and the settings window.</summary>
public static partial class HotkeyProbe
{
    public const uint MOD_NOREPEAT = 0x4000;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private const int ProbeId = 0xBFFE;

    /// <summary>
    /// Check whether a hotkey is free by briefly registering it. Windows offers no way to find out
    /// which program owns a taken hotkey, and keys grabbed through low-level keyboard hooks are
    /// invisible to this check.
    /// </summary>
    public static HotkeyStatus Probe(Hotkey hotkey)
    {
        if (RegisterHotKey(0, ProbeId, (uint)hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
        {
            UnregisterHotKey(0, ProbeId);
            return HotkeyStatus.Available;
        }
        return Marshal.GetLastPInvokeError() == ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotkeyStatus.InUse
            : HotkeyStatus.Rejected;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);
}
