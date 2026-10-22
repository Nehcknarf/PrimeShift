using System.Text.Json.Serialization;

namespace PrimeShift.Core.Display;

/// <summary>One monitor as seen when a layout was captured or listed.</summary>
public sealed class MonitorInfo
{
    public string Name { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string Adapter { get; set; } = "";
    public uint TargetId { get; set; }
    public bool Active { get; set; }
    public string GdiName { get; set; } = "";
    public uint Width { get; set; }
    public uint Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public double RefreshHz { get; set; }
    public bool Primary { get; set; }

    public string Describe()
    {
        if (!Active)
        {
            return $"{Name}（已连接，未启用）";
        }
        var primary = Primary ? "，主屏" : "";
        return $"{Name}  {Width}×{Height} @ {RefreshHz:0.##} Hz（位置 {X},{Y}{primary}）";
    }
}

/// <summary>
/// A saved display layout. Paths and modes hold the raw DISPLAYCONFIG records (hex encoded) so
/// applying the profile restores resolution, refresh rate, position, rotation and the primary
/// display exactly. Adapters maps each adapter LUID key to its device path, because LUIDs change
/// across reboots while device paths do not.
/// </summary>
public sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public string? Hotkey { get; set; }
    public Dictionary<string, string> Adapters { get; set; } = [];
    public List<MonitorInfo> Monitors { get; set; } = [];
    public List<string> Paths { get; set; } = [];
    public List<string> Modes { get; set; } = [];

    [JsonIgnore]
    public string Summary => Monitors.Count == 0
        ? "（没有显示器信息）"
        : string.Join("\n", Monitors.Select(m => m.Describe()));
}
