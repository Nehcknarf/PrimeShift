using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PrimeShift.Agent;
using PrimeShift.Core.Config;
using PrimeShift.Core.Display;
using PrimeShift.Core.Hotkeys;

namespace PrimeShift.Settings;

public partial class SettingsWindow : Window
{
    private const string RepositoryUrl = "https://github.com/Nehcknarf/PrimeShift";

    private readonly ObservableCollection<ProfileItem> _items = [];
    private AppConfig _config = new();
    private bool _loading;

    // Shortcut dialog state
    private ProfileItem? _shortcutTarget;
    private Hotkey? _pendingHotkey;
    private bool _pendingCleared;

    public SettingsWindow()
    {
        InitializeComponent();
        ProfileList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateEmptyHint();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            if (!LoadConfig())
            {
                Close();
                return;
            }
            AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
            AutoStartStale.Visibility = AutoStart.IsStale ? Visibility.Visible : Visibility.Collapsed;
            NotifySwitch.IsChecked = _config.NotifyOnSwitch;
            VersionText.Text = $"版本 {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";
            DataCard.Description = ConfigStore.Directory;
            AgentInfoBar.Visibility = AgentChannel.IsAgentRunning ? Visibility.Collapsed : Visibility.Visible;
            RefreshMonitors();
        }
        finally
        {
            _loading = false;
        }
    }

    // ---------------------------------------------------------------- navigation

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        // The initially selected item can raise this while InitializeComponent is still
        // building the page elements.
        if (sender is not ListBox { SelectedItem: ListBoxItem { Tag: string page } } list || !IsInitialized)
        {
            return;
        }
        // Two lists (top and footer) act as one navigation menu.
        var other = list == MainNav ? FooterNav : MainNav;
        other.SelectedIndex = -1;
        LayoutsPage.Visibility = page == "Layouts" ? Visibility.Visible : Visibility.Collapsed;
        MonitorsPage.Visibility = page == "Monitors" ? Visibility.Visible : Visibility.Collapsed;
        GeneralPage.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Monitors")
        {
            RefreshMonitors();
        }
    }

    // ---------------------------------------------------------------- config

    private bool LoadConfig()
    {
        try
        {
            _config = ConfigStore.Load();
        }
        catch (ConfigException ex)
        {
            var reset = MessageBox.Show(
                $"{ex.Message}\n\n是否重置为空配置？选择“否”将关闭设置窗口，你可以先手动修复该文件。",
                "PrimeShift", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (reset != MessageBoxResult.Yes)
            {
                return false;
            }
            _config = new AppConfig();
        }

        _items.Clear();
        foreach (var profile in _config.Profiles)
        {
            _items.Add(new ProfileItem(profile));
        }
        UpdateEmptyHint();
        CheckSavedHotkeys();
        return true;
    }

    private void Save()
    {
        ConfigStore.Save(_config);
        AgentChannel.NotifyConfigChanged();
    }

    private void UpdateEmptyHint() =>
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Flag saved hotkeys that are invalid or have since been taken by another program.</summary>
    private void CheckSavedHotkeys()
    {
        foreach (var item in _items)
        {
            if (string.IsNullOrEmpty(item.Model.Hotkey))
            {
                continue;
            }
            if (!Hotkey.TryParse(item.Model.Hotkey, out var hotkey, out var error))
            {
                item.Error = $"已保存的快捷键无效：{error}";
                continue;
            }
            var conflict = CheckHotkey(item, hotkey.Value);
            if (conflict is not null)
            {
                item.Error = $"快捷键当前不会生效：{conflict}";
            }
        }
    }

    private string? CheckHotkey(ProfileItem item, Hotkey hotkey)
    {
        var invalid = hotkey.Validate();
        if (invalid is not null)
        {
            return invalid;
        }
        var duplicate = _items.FirstOrDefault(other =>
            other != item && Hotkey.TryParse(other.Model.Hotkey, out var existing, out _) && existing == hotkey);
        if (duplicate is not null)
        {
            return $"与布局「{duplicate.Name}」的快捷键重复";
        }
        // The tray agent released its own hotkeys when this window opened, so anything still
        // registered belongs to another program or to Windows itself.
        return HotkeyProbe.Probe(hotkey) switch
        {
            HotkeyStatus.Available => null,
            HotkeyStatus.InUse => "已被其他程序或系统占用，请换一个组合",
            _ => "Windows 不接受这个组合，请换一个",
        };
    }

    private static ProfileItem ItemOf(object sender) => (ProfileItem)((FrameworkElement)sender).DataContext;

    private static void ShowError(string message) =>
        MessageBox.Show(message, "PrimeShift", MessageBoxButton.OK, MessageBoxImage.Error);

    private static void OpenShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    // ---------------------------------------------------------------- layouts page

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var item = ItemOf(sender);
        try
        {
            LayoutService.Default.Apply(item.Model);
        }
        catch (Exception ex) when (ex is LayoutException or Win32Exception)
        {
            ShowError($"无法切换到「{item.Name}」：\n{ex.Message}");
        }
        RefreshMonitors();
    }

    private void OnOverwrite(object sender, RoutedEventArgs e)
    {
        var item = ItemOf(sender);
        LayoutProfile captured;
        try
        {
            captured = LayoutService.Default.Capture(item.Name);
        }
        catch (Win32Exception ex)
        {
            ShowError($"无法读取当前布局：\n{ex.Message}");
            return;
        }
        item.Model.Adapters = captured.Adapters;
        item.Model.Monitors = captured.Monitors;
        item.Model.Paths = captured.Paths;
        item.Model.Modes = captured.Modes;
        item.Refresh();
        Save();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var item = ItemOf(sender);
        var answer = MessageBox.Show($"删除布局「{item.Name}」？", "PrimeShift", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        _config.Profiles.Remove(item.Model);
        _items.Remove(item);
        Save();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            ((TextBox)sender).SetCurrentValue(TextBox.TextProperty, ItemOf(sender).Name);
        }
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        var item = ItemOf(sender);
        var name = box.Text.Trim();
        if (name == item.Name)
        {
            return;
        }
        var error = ValidateName(name, item);
        if (error is not null)
        {
            ShowError(error);
            // SetCurrentValue keeps the OneWay binding, unlike assigning Text.
            box.SetCurrentValue(TextBox.TextProperty, item.Name);
            return;
        }
        item.Model.Name = name;
        item.Refresh();
        Save();
    }

    private string? ValidateName(string name, ProfileItem? self)
    {
        if (name.Length == 0)
        {
            return "名称不能为空";
        }
        return _items.Any(i => i != self && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            ? $"已经有名为「{name}」的布局"
            : null;
    }

    private void OnNewNameChanged(object sender, TextChangedEventArgs e) =>
        NewNamePlaceholder.Visibility = NewNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnNewNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSaveCurrent(sender, e);
            e.Handled = true;
        }
    }

    private void OnSaveCurrent(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text.Trim();
        var error = ValidateName(name, null);
        if (error is not null)
        {
            ShowError(error);
            NewNameBox.Focus();
            return;
        }
        LayoutProfile profile;
        try
        {
            profile = LayoutService.Default.Capture(name);
        }
        catch (Win32Exception ex)
        {
            ShowError($"无法读取当前布局：\n{ex.Message}");
            return;
        }
        _config.Profiles.Add(profile);
        _items.Add(new ProfileItem(profile) { IsExpanded = true });
        NewNameBox.Clear();
        Save();
    }

    private void OnStartAgent(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            ShowError($"无法启动托盘程序：\n{ex.Message}");
            return;
        }
        // Once the agent's window exists, ask it to release its hotkeys again while settings are open.
        var attempts = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (AgentChannel.IsAgentRunning)
            {
                AgentChannel.SuspendHotkeys();
                AgentInfoBar.Visibility = Visibility.Collapsed;
                timer.Stop();
            }
            else if (++attempts > 20)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- shortcut dialog

    private void OnEditShortcut(object sender, RoutedEventArgs e)
    {
        _shortcutTarget = ItemOf(sender);
        _pendingHotkey = Hotkey.TryParse(_shortcutTarget.Model.Hotkey, out var current, out _) ? current : null;
        ShortcutDialogTarget.Text = $"布局「{_shortcutTarget.Name}」";
        ShowPendingKeys(_pendingHotkey?.Parts ?? []);
        ValidatePendingHotkey(isInitial: true);
        ShortcutOverlay.Visibility = Visibility.Visible;
        Keyboard.Focus(ShortcutOverlay);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ShortcutOverlay.Visibility != Visibility.Visible)
        {
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        if (key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }
        e.Handled = true;
        if (modifiers == HotkeyModifiers.None)
        {
            switch (key)
            {
                case Key.Escape:
                    CloseShortcutDialog();
                    return;
                case Key.Enter:
                    if (SaveShortcutButton.IsEnabled)
                    {
                        OnSaveShortcut(sender, e);
                    }
                    return;
            }
        }
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (Hotkey.IsModifierKey(vk))
        {
            ShowPendingKeys(Hotkey.ModifierParts(modifiers));
            return;
        }
        _pendingHotkey = new Hotkey(modifiers, vk);
        _pendingCleared = false;
        ShowPendingKeys(_pendingHotkey.Value.Parts);
        ValidatePendingHotkey(isInitial: false);
    }

    private void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Releasing a half-typed combination (modifiers only) falls back to the last full one.
        if (ShortcutOverlay.Visibility == Visibility.Visible && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowPendingKeys(_pendingHotkey?.Parts ?? []);
            e.Handled = true;
        }
    }

    private void ShowPendingKeys(IReadOnlyList<string> parts)
    {
        PendingKeys.ItemsSource = parts;
        PendingPlaceholder.Visibility = parts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ValidatePendingHotkey(bool isInitial)
    {
        if (_shortcutTarget is null || _pendingHotkey is not { } hotkey)
        {
            // After "clear", saving with no keys removes the layout's shortcut.
            var canRemove = _pendingCleared && _shortcutTarget?.HasHotkey == true;
            ShortcutStatus.Visibility = canRemove ? Visibility.Visible : Visibility.Collapsed;
            ShortcutStatus.Severity = InfoBarSeverity.Informational;
            ShortcutStatus.Message = "已清除。按下新的组合键，或直接保存以取消这个布局的快捷键。";
            SaveShortcutButton.IsEnabled = canRemove;
            return;
        }
        var conflict = CheckHotkey(_shortcutTarget, hotkey);
        ShortcutStatus.Visibility = Visibility.Visible;
        ShortcutStatus.Severity = conflict is null ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ShortcutStatus.Message = conflict ?? (isInitial ? "当前快捷键可用。" : "没有发现冲突，可以使用。");
        SaveShortcutButton.IsEnabled = conflict is null;
    }

    private void OnSaveShortcut(object sender, RoutedEventArgs e)
    {
        if (_shortcutTarget is { } item && (_pendingHotkey is not null || _pendingCleared))
        {
            item.Model.Hotkey = _pendingHotkey?.ToString();
            item.Error = null;
            item.Refresh();
            Save();
        }
        CloseShortcutDialog();
    }

    /// <summary>Clear the keys entered so far and keep waiting for a new combination.</summary>
    private void OnClearShortcut(object sender, RoutedEventArgs e)
    {
        _pendingHotkey = null;
        _pendingCleared = true;
        ShowPendingKeys([]);
        ValidatePendingHotkey(isInitial: false);
        // The click moved focus to the button; give it back so the next keys are recorded.
        Keyboard.Focus(ShortcutOverlay);
    }

    private void OnCancelShortcut(object sender, RoutedEventArgs e) => CloseShortcutDialog();

    private void CloseShortcutDialog()
    {
        ShortcutOverlay.Visibility = Visibility.Collapsed;
        _shortcutTarget = null;
        _pendingHotkey = null;
        _pendingCleared = false;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys keys)
    {
        var result = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Win;
        return result;
    }

    // ---------------------------------------------------------------- monitors page

    private void RefreshMonitors()
    {
        try
        {
            MonitorList.ItemsSource = LayoutService.Default.ListMonitors().Select(m => new MonitorItem(m)).ToList();
            MonitorError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is Win32Exception or LayoutException)
        {
            MonitorList.ItemsSource = null;
            MonitorError.Message = $"无法读取显示器信息：{ex.Message}";
            MonitorError.Visibility = Visibility.Visible;
        }
    }

    private void OnRefreshMonitors(object sender, RoutedEventArgs e) => RefreshMonitors();

    private void OnOpenDisplaySettings(object sender, RoutedEventArgs e) => OpenShell("ms-settings:display");

    // ---------------------------------------------------------------- general page

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        AutoStart.Set(AutoStartSwitch.IsChecked == true);
        AutoStartStale.Visibility = Visibility.Collapsed;
    }

    private void OnNotifyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        _config.NotifyOnSwitch = NotifySwitch.IsChecked == true;
        Save();
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ConfigStore.Directory);
        OpenShell(ConfigStore.Directory);
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => OpenShell(RepositoryUrl);
}
