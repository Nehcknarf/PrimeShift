using PrimeShift.Core.Config;
using PrimeShift.Core.Display;
using PrimeShift.Core.Hotkeys;

namespace PrimeShift.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F11", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7Au)]
    [InlineData("ctrl + alt + f12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7Bu)]
    [InlineData("Win+Shift+K", HotkeyModifiers.Win | HotkeyModifiers.Shift, (uint)'K')]
    [InlineData("Ctrl+1", HotkeyModifiers.Control, (uint)'1')]
    [InlineData("Ctrl+Num5", HotkeyModifiers.Control, 0x65u)]
    [InlineData("Alt+PageDown", HotkeyModifiers.Alt, 0x22u)]
    [InlineData("Shift+F5", HotkeyModifiers.Shift, 0x74u)]
    [InlineData("Ctrl+0xE2", HotkeyModifiers.Control, 0xE2u)]
    public void ParsesValidHotkeys(string text, HotkeyModifiers modifiers, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey, out var error), error);
        Assert.Equal(new Hotkey(modifiers, vk), hotkey);
    }

    [Theory]
    [InlineData("F11")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+F1")]
    [InlineData("Ctrl+F25")]
    [InlineData("Shift+A")]
    [InlineData("Ctrl+Shift")]
    [InlineData("")]
    public void RejectsInvalidHotkeys(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void FormatsInCanonicalOrderAndRoundTrips()
    {
        var hotkey = new Hotkey(HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Win, 0xBA);
        Assert.Equal("Win+Ctrl+Alt+Shift+;", hotkey.ToString());
        Assert.Equal("Win + Ctrl + Alt + Shift + ;", hotkey.ToDisplayString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var parsed, out _));
        Assert.Equal(hotkey, parsed);
    }

    [Fact]
    public void ConfigRoundTripsThroughDisk()
    {
        var dir = Directory.CreateTempSubdirectory();
        var previous = Environment.GetEnvironmentVariable("PRIMESHIFT_HOME");
        Environment.SetEnvironmentVariable("PRIMESHIFT_HOME", dir.FullName);
        try
        {
            var config = new AppConfig
            {
                NotifyOnSwitch = false,
                Profiles = [new LayoutProfile { Name = "远程", Hotkey = "Ctrl+Alt+F11", Paths = ["00"] }],
            };
            ConfigStore.Save(config);

            Assert.Contains("远程", File.ReadAllText(ConfigStore.ConfigPath));
            var loaded = ConfigStore.Load();
            Assert.False(loaded.NotifyOnSwitch);
            var profile = Assert.Single(loaded.Profiles);
            Assert.Equal(("远程", "Ctrl+Alt+F11"), (profile.Name, profile.Hotkey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRIMESHIFT_HOME", previous);
            dir.Delete(recursive: true);
        }
    }
}
