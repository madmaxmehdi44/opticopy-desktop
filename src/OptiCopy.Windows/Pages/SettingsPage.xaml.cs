using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            TargetFpsBox.Value = settings.TargetFps;
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
            var fps = TargetFpsBox.Value;
            if (double.IsNaN(fps) || double.IsInfinity(fps))
                fps = 24.0;
            fps = Math.Clamp(fps, 5.0, 60.0);

            var settings = new OptiCopy.Data.AppSettings(fps, DarkModeSwitch.IsOn);
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
