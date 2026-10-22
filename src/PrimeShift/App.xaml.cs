using System.Windows;
using System.Windows.Threading;
using PrimeShift.Core.Config;
using PrimeShift.Settings;

namespace PrimeShift;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        MainWindow = new SettingsWindow();
        MainWindow.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("设置窗口出错", e.Exception);
        MessageBox.Show(e.Exception.Message, "PrimeShift", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
