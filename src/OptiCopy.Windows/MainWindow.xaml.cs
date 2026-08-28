using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OptiCopy.Windows.Pages;
using WinRT.Interop;

namespace OptiCopy.Windows;

public sealed partial class MainWindow : Window
{
    private bool _settingsLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Activated += MainWindow_Activated;

        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow is not null)
            appWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 820));

        ContentFrame.Navigate(typeof(SenderPage));
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_settingsLoaded)
            return;

        _settingsLoaded = true;
        try
        {
            var settings = await App.Settings.LoadAsync();
            Root.RequestedTheme = settings.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
        }
        catch
        {
            Root.RequestedTheme = ElementTheme.Dark;
        }
    }

    private void SendNavButton_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(SenderPage));
    }

    private void ReceiveNavButton_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(ReceiverPage));
    }

    private void HistoryNavButton_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(HistoryPage));
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(SettingsPage));
    }
}
