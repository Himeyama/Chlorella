using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    void BindImageInteraction(Canvas overlay, Button button, uint id, JsonElement anchor, double x, double y)
    {
        double dx = anchor.GetProperty("dx").GetDouble(), dy = anchor.GetProperty("dy").GetDouble();
        Windows.Foundation.Point origin = default;
        bool moving = false;
        void SelectImage()
        {
            selectedShape = null; selectedImage = id; ShapeTab.Visibility = Visibility.Collapsed;
            if (RibbonSelector.SelectedItem == ShapeTab) RibbonSelector.SelectedItem = RibbonSelector.Items[1];
        }
        button.GotFocus += (_, _) => SelectImage();
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            SelectImage();
            button.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"];
            button.BorderThickness = new Thickness(2);
            button.Focus(FocusState.Pointer);
            origin = e.GetCurrentPoint(overlay).Position;
            moving = button.CapturePointer(e.Pointer);
            e.Handled = true;
        }), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!moving) return;
            Windows.Foundation.Point point = e.GetCurrentPoint(overlay).Position;
            double nx = x + point.X - origin.X, ny = y + point.Y - origin.Y;
            if (AltDown()) { nx = SnapBoundary(nx, 48, columnWidths, viewColumns); ny = SnapBoundary(ny, 25, rowHeights, viewRows); }
            Canvas.SetLeft(button, nx); Canvas.SetTop(button, ny);
            e.Handled = true;
        }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (!moving) return;
            Windows.Foundation.Point point = e.GetCurrentPoint(overlay).Position;
            double nx = x + point.X - origin.X, ny = y + point.Y - origin.Y;
            if (AltDown()) { nx = SnapBoundary(nx, 48, columnWidths, viewColumns); ny = SnapBoundary(ny, 25, rowHeights, viewRows); }
            moving = false; button.ReleasePointerCapture(e.Pointer); e.Handled = true;
            await RunAsync(async () =>
            {
                bool committed = false;
                try
                {
                    await CommitInputAsync(); committed = true;
                    if (Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) > 1)
                    {
                        await ExecuteAsync("object.move", new { kind = "image", id, dx = dx + nx - x, dy = dy + ny - y });
                        dirty = true;
                    }
                }
                finally { if (committed) { await RefreshAsync(); FocusObject("image", id); } else { Canvas.SetLeft(button, x); Canvas.SetTop(button, y); } }
            });
        }), true);
        button.PointerCaptureLost += (_, _) =>
        {
            if (!moving) return;
            moving = false;
            Canvas.SetLeft(button, x); Canvas.SetTop(button, y);
        };
        button.KeyDown += async (_, e) =>
        {
            bool control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (control && e.Key is VirtualKey.C or VirtualKey.X)
            {
                e.Handled = true; CopyObject("image", id, e.Key == VirtualKey.X); return;
            }
            if (e.Key == VirtualKey.Delete)
            {
                e.Handled = true;
                await RunAsync(async () => { await ExecuteAsync("object.delete", new { kind = "image", id }); selectedImage = null; dirty = true; await RefreshAsync(); FocusCell(activeCell); });
                return;
            }
            double mx = e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0;
            double my = e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0;
            if (mx == 0 && my == 0) return;
            e.Handled = true; SelectImage();
            await RunAsync(async () => { await CommitInputAsync(); await ExecuteAsync("object.move", new { kind = "image", id, dx = dx + mx, dy = dy + my }); dirty = true; await RefreshAsync(); FocusObject("image", id); });
        };
    }

    async Task EditImageBoundsAsync(uint id, JsonElement anchor)
    {
        StackPanel panel = new() { Spacing = 8 };
        TextBox[] inputs = [new(), new(), new(), new()];
        string[] labels = ["横位置", "縦位置", "幅", "高さ"];
        string[] fields = ["dx", "dy", "width", "height"];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].Header = labels[i];
            inputs[i].Text = anchor.GetProperty(fields[i]).GetDouble().ToString(CultureInfo.CurrentCulture);
            panel.Children.Add(inputs[i]);
        }
        ContentDialog dialog = new() { XamlRoot = RootGrid.XamlRoot, Title = "画像の位置とサイズ", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        double[] values = new double[4];
        for (int i = 0; i < values.Length; i++)
            if (!double.TryParse(inputs[i].Text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[i]) || !double.IsFinite(values[i])) throw new FormatException("位置とサイズには数値を入力してください。");
        await CommitInputAsync();
        await ExecuteAsync("object.move", new { kind = "image", id, dx = values[0], dy = values[1], width = values[2], height = values[3] });
        dirty = true; await RefreshAsync();
    }
}
