using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    Button AddRotationHandle(Canvas overlay, Button shape, string kind, uint id, double x, double y, double width,
        double height, double rotation)
    {
        double radians = rotation * Math.PI / 180;
        Point center = new(x + width / 2, y + height / 2);
        double visualAngleOffset = ((shape.RenderTransform as RotateTransform)?.Angle ?? rotation) - rotation;
        double distance = height / 2 + 24;
        DrawingObjectButton handle = new() { Tag = "rotationHandle", Width = 20, Height = 20, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE7AD", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center } };
        AutomationProperties.SetName(handle, $"{(kind == "image" ? "画像" : "図形")} {id} を回転");
        ToolTipService.SetToolTip(handle, "回転（Shift で 15 度刻み）");
        Canvas.SetLeft(handle, center.X + distance * Math.Sin(radians) - 10);
        Canvas.SetTop(handle, center.Y - distance * Math.Cos(radians) - 10);
        Canvas.SetZIndex(handle, 2);
        overlay.Children.Add(handle);
        List<(Button Handle, double Left, double Top)> handles = overlay.Children.OfType<Button>()
            .Where(button => button.Tag is string tag && IsObjectHandle(tag))
            .Select(button => (button, Canvas.GetLeft(button), Canvas.GetTop(button))).ToList();
        bool rotating = false;
        double startAngle = 0;
        bool ShiftDown() => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) &
            Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        double AngleAt(Point point) => Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
        double DragAngle(Point point) => DrawingGeometry.SnapRotation(rotation + AngleAt(point) - startAngle, ShiftDown());
        void Preview(double angle)
        {
            shape.RenderTransform = new RotateTransform { Angle = angle + visualAngleOffset };
            double delta = (angle - rotation) * Math.PI / 180, cos = Math.Cos(delta), sin = Math.Sin(delta);
            foreach ((Button item, double left, double top) in handles)
            {
                double px = left + item.Width / 2 - center.X, py = top + item.Height / 2 - center.Y;
                Canvas.SetLeft(item, center.X + px * cos - py * sin - item.Width / 2);
                Canvas.SetTop(item, center.Y + px * sin + py * cos - item.Height / 2);
            }
        }
        async Task CommitAsync(double angle, bool focusHandle = false)
        {
            await CommitInputAsync();
            await ExecuteAsync("object.transform", new { kind, id, rotation = angle });
            dirty = true;
            await RefreshAsync();
            if (focusHandle) FocusDrawingHandle("rotationHandle");
            else FocusObject(kind, id);
        }
        handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            startAngle = AngleAt(e.GetCurrentPoint(overlay).Position);
            rotating = handle.CapturePointer(e.Pointer);
            e.Handled = true;
        }), true);
        handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!rotating) return;
            Preview(DragAngle(e.GetCurrentPoint(overlay).Position));
            e.Handled = true;
        }), true);
        handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (!rotating) return;
            double angle = DragAngle(e.GetCurrentPoint(overlay).Position);
            rotating = false;
            handle.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            if (Math.Abs(angle - rotation) < .01) { Preview(rotation); return; }
            await RunAsync(async () =>
            {
                try { await CommitAsync(angle); }
                finally { Preview(rotation); }
            });
        }), true);
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!rotating) return;
            rotating = false;
            Preview(rotation);
        };
        handle.KeyDown += async (_, e) =>
        {
            int delta = e.Key is VirtualKey.Right or VirtualKey.Up ? 1 : e.Key is VirtualKey.Left or VirtualKey.Down ? -1 : 0;
            if (delta == 0) return;
            e.Handled = true;
            await RunAsync(() => CommitAsync(DrawingGeometry.StepRotation(rotation, delta, ShiftDown()), true));
        };
        return handle;
    }
}
