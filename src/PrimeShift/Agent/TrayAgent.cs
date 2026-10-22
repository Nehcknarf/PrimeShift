using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PrimeShift.Core.Config;
using PrimeShift.Core.Display;
using PrimeShift.Core.Hotkeys;
using static PrimeShift.Agent.Win32;

namespace PrimeShift.Agent;

/// <summary>
/// The resident part of PrimeShift: a hidden window that owns the tray icon and the global
/// hotkeys. It deliberately uses plain Win32 and never touches WPF, so WPF is only loaded by
/// the separate settings process and the idle footprint stays small.
/// </summary>
internal sealed unsafe class TrayAgent
{
    private const uint TrayCallback = WM_APP + 100;
    private const uint TrayIconId = 1;
    private const int MenuSettings = 1;
    private const int MenuAutoStart = 2;
    private const int MenuExit = 3;
    private const int MenuProfileBase = 1000;

    private static TrayAgent? _instance;

    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly Dictionary<int, LayoutProfile> _hotkeys = [];
    private AppConfig _config = new();
    private nint _hwnd;
    private nint _icon;
    private bool _suspended;
    private Process? _suspender;

    public static int Run()
    {
        using var mutex = new Mutex(true, @"Local\PrimeShift.TrayAgent", out var createdNew);
        if (!createdNew)
        {
            // Launching the exe again while it runs opens the settings window instead.
            if (!AgentChannel.RequestOpenSettings())
            {
                SettingsLauncher.Start();
            }
            return 0;
        }
        _instance = new TrayAgent();
        return _instance.MessageLoop();
    }

    private int MessageLoop()
    {
        CreateHiddenWindow();
        ApplyTheme();
        AddTrayIcon();
        LoadConfig();
        RegisterHotkeys();
        Log.Info("PrimeShift 已启动");
        if (_config.Profiles.Count == 0)
        {
            SettingsLauncher.Start();
        }

        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        UnregisterHotkeys();
        RemoveTrayIcon();
        DestroyIcon(_icon);
        Log.Info("PrimeShift 已退出");
        return 0;
    }

    private void CreateHiddenWindow()
    {
        var instance = GetModuleHandleW(null);
        fixed (char* className = AgentChannel.WindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = className,
            };
            if (RegisterClassExW(&wc) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        // A hidden top-level window rather than a message-only one: only top-level windows
        // receive the TaskbarCreated and WM_SETTINGCHANGE broadcasts.
        _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, AgentChannel.WindowClass, "PrimeShift", WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (_instance is { } agent && agent.Handle(msg, wParam, lParam, out var result))
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            // Exceptions must not cross back into native code.
            Log.Error($"处理窗口消息 0x{msg:X4} 失败", ex);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private bool Handle(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case WM_HOTKEY:
                if (_hotkeys.TryGetValue((int)wParam, out var profile))
                {
                    SwitchTo(profile);
                }
                return true;
            case TrayCallback:
                var trayEvent = (uint)(lParam & 0xFFFF);
                if (trayEvent is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT)
                {
                    ShowMenu();
                }
                return true;
            case AgentChannel.Suspend:
                Suspend((int)wParam);
                result = 1;
                return true;
            case AgentChannel.Reload:
                LoadConfig();
                if (!_suspended)
                {
                    RegisterHotkeys();
                }
                return true;
            case AgentChannel.Resume:
                _suspended = false;
                _suspender?.Dispose();
                _suspender = null;
                LoadConfig();
                RegisterHotkeys();
                return true;
            case AgentChannel.OpenSettings:
                SettingsLauncher.Start();
                return true;
            case WM_SETTINGCHANGE:
                if (lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet")
                {
                    ApplyTheme();
                }
                return false;
            case WM_DESTROY:
                PostQuitMessage(0);
                return true;
            default:
                if (msg == _taskbarCreated)
                {
                    // Explorer restarted (or was not ready at logon): the icon has to be added again.
                    AddTrayIcon();
                    return true;
                }
                return false;
        }
    }

    private void LoadConfig()
    {
        try
        {
            _config = ConfigStore.Load();
        }
        catch (ConfigException ex)
        {
            Log.Error("读取配置失败", ex);
            ShowBalloon("读取配置失败", ex.Message, error: true);
        }
        UpdateTooltip();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var failures = new List<string>();
        for (var i = 0; i < _config.Profiles.Count; i++)
        {
            var profile = _config.Profiles[i];
            if (string.IsNullOrEmpty(profile.Hotkey))
            {
                continue;
            }
            if (!Hotkey.TryParse(profile.Hotkey, out var hotkey, out var error))
            {
                failures.Add($"{profile.Name}：{error}");
                continue;
            }
            var id = i + 1;
            if (HotkeyProbe.RegisterHotKey(_hwnd, id, (uint)hotkey.Value.Modifiers | HotkeyProbe.MOD_NOREPEAT, hotkey.Value.VirtualKey))
            {
                _hotkeys[id] = profile;
            }
            else
            {
                failures.Add($"{profile.Name}（{hotkey.Value.ToDisplayString()}）已被其他程序或系统占用");
            }
        }
        if (failures.Count > 0)
        {
            Log.Error("快捷键注册失败：" + string.Join("；", failures));
            ShowBalloon("部分快捷键未生效", string.Join("\n", failures), error: true);
        }
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _hotkeys.Keys)
        {
            HotkeyProbe.UnregisterHotKey(_hwnd, id);
        }
        _hotkeys.Clear();
    }

    /// <summary>Release the hotkeys while the settings window (process <paramref name="pid"/>) is open.</summary>
    private void Suspend(int pid)
    {
        UnregisterHotkeys();
        _suspended = true;
        _suspender?.Dispose();
        _suspender = null;
        try
        {
            var process = Process.GetProcessById(pid);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => PostMessageW(_hwnd, AgentChannel.Resume, 0, 0);
            _suspender = process;
            if (process.HasExited)
            {
                PostMessageW(_hwnd, AgentChannel.Resume, 0, 0);
            }
        }
        catch (ArgumentException)
        {
            PostMessageW(_hwnd, AgentChannel.Resume, 0, 0);
        }
    }

    private void SwitchTo(LayoutProfile profile)
    {
        try
        {
            LayoutService.Default.Apply(profile);
            Log.Info($"已切换到布局「{profile.Name}」");
            if (_config.NotifyOnSwitch)
            {
                ShowBalloon("已切换显示布局", profile.Name, error: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"切换到布局「{profile.Name}」失败", ex);
            ShowBalloon($"无法切换到「{profile.Name}」", ex.Message, error: true);
        }
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            if (_config.Profiles.Count == 0)
            {
                AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, "还没有保存任何布局");
            }
            for (var i = 0; i < _config.Profiles.Count; i++)
            {
                var profile = _config.Profiles[i];
                var label = profile.Name.Replace("&", "&&");
                if (Hotkey.TryParse(profile.Hotkey, out var hotkey, out _))
                {
                    label += "\t" + hotkey.Value.ToDisplayString();
                }
                AppendMenuW(menu, MF_STRING, (nuint)(MenuProfileBase + i), label);
            }
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, MenuSettings, "设置…");
            AppendMenuW(menu, MF_STRING | (AutoStart.IsEnabled ? MF_CHECKED : 0), MenuAutoStart, "开机自启");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, MenuExit, "退出");

