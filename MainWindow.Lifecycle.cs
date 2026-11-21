using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace Chlorella;

public sealed partial class MainWindow
{
    async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    async Task<bool> ConfirmDiscardAsync()
    {
        ContentDialog dialog = new()
        {
            Title = "保存していない変更を破棄しますか？",
            Content = "最後に保存してからの変更は失われます。",
            PrimaryButtonText = "破棄",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!dirty || confirmedClose) return;
        args.Cancel = true;
        await RunAsync(async () =>
        {
            if (await ConfirmDiscardAsync()) { confirmedClose = true; Close(); }
        });
    }
    void Window_Closed(object sender, WindowEventArgs args) { }
    void SetInitialWindowSize()
    {
        uint dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        double scale = dpi > 0 ? dpi / 96.0 : 1;
        int width = (int)Math.Round(960 * scale);
        int height = (int)Math.Round(640 * scale);
        DisplayArea? display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (display is null) { AppWindow.Resize(new SizeInt32(width, height)); return; }
        RectInt32 work = display.WorkArea;
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }
    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);
}
