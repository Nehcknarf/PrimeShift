using System.Runtime.CompilerServices;
using PrimeShift.Core.Display;
using PrimeShift.Core.Native;
using static PrimeShift.Core.Native.DisplayConfigConstants;

namespace PrimeShift.Core.Tests;

public class LayoutServiceTests
{
    private static readonly LUID Gpu = new(0x1234, 0);

    [Fact]
    public unsafe void StructSizesMatchWingdi()
    {
        Assert.Equal(72, Unsafe.SizeOf<DISPLAYCONFIG_PATH_INFO>());
        Assert.Equal(64, Unsafe.SizeOf<DISPLAYCONFIG_MODE_INFO>());
        Assert.Equal(20, Unsafe.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>());
        Assert.Equal(84, sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
        Assert.Equal(420, sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
        Assert.Equal(276, sizeof(DISPLAYCONFIG_ADAPTER_NAME));
    }

    [Fact]
    public void DescribeMarksDisplayAtOriginAsPrimary()
    {
        var api = new FakeDisplayApi();
        var service = new LayoutService(api);
        var paths = new[] { Path(Gpu, 0, 0, 0), Path(Gpu, 1, 3, 1) };
        var modes = new[] { SourceMode(Gpu, 0, 5120, 2880, 0, 0), SourceMode(Gpu, 1, 1920, 1080, 5120, 0) };

        var monitors = service.Describe(paths, modes);

        Assert.True(monitors[0].Primary);
        Assert.Equal((5120u, 2880u, 165.0), (monitors[0].Width, monitors[0].Height, monitors[0].RefreshHz));
        Assert.False(monitors[1].Primary);
        Assert.Equal(("GL-RM10", 5120), (monitors[1].Name, monitors[1].X));
    }

    [Fact]
    public void CaptureRoundTripsRawRecords()
    {
        var api = new FakeDisplayApi { Active = ([Path(Gpu, 1, 3, 0)], [SourceMode(Gpu, 1, 1920, 1080, 0, 0)]) };
        var profile = new LayoutService(api).Capture("remote");

        var (paths, modes) = LayoutService.Decode(profile);

        Assert.Equal(LayoutService.Encode(api.Active.Paths[0]), LayoutService.Encode(paths[0]));
        Assert.Equal(LayoutService.Encode(api.Active.Modes[0]), LayoutService.Encode(modes[0]));
        Assert.Equal(new Dictionary<string, string> { ["00000000:00001234"] = FakeDisplayApi.GpuPath }, profile.Adapters);
        Assert.Equal("GL-RM10", Assert.Single(profile.Monitors).Name);
    }

    [Fact]
    public void ApplyRemapsAdapterLuidAfterReboot()
    {
        var api = new FakeDisplayApi { Active = ([Path(Gpu, 1, 3, 0)], [SourceMode(Gpu, 1, 1920, 1080, 0, 0)]) };
        var profile = new LayoutService(api).Capture("remote");

        var rebooted = new LUID(0x9999, 1);
        api.All = [Path(rebooted, 1, 3, 0, active: false)];
        new LayoutService(api).Apply(profile);

        Assert.Equal(2, api.SetCalls.Count);
        Assert.NotEqual(0u, api.SetCalls[0].Flags & SDC_VALIDATE);
        Assert.NotEqual(0u, api.SetCalls[1].Flags & SDC_APPLY);
        var (paths, modes, _) = api.SetCalls[1];
        Assert.All(
            new[] { paths[0].sourceInfo.adapterId, paths[0].targetInfo.adapterId, modes[0].adapterId },
            luid => Assert.Equal("00000001:00009999", luid.Key));
    }

    [Fact]
    public void ApplyRejectsUnknownAdapter()
    {
        var api = new FakeDisplayApi { Active = ([Path(Gpu, 1, 3, 0)], []) };
        var profile = new LayoutService(api).Capture("remote");
        api.AdapterPath = @"\\?\PCI#OTHER";

        var ex = Assert.Throws<LayoutException>(() => new LayoutService(api).Apply(profile));

        Assert.Contains("显卡", ex.Message);
        Assert.Empty(api.SetCalls);
    }

    [Fact]
    public void ApplyRejectsDisconnectedMonitorByName()
    {
        var api = new FakeDisplayApi { Active = ([Path(Gpu, 1, 3, 0)], []) };
        var profile = new LayoutService(api).Capture("remote");
        api.All = [Path(Gpu, 1, 3, 0, available: false)];

        var ex = Assert.Throws<LayoutException>(() => new LayoutService(api).Apply(profile));

        Assert.Contains("GL-RM10", ex.Message);
        Assert.Empty(api.SetCalls);
    }

    [Fact]
    public void DecodeRejectsCorruptRecords()
    {
        var profile = new LayoutProfile { Name = "x", Paths = ["ABCD"] };
        Assert.Throws<LayoutException>(() => LayoutService.Decode(profile));
    }

    [Fact]
    public void ListMonitorsIncludesConnectedButDisabledOnce()
    {
        var api = new FakeDisplayApi
        {
            Active = ([Path(Gpu, 0, 0, 0)], [SourceMode(Gpu, 0, 5120, 2880, 0, 0)]),
            All = [Path(Gpu, 0, 0, 0), Path(Gpu, 1, 3, 0, active: false), Path(Gpu, 2, 3, 0, active: false)],
        };

        var monitors = new LayoutService(api).ListMonitors();

        Assert.Equal(2, monitors.Count);
        Assert.False(monitors[1].Active);
        Assert.Equal("GL-RM10", monitors[1].Name);
    }

    private static DISPLAYCONFIG_PATH_INFO Path(
        LUID adapter, uint sourceId, uint targetId, uint sourceModeIdx, bool active = true, bool available = true)
    {
        var path = new DISPLAYCONFIG_PATH_INFO();
        path.sourceInfo.adapterId = adapter;
        path.sourceInfo.id = sourceId;
        path.sourceInfo.modeInfoIdx = sourceModeIdx;
        path.targetInfo.adapterId = adapter;
        path.targetInfo.id = targetId;
        path.targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        path.targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = 165000, Denominator = 1000 };
        path.targetInfo.targetAvailable = available ? 1 : 0;
        path.flags = active ? DISPLAYCONFIG_PATH_ACTIVE : 0;
        return path;
    }

    private static DISPLAYCONFIG_MODE_INFO SourceMode(LUID adapter, uint sourceId, uint width, uint height, int x, int y)
    {
        var mode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = MODE_INFO_TYPE_SOURCE,
            id = sourceId,
            adapterId = adapter,
        };
        mode.sourceMode.width = width;
        mode.sourceMode.height = height;
        mode.sourceMode.position = new POINTL { x = x, y = y };
        return mode;
    }

    private sealed class FakeDisplayApi : IDisplayApi
    {
        public const string GpuPath = @"\\?\PCI#GPU";

        public (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Active { get; set; } = ([], []);
        public DISPLAYCONFIG_PATH_INFO[]? All { get; set; }
        public string AdapterPath { get; set; } = GpuPath;
        public List<(DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes, uint Flags)> SetCalls { get; } = [];

        public (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(uint flags) =>
            flags == QDC_ALL_PATHS ? (All ?? Active.Paths, []) : Active;

        public void Set(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, uint flags) =>
            SetCalls.Add((paths, modes, flags));

        public (string FriendlyName, string DevicePath) GetTargetName(LUID adapter, uint targetId) =>
            targetId == 3 ? ("GL-RM10", @"\\?\DISPLAY#KVM") : ("Main 5K", @"\\?\DISPLAY#MAIN");

        public string GetSourceGdiName(LUID adapter, uint sourceId) => $@"\\.\DISPLAY{sourceId + 1}";

        public string GetAdapterPath(LUID adapter) => AdapterPath;
    }
}
