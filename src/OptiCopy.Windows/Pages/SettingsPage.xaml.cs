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
            AutoStartCameraSwitch.IsOn = settings.AutoStartCamera;
            RememberLastCameraSwitch.IsOn = settings.RememberLastCamera;
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

            var current = await App.Settings.LoadAsync();
            var settings = current with
            {
                AutoStartCamera = AutoStartCameraSwitch.IsOn,
                RememberLastCamera = RememberLastCameraSwitch.IsOn,
                TargetFps = fps,
                DarkMode = DarkModeSwitch.IsOn
            };

            await App.Settings.SaveAsync(settings);
            StatusLabel.Text = "Settings saved locally.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Saving settings failed.", ex);
            StatusLabel.Text = $"Settings error: {ex.Message}";
        }
    }
}
