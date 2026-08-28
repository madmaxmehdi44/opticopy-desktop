using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptiCopy.Data;
using OptiCopy.Windows.Diagnostics;

namespace OptiCopy.Windows.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await App.Settings.LoadAsync();
            DarkModeSwitch.IsOn = settings.DarkMode;
            StatusLabel.Text = "Settings loaded.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Loading settings failed.", ex);
            StatusLabel.Text = $"Settings error: {ex.Message}";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new AppSettings(DarkModeSwitch.IsOn);
            await App.Settings.SaveAsync(settings);

            if (App.MainWindow?.Content is FrameworkElement root)
                root.RequestedTheme = settings.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

            StatusLabel.Text = "Settings saved locally.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Saving settings failed.", ex);
            StatusLabel.Text = $"Settings error: {ex.Message}";
        }
    }
}
