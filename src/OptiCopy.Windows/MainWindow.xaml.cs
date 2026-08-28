using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OptiCopy.Windows.Pages;
using WinRT.Interop;

namespace OptiCopy.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;

        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow is not null)
            appWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 820));

        ContentFrame.Navigate(typeof(SenderPage));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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