            GetCursorPos(out var pt);
            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
            OnMenuCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void OnMenuCommand(int command)
    {
        switch (command)
        {
            case MenuSettings:
                SettingsLauncher.Start();
                break;
            case MenuAutoStart:
                AutoStart.Set(!AutoStart.IsEnabled);
                break;
            case MenuExit:
                DestroyWindow(_hwnd);
                break;
            case >= MenuProfileBase when command - MenuProfileBase < _config.Profiles.Count:
                SwitchTo(_config.Profiles[command - MenuProfileBase]);
                break;
        }
    }

    private void ApplyTheme()
    {
        var light = SystemTheme.IsTaskbarLight();
        SystemTheme.ApplyToMenus(light);
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        var previous = _icon;
        _icon = IconLoader.Load(light ? "tray-light.ico" : "tray-dark.ico", size);
        var data = NewNotifyData(NIF_ICON);
        Shell_NotifyIconW(NIM_MODIFY, &data);
        if (previous != 0)
        {
            DestroyIcon(previous);
        }
    }

    private void AddTrayIcon()
    {
        var data = NewNotifyData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.uCallbackMessage = TrayCallback;
        CopyTo(data.szTip, 128, Tooltip());
        Shell_NotifyIconW(NIM_ADD, &data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    private void RemoveTrayIcon()
    {
        var data = NewNotifyData(0);
        Shell_NotifyIconW(NIM_DELETE, &data);
    }

    private void UpdateTooltip()
    {
        var data = NewNotifyData(NIF_TIP | NIF_SHOWTIP);
        CopyTo(data.szTip, 128, Tooltip());
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    private void ShowBalloon(string title, string text, bool error)
    {
        var data = NewNotifyData(NIF_INFO);
        CopyTo(data.szInfoTitle, 64, title);
        CopyTo(data.szInfo, 256, text);
        data.dwInfoFlags = error ? NIIF_ERROR : NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    private NOTIFYICONDATAW NewNotifyData(uint flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _hwnd,
        uID = TrayIconId,
        uFlags = flags,
        hIcon = _icon,
    };

    private string Tooltip()
    {
        var lines = _config.Profiles
            .Where(p => !string.IsNullOrEmpty(p.Hotkey))
            .Select(p => $"{p.Name}：{p.Hotkey}");
        return string.Join("\n", lines.Prepend("PrimeShift"));
    }
}
