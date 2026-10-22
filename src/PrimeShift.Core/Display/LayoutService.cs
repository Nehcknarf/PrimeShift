using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimeShift.Core.Native;
using static PrimeShift.Core.Native.DisplayConfigConstants;

namespace PrimeShift.Core.Display;

public sealed class LayoutException(string message) : Exception(message);

/// <summary>Captures and applies complete display layouts through the CCD API.</summary>
public sealed class LayoutService(IDisplayApi api)
{
    public static LayoutService Default { get; } = new(Win32DisplayApi.Instance);

    /// <summary>All connected monitors: active ones first, then connected but disabled ones.</summary>
    public List<MonitorInfo> ListMonitors()
    {
        var (paths, modes) = api.Query(QDC_ONLY_ACTIVE_PATHS);
        var monitors = Describe(paths, modes);
        var seen = monitors.Select(m => (m.Adapter, m.TargetId)).ToHashSet();
        var (allPaths, _) = api.Query(QDC_ALL_PATHS);
        foreach (var path in allPaths)
        {
            var target = path.targetInfo;
            if (target.targetAvailable == 0 || !seen.Add((target.adapterId.Key, target.id)))
            {
                continue;
            }
            var (name, devicePath) = TargetName(target.adapterId, target.id);
            monitors.Add(new MonitorInfo
            {
                Name = name,
                DevicePath = devicePath,
                Adapter = target.adapterId.Key,
                TargetId = target.id,
            });
        }
        return monitors;
    }

    /// <summary>Snapshot the current display layout.</summary>
    public LayoutProfile Capture(string name)
    {
        var (paths, modes) = api.Query(QDC_ONLY_ACTIVE_PATHS);
        var adapters = new Dictionary<string, string>();
        foreach (var luid in AdapterLuids(paths, modes))
        {
            if (!adapters.ContainsKey(luid.Key))
            {
                adapters[luid.Key] = api.GetAdapterPath(luid);
            }
        }
        return new LayoutProfile
        {
            Name = name,
            Adapters = adapters,
            Monitors = Describe(paths, modes),
            Paths = paths.Select(Encode).ToList(),
            Modes = modes.Select(Encode).ToList(),
        };
    }

    /// <summary>Apply a profile, turning off every display that is not part of it.</summary>
    public void Apply(LayoutProfile profile)
    {
        var (paths, modes) = Decode(profile);

        var (allPaths, _) = api.Query(QDC_ALL_PATHS);
        var currentAdapters = new Dictionary<string, LUID>();
        foreach (var luid in AdapterLuids(allPaths, []).DistinctBy(l => l.Key))
        {
            currentAdapters[api.GetAdapterPath(luid)] = luid;
        }
        var available = allPaths
            .Where(p => p.targetInfo.targetAvailable != 0)
            .Select(p => (p.targetInfo.adapterId.Key, p.targetInfo.id))
            .ToHashSet();

        RemapLuids(paths, modes, ResolveAdapters(profile.Adapters, currentAdapters));

        var missing = MissingTargets(paths, available);
        if (missing.Count > 0)
        {
            var names = missing.Select(i => i < profile.Monitors.Count ? profile.Monitors[i].Name : $"#{i}");
            throw new LayoutException("以下显示器当前未连接：" + string.Join("、", names));
        }

        const uint baseFlags = SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
        // Validate first so a bad profile never leaves the desktop half-applied.
        api.Set(paths, modes, baseFlags | SDC_VALIDATE);
        api.Set(paths, modes, baseFlags | SDC_APPLY | SDC_SAVE_TO_DATABASE);
    }

