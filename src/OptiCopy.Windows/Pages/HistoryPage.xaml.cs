using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptiCopy.Data;
using OptiCopy.Windows.Diagnostics;

namespace OptiCopy.Windows.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<HistoryItem> _items = [];

    public HistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _items;
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.History.ClearAsync();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Clearing transfer history failed.", ex);
            SummaryLabel.Text = $"History error: {ex.Message}";
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var entries = await App.History.GetAsync();
            _items.Clear();
            foreach (var entry in entries)
                _items.Add(HistoryItem.From(entry));

            SummaryLabel.Text = _items.Count == 0
                ? "No transfer sessions recorded."
                : $"{_items.Count.ToString("N0", CultureInfo.InvariantCulture)} session(s) • newest first";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Loading transfer history failed.", ex);
            SummaryLabel.Text = $"History error: {ex.Message}";
        }
    }

    private sealed record HistoryItem(
        string DirectionLabel,
        string StatusLabel,
        string FileName,
        string Details,
        string TimestampLabel,
        string HashLabel)
    {
        public static HistoryItem From(TransferHistoryEntry entry)
        {
            var direction = entry.Direction == TransferDirection.Send ? "SEND" : "RECEIVE";
            var status = entry.Status.ToString().ToUpperInvariant();
            var details = $"{FormatBytes(entry.OriginalSize)} original • {FormatBytes(entry.TransmittedSize)} wire • " +
                          $"{entry.Frames.ToString("N0", CultureInfo.InvariantCulture)} frames";
            var timestamp = entry.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var hash = string.IsNullOrWhiteSpace(entry.Sha256)
                ? ""
                : $"SHA-256 {entry.Sha256[..Math.Min(12, entry.Sha256.Length)]}…";

            return new HistoryItem(direction, status, entry.FileName, details, timestamp, hash);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            var value = (double)bytes;
            string[] units = ["KB", "MB", "GB", "TB"];
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
        }
    }
}
