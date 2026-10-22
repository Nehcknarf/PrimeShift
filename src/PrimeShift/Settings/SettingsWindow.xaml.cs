using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrimeShift.Agent;
using PrimeShift.Core.Config;
using PrimeShift.Core.Display;
using PrimeShift.Core.Hotkeys;

namespace PrimeShift.Settings;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<ProfileItem> _items = [];
    private AppConfig _config = new();
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        ProfileList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            AutoStartBox.IsChecked = AutoStart.IsEnabled;
            AutoStartStaleHint.Visibility = AutoStart.IsStale ? Visibility.Visible : Visibility.Collapsed;
            NotifyBox.IsChecked = _config.NotifyOnSwitch;
            var version = typeof(SettingsWindow).Assembly.GetName().Version;
            FooterText.Text = $"版本 {version?.ToString(3)} · 配置文件：{ConfigStore.ConfigPath}";
            if (!AgentChannel.IsAgentRunning)
            {
                FooterText.Text += "\n托盘程序未运行，快捷键不会生效。直接双击 PrimeShift.exe 即可启动。";
            }
            RefreshMonitors();
        }
        finally
        {
            _loading = false;
        }
    }

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
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CheckSavedHotkeys();
        return true;
    }

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
                item.Error = $"{hotkey.Value.ToDisplayString()} 当前不会生效：{conflict}";
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

    private void Save()
    {
        ConfigStore.Save(_config);
        AgentChannel.NotifyConfigChanged();
    }

    private void RefreshMonitors()
    {
        try
        {
            MonitorList.ItemsSource = LayoutService.Default.ListMonitors().Select(m => m.Describe()).ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or LayoutException)
        {
            MonitorList.ItemsSource = new[] { $"无法读取显示器信息：{ex.Message}" };
        }
    }

    private static ProfileItem ItemOf(object sender) => (ProfileItem)((FrameworkElement)sender).DataContext;

    private static void ShowError(string message) =>
        MessageBox.Show(message, "PrimeShift", MessageBoxButton.OK, MessageBoxImage.Error);

    // ---- layout cards ----

    private void OnHotkeyRecorded(object? sender, HotkeyRecordedEventArgs e)
    {
        var item = ItemOf(sender!);
        if (e.Hotkey is not { } hotkey)
        {
            item.Model.Hotkey = null;
            item.Error = null;
        }
        else
        {
            var conflict = CheckHotkey(item, hotkey);
            if (conflict is not null)
            {
                item.Error = $"{hotkey.ToDisplayString()}：{conflict}";
                return;
            }
            item.Model.Hotkey = hotkey.ToString();
            item.Error = null;
        }
        item.Refresh();
        Save();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var item = ItemOf(sender);
        try
        {
            LayoutService.Default.Apply(item.Model);
        }
        catch (Exception ex) when (ex is LayoutException or System.ComponentModel.Win32Exception)
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
        catch (System.ComponentModel.Win32Exception ex)
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
        if (e.Key is Key.Enter or Key.Escape)
        {
            if (e.Key == Key.Escape)
            {
                ((TextBox)sender).SetCurrentValue(TextBox.TextProperty, ItemOf(sender).Name);
            }
            Keyboard.ClearFocus();
            e.Handled = true;
        }
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
            item.Error = error;
            // SetCurrentValue keeps the OneWay binding, unlike assigning Text.
            box.SetCurrentValue(TextBox.TextProperty, item.Name);
            return;
        }
        item.Model.Name = name;
        item.Error = null;
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

    // ---- new layout ----

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
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowError($"无法读取当前布局：\n{ex.Message}");
            return;
        }
        _config.Profiles.Add(profile);
        _items.Add(new ProfileItem(profile));
        NewNameBox.Clear();
        Save();
    }

    private void OnOpenDisplaySettings(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true })?.Dispose();

    private void OnRefreshMonitors(object sender, RoutedEventArgs e) => RefreshMonitors();

    // ---- general ----

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        AutoStart.Set(AutoStartBox.IsChecked == true);
        AutoStartStaleHint.Visibility = Visibility.Collapsed;
    }

    private void OnNotifyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        _config.NotifyOnSwitch = NotifyBox.IsChecked == true;
        Save();
    }
}