    internal List<MonitorInfo> Describe(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var monitors = new List<MonitorInfo>(paths.Length);
        foreach (var path in paths)
        {
            var (source, target) = (path.sourceInfo, path.targetInfo);
            var (name, devicePath) = TargetName(target.adapterId, target.id);
            var monitor = new MonitorInfo
            {
                Name = name,
                DevicePath = devicePath,
                Adapter = target.adapterId.Key,
                TargetId = target.id,
                Active = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0,
                GdiName = GdiName(source.adapterId, source.id),
                RefreshHz = target.refreshRate.Denominator == 0
                    ? 0
                    : Math.Round((double)target.refreshRate.Numerator / target.refreshRate.Denominator, 2),
            };
            var idx = source.modeInfoIdx;
            if (idx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && idx < modes.Length && modes[idx].infoType == MODE_INFO_TYPE_SOURCE)
            {
                var sourceMode = modes[idx].sourceMode;
                monitor.Width = sourceMode.width;
                monitor.Height = sourceMode.height;
                monitor.X = sourceMode.position.x;
                monitor.Y = sourceMode.position.y;
                // The primary display is by definition the one whose desktop origin is (0, 0).
                monitor.Primary = monitor.Active && monitor.X == 0 && monitor.Y == 0;
            }
            monitors.Add(monitor);
        }
        return monitors;
    }

    private (string Name, string DevicePath) TargetName(LUID adapter, uint targetId)
    {
        var (name, devicePath) = api.GetTargetName(adapter, targetId);
        return (string.IsNullOrEmpty(name) ? "未知显示器" : name, devicePath);
    }

    private string GdiName(LUID adapter, uint sourceId)
    {
        try
        {
            return api.GetSourceGdiName(adapter, sourceId);
        }
        catch (DisplayConfigException)
        {
            return "";
        }
    }

    private static IEnumerable<LUID> AdapterLuids(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        foreach (var path in paths)
        {
            yield return path.sourceInfo.adapterId;
            yield return path.targetInfo.adapterId;
        }
        foreach (var mode in modes)
        {
            yield return mode.adapterId;
        }
    }

    internal static string Encode<T>(T value) where T : unmanaged =>
        Convert.ToHexString(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(ref value)));

    internal static T DecodeRecord<T>(string hex) where T : unmanaged
    {
        var bytes = Convert.FromHexString(hex);
        if (bytes.Length != Unsafe.SizeOf<T>())
        {
            throw new FormatException($"记录长度 {bytes.Length} 与 {typeof(T).Name} 不符");
        }
        return MemoryMarshal.Read<T>(bytes);
    }

    internal static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Decode(LayoutProfile profile)
    {
        try
        {
            var paths = profile.Paths.Select(DecodeRecord<DISPLAYCONFIG_PATH_INFO>).ToArray();
            var modes = profile.Modes.Select(DecodeRecord<DISPLAYCONFIG_MODE_INFO>).ToArray();
            if (paths.Length == 0)
            {
                throw new LayoutException($"布局「{profile.Name}」中没有任何显示器");
            }
            return (paths, modes);
        }
        catch (FormatException ex)
        {
            throw new LayoutException($"布局「{profile.Name}」数据已损坏：{ex.Message}");
        }
    }

    internal static Dictionary<string, LUID> ResolveAdapters(
        Dictionary<string, string> saved, Dictionary<string, LUID> currentByDevicePath)
    {
        var mapping = new Dictionary<string, LUID>();
        foreach (var (key, devicePath) in saved)
        {
            if (!currentByDevicePath.TryGetValue(devicePath, out var luid))
            {
                throw new LayoutException($"找不到布局中记录的显卡：{devicePath}");
            }
            mapping[key] = luid;
        }
        return mapping;
    }

    internal static void RemapLuids(
        DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, Dictionary<string, LUID> mapping)
    {
        LUID Map(LUID old) => mapping.TryGetValue(old.Key, out var luid)
            ? luid
            : throw new LayoutException($"布局数据不完整：缺少显卡 {old.Key} 的记录");

        for (var i = 0; i < paths.Length; i++)
        {
            paths[i].sourceInfo.adapterId = Map(paths[i].sourceInfo.adapterId);
            paths[i].targetInfo.adapterId = Map(paths[i].targetInfo.adapterId);
        }
        for (var i = 0; i < modes.Length; i++)
        {
            modes[i].adapterId = Map(modes[i].adapterId);
        }
    }

    internal static List<int> MissingTargets(DISPLAYCONFIG_PATH_INFO[] paths, HashSet<(string, uint)> available) =>
        Enumerable.Range(0, paths.Length)
            .Where(i => !available.Contains((paths[i].targetInfo.adapterId.Key, paths[i].targetInfo.id)))
            .ToList();
}
