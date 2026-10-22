using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrimeShift.Core.Hotkeys;

namespace PrimeShift.Settings;

public sealed class HotkeyRecordedEventArgs(RoutedEvent routedEvent, Hotkey? hotkey) : RoutedEventArgs(routedEvent)
{
    /// <summary>The recorded combination, or null when the user cleared the hotkey.</summary>
    public Hotkey? Hotkey { get; } = hotkey;
}

/// <summary>
/// Click, then press a key combination to record it. Backspace/Delete clears, Esc cancels and
/// Tab moves on as usual.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyTextProperty = DependencyProperty.Register(
        nameof(HotkeyText), typeof(string), typeof(HotkeyBox),
        new PropertyMetadata(null, (d, _) => ((HotkeyBox)d).ShowCurrent()));

    public static readonly RoutedEvent RecordedEvent = EventManager.RegisterRoutedEvent(
        nameof(Recorded), RoutingStrategy.Bubble, typeof(EventHandler<HotkeyRecordedEventArgs>), typeof(HotkeyBox));

    private bool _recording;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip = "点击后按下组合键；Backspace 清除，Esc 取消";
        ContextMenu = null;
        ShowCurrent();
    }

    /// <summary>The saved hotkey in config text form ("Ctrl+Alt+F11"), or null.</summary>
    public string? HotkeyText
    {
        get => (string?)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    public event EventHandler<HotkeyRecordedEventArgs> Recorded
    {
        add => AddHandler(RecordedEvent, value);
        remove => RemoveHandler(RecordedEvent, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _recording = true;
        Text = "请按下组合键…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _recording = false;
        ShowCurrent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        if (key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }
        if (key == Key.Tab && modifiers == HotkeyModifiers.None)
        {
            return; // keep keyboard navigation working
        }

        e.Handled = true;
        if (modifiers == HotkeyModifiers.None && key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete)
        {
            Finish(null);
            return;
        }
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (Hotkey.IsModifierKey(vk))
        {
            Text = modifiers == HotkeyModifiers.None
                ? "请按下组合键…"
                : Hotkey.ModifiersDisplayString(modifiers) + " + …";
            return;
        }
        Finish(new Hotkey(modifiers, vk));
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_recording && Keyboard.Modifiers == ModifierKeys.None)
        {
            Text = "请按下组合键…";
        }
    }

    private void Finish(Hotkey? hotkey)
    {
        RaiseEvent(new HotkeyRecordedEventArgs(RecordedEvent, hotkey));
        Keyboard.ClearFocus();
    }

    private void ShowCurrent()
    {
        if (_recording)
        {
            return;
        }
        Text = Hotkey.TryParse(HotkeyText, out var hotkey, out _) ? hotkey.Value.ToDisplayString() : "未设置";
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
}
