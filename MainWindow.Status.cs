using Microsoft.UI.Xaml;

namespace Chlorella;

public sealed partial class MainWindow
{
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    void SetStatus(string message)
    {
        statusTimer.Stop();
        statusTimer.Tick -= StatusTimer_Tick;
        StatusText.Text = message;
        StatusCard.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (message.Length == 0) return;
        statusTimer.Tick += StatusTimer_Tick;
        statusTimer.Start();
    }

    void StatusTimer_Tick(object? sender, object e)
    {
        statusTimer.Stop();
        StatusText.Text = "";
        StatusCard.Visibility = Visibility.Collapsed;
    }
    void DismissStatus_Click(object sender, RoutedEventArgs e) => SetStatus("");
}
