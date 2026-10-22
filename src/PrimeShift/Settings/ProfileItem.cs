using System.ComponentModel;
using System.Runtime.CompilerServices;
using PrimeShift.Core.Display;

namespace PrimeShift.Settings;

/// <summary>View model for one layout card in the settings window.</summary>
public sealed class ProfileItem(LayoutProfile model) : INotifyPropertyChanged
{
    private string? _error;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LayoutProfile Model { get; } = model;

    public string Name => Model.Name;

    public string Summary => Model.Summary;

    public string? HotkeyText => Model.Hotkey;

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

    /// <summary>Call after changing <see cref="Model"/> directly.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HotkeyText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
