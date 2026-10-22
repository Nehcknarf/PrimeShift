using System.ComponentModel;
using System.Runtime.InteropServices;
using static PrimeShift.Core.Native.DisplayConfigConstants;

namespace PrimeShift.Core.Native;

/// <summary>The subset of the CCD API PrimeShift uses, abstracted so layout logic can be tested.</summary>
public interface IDisplayApi
{
    (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(uint flags);

    void Set(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, uint flags);

    (string FriendlyName, string DevicePath) GetTargetName(LUID adapter, uint targetId);

    string GetSourceGdiName(LUID adapter, uint sourceId);

    string GetAdapterPath(LUID adapter);
}

public sealed class DisplayConfigException(string function, int error)
    : Win32Exception(error, $"{function} 失败（错误码 {error}）：{new Win32Exception(error).Message}");

public sealed unsafe partial class Win32DisplayApi : IDisplayApi
{
    public static Win32DisplayApi Instance { get; } = new();

    public (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(uint flags)
    {
        while (true)
        {
            Check(GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount), "GetDisplayConfigBufferSizes");
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            int rc;
            fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
            {
                rc = QueryDisplayConfig(flags, ref pathCount, p, ref modeCount, m, 0);
            }
            // The topology can change between the two calls; retry with fresh sizes.
            if (rc == ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }
            Check(rc, "QueryDisplayConfig");
            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    public void Set(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, uint flags)
    {
        fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
        {
            Check(SetDisplayConfig((uint)paths.Length, p, (uint)modes.Length, m, flags), "SetDisplayConfig");
        }
    }

    public (string FriendlyName, string DevicePath) GetTargetName(LUID adapter, uint targetId)
    {
        var info = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        GetDeviceInfo(&info.header, DEVICE_INFO_GET_TARGET_NAME, (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), adapter, targetId);
        return (ReadString(info.monitorFriendlyDeviceName, 64), ReadString(info.monitorDevicePath, 128));
    }

    public string GetSourceGdiName(LUID adapter, uint sourceId)
    {
        var info = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        GetDeviceInfo(&info.header, DEVICE_INFO_GET_SOURCE_NAME, (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME), adapter, sourceId);
        return ReadString(info.viewGdiDeviceName, 32);
    }

    public string GetAdapterPath(LUID adapter)
    {
        var info = new DISPLAYCONFIG_ADAPTER_NAME();
        GetDeviceInfo(&info.header, DEVICE_INFO_GET_ADAPTER_NAME, (uint)sizeof(DISPLAYCONFIG_ADAPTER_NAME), adapter, 0);
        return ReadString(info.adapterDevicePath, 128);
    }

    private static void GetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_HEADER* header, uint type, uint size, LUID adapter, uint id)
    {
        header->type = type;
        header->size = size;
        header->adapterId = adapter;
        header->id = id;
        Check(DisplayConfigGetDeviceInfo(header), "DisplayConfigGetDeviceInfo");
    }

    private static string ReadString(char* buffer, int capacity)
    {
        var span = new ReadOnlySpan<char>(buffer, capacity);
        var end = span.IndexOf('\0');
        return new string(end < 0 ? span : span[..end]);
    }

    private static void Check(int rc, string function)
    {
        if (rc != ERROR_SUCCESS)
        {
            throw new DisplayConfigException(function, rc);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(
        uint flags, ref uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO* pathArray,
        ref uint numModeInfoArrayElements, DISPLAYCONFIG_MODE_INFO* modeInfoArray, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    private static partial int SetDisplayConfig(
        uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO* pathArray,
        uint numModeInfoArrayElements, DISPLAYCONFIG_MODE_INFO* modeInfoArray, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_HEADER* requestPacket);
}
