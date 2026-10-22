using System.ComponentModel;
using System.Runtime.CompilerServices;
using PrimeShift.Core.Display;
using PrimeShift.Core.Hotkeys;

namespace PrimeShift.Settings;

/// <summary>View model for one layout card in the settings window.</summary>
public sealed class ProfileItem(LayoutProfile model) : INotifyPropertyChanged
{
    private string? _error;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LayoutProfile Model { get; } = model;

    public string Name => Model.Name;

    /// <summary>One-line description for the collapsed card.</summary>
    public string Overview
    {
        get
        {
            var monitors = Model.Monitors;
            if (monitors.Count == 0)
            {
                return "没有显示器信息";
            }
            var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
            var mode = $"{primary.Width}×{primary.Height} @ {primary.RefreshHz:0.##} Hz";
            return monitors.Count == 1
                ? $"仅 {primary.Name} · {mode}"
                : $"{monitors.Count} 块显示器 · 主屏 {primary.Name} · {mode}";
        }
    }

    public IReadOnlyList<MonitorItem> Monitors => Model.Monitors.Select(m => new MonitorItem(m)).ToList();

    public IReadOnlyList<string> HotkeyParts =>
        Hotkey.TryParse(Model.Hotkey, out var hotkey, out _) ? hotkey.Value.Parts : [];

    public bool HasHotkey => HotkeyParts.Count > 0;

    public string? Error
    {
        get => _error;
        set
        {
            _error = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Call after changing <see cref="Model"/> directly.</summary>
    public void Refresh()
    {
        foreach (var name in new[] { nameof(Name), nameof(Overview), nameof(Monitors), nameof(HotkeyParts), nameof(HasHotkey) })
        {
            OnPropertyChanged(name);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One monitor row, shown on the monitors page and inside an expanded layout card.</summary>
public sealed class MonitorItem(MonitorInfo info)
{
    public string Name => info.Name;

    public bool IsPrimary => info.Primary;

    public bool IsActive => info.Active;

    public string Detail
    {
        get
        {
            if (!info.Active)
            {
                return "已连接，未启用";
            }
            var detail = $"{info.Width}×{info.Height} · {info.RefreshHz:0.##} Hz · 位置 ({info.X}, {info.Y})";
            return string.IsNullOrEmpty(info.GdiName) ? detail : $"{detail} · {info.GdiName.TrimStart('\\', '.')}";
        }
    }
}
