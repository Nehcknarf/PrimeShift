using System.Diagnostics;
using System.Runtime.CompilerServices;
using PrimeShift.Agent;
using PrimeShift.Core.Config;

namespace PrimeShift;

internal static class Program
{
    public const string SettingsArgument = "--settings";

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("未处理的异常", e.ExceptionObject as Exception);
        return args.Contains(SettingsArgument, StringComparer.OrdinalIgnoreCase)
            ? RunSettings()
            : TrayAgent.Run();
    }

    // Kept out of Main so the tray agent never JIT-compiles a method that references WPF types,
    // which would load the WPF assemblies into the resident process.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunSettings() => Settings.SettingsHost.Run();
}

internal static class SettingsLauncher
{
    public static void Start()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Program.SettingsArgument) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("无法打开设置窗口", ex);
        }
    }
}
