using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    void AddLineEndpointHandles(Canvas overlay, Button shape, uint id, double x, double y, double width, double height,
        double dx, double dy, bool flipV, double rotation)
    {
        Point Rotate(double px, double py)
        {
            (double px2, double py2) = DrawingGeometry.RotatePoint(x, y, width, height, px, py, rotation);
            return new Point(px2, py2);
        }
        Point[] endpoints = [Rotate(0, flipV ? height : 0), Rotate(width, flipV ? 0 : height)];
        for (int index = 0; index < 2; index++)
        {
            int endpoint = index;
            string handleTag = $"lineEndpoint:{endpoint}";
            DrawingObjectButton handle = new() { Tag = handleTag, Width = 10, Height = 10, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), Background = (Brush)RootGrid.Resources["MergeAccentBrush"] };
            AutomationProperties.SetName(handle, $"線 {id} の{(index == 0 ? "始点" : "終点")}");
            Canvas.SetLeft(handle, endpoints[index].X - 5);
            Canvas.SetTop(handle, endpoints[index].Y - 5);
            Canvas.SetZIndex(handle, 2);
            overlay.Children.Add(handle);
            Point origin = default;
            bool dragging = false;
            Line? preview = null;
            async Task CommitAsync(Point position, bool focusHandle = false)
            {
                Point fixedPoint = endpoints[1 - endpoint];
                (double left, double top, double w, double h, bool flipped) =
                    DrawingGeometry.LineBounds(position.X, position.Y, fixedPoint.X, fixedPoint.Y);
                await CommitInputAsync();
                await ExecuteAsync("object.move", new { kind = "shape", id, dx = dx + left - x, dy = dy + top - y,
                    width = w, height = h, flipV = flipped, rotation = 0 });
                dirty = true;
                await RefreshAsync();
                if (focusHandle)
                {
                    int newEndpoint = DrawingGeometry.MovedEndpointIndex(position.X, fixedPoint.X, endpoint);
                    FocusDrawingHandle($"lineEndpoint:{newEndpoint}");
                }
                else FocusObject("shape", id);
            }
            handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
                origin = e.GetCurrentPoint(overlay).Position;
                dragging = handle.CapturePointer(e.Pointer);
                if (dragging)
                {
                    preview = new Line { Stroke = (Brush)RootGrid.Resources["MergeAccentBrush"], StrokeThickness = 2,
                        IsHitTestVisible = false };
                    overlay.Children.Add(preview);
                    shape.Opacity = 0;
                }
                e.Handled = true;
            }), true);
            handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
            {
                if (!dragging || preview is null) return;
                Point point = e.GetCurrentPoint(overlay).Position;
                Point moved = new(endpoints[endpoint].X + point.X - origin.X, endpoints[endpoint].Y + point.Y - origin.Y);
                if (AltDown()) moved = new Point(SnapBoundary(moved.X, 48, columnWidths, viewColumns),
                    SnapBoundary(moved.Y, 25, rowHeights, viewRows));
                preview.X1 = endpoints[1 - endpoint].X; preview.Y1 = endpoints[1 - endpoint].Y;
                preview.X2 = moved.X; preview.Y2 = moved.Y;
                Canvas.SetLeft(handle, moved.X - 5); Canvas.SetTop(handle, moved.Y - 5);
                e.Handled = true;
            }), true);
            handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(async (_, e) =>
            {
                if (!dragging) return;
                Point point = e.GetCurrentPoint(overlay).Position;
                Point moved = new(endpoints[endpoint].X + point.X - origin.X, endpoints[endpoint].Y + point.Y - origin.Y);
                if (AltDown()) moved = new Point(SnapBoundary(moved.X, 48, columnWidths, viewColumns),
                    SnapBoundary(moved.Y, 25, rowHeights, viewRows));
                dragging = false;
                handle.ReleasePointerCapture(e.Pointer);
                if (preview is not null) overlay.Children.Remove(preview);
                shape.Opacity = 1;
                e.Handled = true;
                if (Math.Abs(moved.X - endpoints[endpoint].X) + Math.Abs(moved.Y - endpoints[endpoint].Y) < .01)
                {
                    Canvas.SetLeft(handle, endpoints[endpoint].X - 5);
                    Canvas.SetTop(handle, endpoints[endpoint].Y - 5);
                    return;
                }
                await RunAsync(async () =>
                {
                    try { await CommitAsync(moved); }
                    finally
                    {
                        Canvas.SetLeft(handle, endpoints[endpoint].X - 5);
                        Canvas.SetTop(handle, endpoints[endpoint].Y - 5);
                    }
                });
            }), true);
            handle.PointerCaptureLost += (_, _) =>
            {
                if (!dragging) return;
                dragging = false;
                if (preview is not null) overlay.Children.Remove(preview);
                shape.Opacity = 1;
                Canvas.SetLeft(handle, endpoints[endpoint].X - 5); Canvas.SetTop(handle, endpoints[endpoint].Y - 5);
            };
            handle.KeyDown += async (_, e) =>
            {
                double mx = e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0;
                double my = e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0;
                if (mx == 0 && my == 0) return;
                e.Handled = true;
                await RunAsync(() => CommitAsync(new Point(endpoints[endpoint].X + mx, endpoints[endpoint].Y + my), true));
            };
        }
        AddRotationHandle(overlay, shape, "shape", id, x, y, width, height, rotation);
    }
}
