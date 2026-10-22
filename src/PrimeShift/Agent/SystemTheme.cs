using Microsoft.Win32;

namespace PrimeShift.Agent;

internal static unsafe class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // uxtheme.dll ordinals (Windows 10 1903+): undocumented but the de-facto way for Win32 apps,
    // including many built into Windows, to get dark context menus.
    private const int SetPreferredAppModeOrdinal = 135;
    private const int FlushMenuThemesOrdinal = 136;
    private const int ForceDark = 2;
    private const int ForceLight = 3;

    /// <summary>The taskbar and tray follow the system theme, not the app theme.</summary>
    public static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    /// <summary>Make this process's popup menus match the taskbar theme.</summary>
    public static void ApplyToMenus(bool light)
    {
        // On older Windows the ordinals are missing and menus simply stay light.
        var uxtheme = Win32.LoadLibraryW("uxtheme.dll");
        if (uxtheme == 0)
        {
            return;
        }
        var setMode = (delegate* unmanaged<int, int>)Win32.GetProcAddress(uxtheme, SetPreferredAppModeOrdinal);
        var flush = (delegate* unmanaged<void>)Win32.GetProcAddress(uxtheme, FlushMenuThemesOrdinal);
        if (setMode != null)
        {
            setMode(light ? ForceLight : ForceDark);
        }
        if (flush != null)
        {
            flush();
        }
    }
}
