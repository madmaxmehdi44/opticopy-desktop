using Microsoft.UI.Xaml;
using OptiCopy.Data;

namespace OptiCopy.Windows;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static TransferHistoryStore History { get; } = new();
    public static AppSettingsStore Settings { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
