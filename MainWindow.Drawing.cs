using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    void ClearObjectSelectionVisuals()
    {
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not Canvas overlay) continue;
            for (int i = overlay.Children.Count - 1; i >= 0; i--)
            {
                if (overlay.Children[i] is DrawingObjectButton { Tag: string tag } && IsObjectHandle(tag)) overlay.Children.RemoveAt(i);
                else if (overlay.Children[i] is DrawingObjectButton button) button.BorderThickness = new Thickness(0);
            }
        }
    }

    static bool IsObjectHandle(string tag) => tag is "resizeHandle" or "rotationHandle" || tag.StartsWith("lineEndpoint:");

    void FocusDrawingHandle(string tag)
    {
        foreach (UIElement element in CellGrid.Children)
            if (element is Canvas overlay)
                foreach (UIElement child in overlay.Children)
                    if (child is Button button && Equals(button.Tag, tag))
                    {
                        button.Focus(FocusState.Programmatic);
                        return;
                    }
    }

    async Task AddDrawingObjectsAsync(JsonElement view)
    {
        Canvas overlay = new() { Tag = "drawing-overlay" };
        overlay.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(48, 25,
            columnWidths.Take(viewColumns).Sum(), rowHeights.Take(viewRows).Sum())
        };
        Grid.SetRowSpan(overlay, viewRows + 1);
        Grid.SetColumnSpan(overlay, viewColumns + 1);
        Canvas.SetZIndex(overlay, 1);
        JsonElement colors = view.GetProperty("theme").GetProperty("colors");
        foreach (JsonElement shape in view.GetProperty("shapes").EnumerateArray())
        {
            if (!TryObjectPosition(shape.GetProperty("anchor"), out double x, out double y, out double width, out double height)) continue;
            string kind = shape.GetProperty("kind").GetString() ?? "";
            Microsoft.UI.Xaml.Media.Brush transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Microsoft.UI.Xaml.Media.Brush fill = ColorBrush(shape.GetProperty("fill"), colors) ?? transparent;
            Microsoft.UI.Xaml.Media.Brush line = ColorBrush(shape.GetProperty("line"), colors) ?? transparent;
            bool lineFlip = shape.GetProperty("flipV").GetBoolean();
            (double lineLength, double lineAngle) = DrawingGeometry.LineDirection(width, height, lineFlip);
            Microsoft.UI.Xaml.Shapes.Shape? visual = kind switch
            {
                "Rectangle" => new Microsoft.UI.Xaml.Shapes.Rectangle(),
                "RoundedRectangle" => new Microsoft.UI.Xaml.Shapes.Rectangle { RadiusX = 12, RadiusY = 12 },
                "Ellipse" => new Microsoft.UI.Xaml.Shapes.Ellipse(),
                "Triangle" => new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points =
                    new Microsoft.UI.Xaml.Media.PointCollection { new(width / 2, 0), new(width, height), new(0, height) }
                },
                "Diamond" => new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points =
                    new Microsoft.UI.Xaml.Media.PointCollection { new(width / 2, 0), new(width, height / 2), new(width / 2,
                        height), new(0, height / 2) }
                },
                "Pentagon" => new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points =
                    new Microsoft.UI.Xaml.Media.PointCollection { new(width / 2, 0), new(width, height * .38),
                        new(width * .8, height), new(width * .2, height), new(0, height * .38) }
                },
                "Hexagon" => new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points =
                    new Microsoft.UI.Xaml.Media.PointCollection { new(width * .25, 0), new(width * .75, 0), new(width,
                        height / 2), new(width * .75, height), new(width * .25, height), new(0, height / 2) }
                },
                "Line" => new Microsoft.UI.Xaml.Shapes.Line { X1 = 0, Y1 = .5, X2 = lineLength, Y2 = .5 },
                "Arrow" => new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points =
                    new Microsoft.UI.Xaml.Media.PointCollection { new(0, height * .35), new(width * .65, height * .35),
                        new(width * .65, 0), new(width, height / 2), new(width * .65, height), new(width * .65,
                            height * .65), new(0, height * .65) }
                },
                _ => null,
            };
            if (visual is null) continue;
            visual.Width = kind == "Line" ? lineLength : width;
            visual.Height = kind == "Line" ? 1 : Math.Max(1, height);
            visual.Fill = fill;
            visual.Stroke = line;
            visual.StrokeThickness = 1;
            uint id = shape.GetProperty("id").GetUInt32();
            JsonElement anchor = shape.GetProperty("anchor");
            double dx = anchor.GetProperty("dx").GetDouble(), dy = anchor.GetProperty("dy").GetDouble();
            Grid shapeContent = new() { IsHitTestVisible = false };
            shapeContent.Children.Add(visual);
            string shapeText = shape.GetProperty("text").GetString() ?? "";
            if (shapeText.Length > 0)
            {
                JsonElement format = shape.GetProperty("format");
                JsonElement textColor = format.GetProperty("text_color");
                TextBlock label = new()
                {
                    Text = shapeText,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = format.GetProperty("bold").GetBoolean() ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    FontStyle = format.GetProperty("italic").GetBoolean() ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                    TextDecorations = format.GetProperty("underline").GetBoolean() ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
                    FontSize = format.GetProperty("size").GetDouble(),
                    Foreground = ColorBrush(textColor, colors) ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                };
                shapeContent.Children.Add(label);
            }
            DrawingObjectButton objectButton = new()
            {
                Content = shapeContent,
                Width = Math.Max(24, kind == "Line" ? lineLength : width),
                Height = kind == "Line" ? 24 : Math.Max(24, height),
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = selectedShape == id ? (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"] : null,
                BorderThickness = new Thickness(selectedShape == id && kind != "Line" ? 2 : 0)
            };
            objectButton.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
            objectButton.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform
                { Angle = shape.GetProperty("rotation").GetDouble() + (kind == "Line" ? lineAngle : 0) };
            double hitOffsetX = kind == "Line" ? (objectButton.Width - width) / 2 : 0;
            double hitOffsetY = kind == "Line" ? (objectButton.Height - height) / 2 : 0;
            AutomationProperties.SetName(objectButton, $"図形 {id} {kind}");
            objectButton.ContextFlyout = ObjectMenu("shape", id, shapeText, kind != "Line", shape);
            objectButton.GotFocus += (_, _) =>
            {
                selectedImage = null;
                selectedShape = id;
                ShapeTab.Visibility = Visibility.Visible;
            }
;
            Windows.Foundation.Point origin = default;
            bool moving = false;
            objectButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (!e.GetCurrentPoint(objectButton).Properties.IsLeftButtonPressed) return;
                ClearObjectSelectionVisuals();
                selectedImage = null;
                selectedShape = id;
                ShapeTab.Visibility = Visibility.Visible;
                if (kind == "Line") AddLineEndpointHandles(overlay, objectButton, id, x, y, width, height, dx, dy,
                    shape.GetProperty("flipV").GetBoolean(), shape.GetProperty("rotation").GetDouble());
                else AddObjectResizeHandles(overlay, objectButton, "shape", id, x, y, Math.Max(1, width), Math.Max(1, height), dx, dy);
                objectButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"];
                objectButton.BorderThickness = new Thickness(kind == "Line" ? 0 : 2);
                objectButton.Focus(FocusState.Pointer);
                origin = e.GetCurrentPoint(overlay).Position;
                moving = objectButton.CapturePointer(e.Pointer);
                e.Handled = true;
            }), true);
            objectButton.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
            {
                if (!moving) return;
                Windows.Foundation.Point point = e.GetCurrentPoint(overlay).Position;
                double nx = x + point.X - origin.X, ny = y + point.Y - origin.Y;
                if (AltDown())
                {
                    nx = SnapBoundary(nx, 48, columnWidths, viewColumns);
                    ny = SnapBoundary(ny, 25, rowHeights, viewRows);
                }
                Canvas.SetLeft(objectButton, nx - hitOffsetX);
                Canvas.SetTop(objectButton, ny - hitOffsetY);
                e.Handled = true;
            }), true);
            objectButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(async (_, e) =>
            {
                if (!moving) return;
                Windows.Foundation.Point point = e.GetCurrentPoint(overlay).Position;
                double nx = x + point.X - origin.X, ny = y + point.Y - origin.Y;
                if (AltDown())
                {
                    nx = SnapBoundary(nx, 48, columnWidths, viewColumns);
                    ny = SnapBoundary(ny, 25, rowHeights, viewRows);
                }
                moving = false;
                objectButton.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
                await RunAsync(async () =>
                {
                    if (Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) <= 1 && !inputPending)
                    {
                        Canvas.SetLeft(objectButton, x - hitOffsetX);
                        Canvas.SetTop(objectButton, y - hitOffsetY);
                        return;
                    }
                    bool committed = false;
                    try
                    {
                        await CommitInputAsync();
                        committed = true;
                        if (Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) > 1)
                        {
                            await ExecuteAsync("object.move", new { kind = "shape", id, dx = dx + nx - x, dy = dy + ny - y });
                            dirty = true;
                        }
                    }
                    finally
                    {
                        if (committed)
                        {
                            await RefreshAsync();
                            FocusShape(id);
                        }
                        else
                        {
                            Canvas.SetLeft(objectButton, x - hitOffsetX);
                            Canvas.SetTop(objectButton, y - hitOffsetY);
                        }

                    }
                });
            }), true);
            objectButton.PointerCaptureLost += (_, _) =>
            {
                if (!moving) return;
                moving = false;
                Canvas.SetLeft(objectButton, x - hitOffsetX);
                Canvas.SetTop(objectButton, y - hitOffsetY);
            }
;
            objectButton.KeyDown += async (_, e) =>
            {
                if (await HandleObjectShortcutAsync(e, "shape", id)) return;
                double mx = e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0;
                double my = e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0;
                if (mx == 0 && my == 0) return;
                e.Handled = true;
                selectedShape = id;
                ShapeTab.Visibility = Visibility.Visible;
                await RunAsync(async () =>
                {
                    await ExecuteAsync("object.move", new { kind = "shape", id, dx = dx + mx, dy = dy + my });
                    dirty = true;
                    await RefreshAsync();
                    FocusShape(id);
                });
            }
;
            objectButton.Tag = $"shape:{id}";
            Canvas.SetLeft(objectButton, x - hitOffsetX);
            Canvas.SetTop(objectButton, y - hitOffsetY);
            overlay.Children.Add(objectButton);
            if (selectedShape == id)
            {
                if (kind == "Line") AddLineEndpointHandles(overlay, objectButton, id, x, y, width, height, dx, dy,
                    shape.GetProperty("flipV").GetBoolean(), shape.GetProperty("rotation").GetDouble());
                else AddObjectResizeHandles(overlay, objectButton, "shape", id, x, y, Math.Max(1, width), Math.Max(1, height), dx, dy);
            }
        }
        foreach (JsonElement picture in view.GetProperty("images").EnumerateArray())
        {
            if (!TryObjectPosition(picture.GetProperty("anchor"), out double x, out double y, out double width,
                out double height)) continue;
            try
            {
                JsonElement data = await SendAsync("sheet.picture", new { id = picture.GetProperty("id").GetUInt32() });
                byte[] bytes = Convert.FromBase64String(data.GetProperty("base64").GetString() ?? "");
                using Windows.Storage.Streams.InMemoryRandomAccessStream stream = new();
                using (Windows.Storage.Streams.DataWriter writer = new(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap = new();
                await bitmap.SetSourceAsync(stream);
                double cropLeft = picture.GetProperty("cropLeft").GetDouble(), cropTop = picture.GetProperty("cropTop").GetDouble(),
                    cropRight = picture.GetProperty("cropRight").GetDouble(), cropBottom = picture.GetProperty("cropBottom").GetDouble();
                double imageWidth = width / (1 - cropLeft - cropRight), imageHeight = height / (1 - cropTop - cropBottom);
                Microsoft.UI.Xaml.Controls.Image image = new()
                {
                    Source = bitmap,
                    Width = imageWidth,
                    Height = imageHeight,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill
                };
                Canvas imageViewport = new() { Width = width, Height = height, IsHitTestVisible = false,
                    Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) } };
                Canvas.SetLeft(image, -cropLeft * imageWidth);
                Canvas.SetTop(image, -cropTop * imageHeight);
                imageViewport.Children.Add(image);
                AutomationProperties.SetName(image, picture.GetProperty("alt").GetString() ?? "画像");
                uint id = picture.GetProperty("id").GetUInt32();
                DrawingObjectButton imageButton = new()
                {
                    Content = imageViewport,
                    Width = Math.Max(1, width),
                    Height = Math.Max(1, height),
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderBrush = selectedImage == id ? (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"] : null,
                    BorderThickness = new Thickness(selectedImage == id ? 2 : 0),
                    Tag = $"image:{id}",
                    ContextFlyout = ObjectMenu("image", id, "", false, picture, bitmap)
                };
                imageButton.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
                imageButton.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = picture.GetProperty("rotation").GetDouble() };
                AutomationProperties.SetName(imageButton, picture.GetProperty("alt").GetString() ?? "画像");
                BindImageInteraction(overlay, imageButton, id, picture.GetProperty("anchor"), x, y);
                Canvas.SetLeft(imageButton, x);
                Canvas.SetTop(imageButton, y);
                overlay.Children.Add(imageButton);
                if (selectedImage == id) AddObjectResizeHandles(overlay, imageButton, "image", id, x, y, Math.Max(1, width),
                    Math.Max(1, height), picture.GetProperty("anchor").GetProperty("dx").GetDouble(),
                        picture.GetProperty("anchor").GetProperty("dy").GetDouble());
            }
            catch (Exception ex)
            {
                SetStatus($"画像を表示できません: {ex.Message}");
            }
        }
        CellGrid.Children.Add(overlay);
    }
    MenuFlyout ObjectMenu(string kind, uint id, string text, bool canEditText, JsonElement drawing,
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? bitmap = null)
    {
        MenuFlyout menu = new();
        void Add(string label, string glyph, Func<Task> action)
        {
            MenuFlyoutItem item = new() { Text = label, Icon = new FontIcon { Glyph = glyph } };
            item.Click += async (_, _) => await RunAsync(action);
            menu.Items.Add(item);
        }
        Add("切り取り", "\uE8C6", () =>
        {
            CopyObject(kind, id, true);
            return Task.CompletedTask;
        });
        Add("コピー", "\uE8C8", () =>
        {
            CopyObject(kind, id, false);
            return Task.CompletedTask;
        });
        Add("複製", "\uE8C8", async () =>
        {
            await ExecuteAsync("object.duplicate", new { kind, id });
            dirty = true;
            await RefreshAsync();
        });
        Add("削除", "\uE74D", async () =>
        {
            await ExecuteAsync("object.delete", new { kind, id });
            selectedShape = null;
            selectedImage = null;
            ShapeTab.Visibility = Visibility.Collapsed;
            dirty = true;
            await RefreshAsync();
        });
        Add("回転…", "\uE7AD", () => EditObjectRotationAsync(kind, id, drawing.GetProperty("rotation").GetDouble()));
        if (kind == "image")
        {
            Add("トリミング…", "\uE7A8", () => EditImageCropAsync(id, drawing, bitmap));
            Add("位置とサイズ…", "\uE8A3", () => EditImageBoundsAsync(id, drawing.GetProperty("anchor")));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("最前面へ移動（同種）", "\uE70E", async () =>
        {
            await ExecuteAsync("arrange.bringForward", new { kind, id, toFront = true });
            dirty = true;
            await RefreshAsync();
        });
        Add("前面へ移動（同種）", "\uE74A", async () =>
        {
            await ExecuteAsync("arrange.bringForward", new { kind, id });
            dirty = true;
            await RefreshAsync();
        });
        Add("背面へ移動（同種）", "\uE74B", async () =>
        {
            await ExecuteAsync("arrange.sendBackward", new { kind, id });
            dirty = true;
            await RefreshAsync();
        });
        Add("最背面へ移動（同種）", "\uE70D", async () =>
        {
            await ExecuteAsync("arrange.sendBackward", new { kind, id, toBack = true });
            dirty = true;
            await RefreshAsync();
        });
        if (canEditText)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("テキストの編集", "\uE70F", () => EditShapeTextAsync(id, drawing));
        }
        return menu;
    }
    void CopyObject(string kind, uint id, bool cut)
    {
        copiedCells = null;
        DrawCopyOutline();
        objectClipboard = (kind, id, currentSheet, cut);
        objectClipboardToken = Guid.NewGuid().ToString();
        DataPackage package = new();
        package.SetData("Chlorella.ObjectReference", objectClipboardToken);
        Clipboard.SetContent(package);
        SetStatus("コピー先のセルを選択し Ctrl+V で貼り付けてください（同じシート内）。");
    }
    static bool AltDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    static double SnapBoundary(double coordinate, double start, double[] sizes, int count)
    {
        double edge = start, closest = start;
        for (int i = 0; i < count; i++)
        {
            edge += sizes[i];
            if (Math.Abs(edge - coordinate) < Math.Abs(closest - coordinate)) closest = edge;
        }
        return closest;
    }
    void AddObjectResizeHandles(Canvas overlay, Button shape, string kind, uint id, double x, double y, double width,
        double height, double dx, double dy)
    {
        double rotation = (shape.RenderTransform as Microsoft.UI.Xaml.Media.RotateTransform)?.Angle ?? 0;
        double radians = rotation * Math.PI / 180, cosine = Math.Cos(radians), sine = Math.Sin(radians);
        List<(Button Button, int X, int Y)> handles = [];
        Button? rotationHandle = null;
        void PlaceHandles(double left, double top, double w, double h)
        {
            foreach ((Button button, int hx, int hy) in handles)
            {
                double px = hx * w / 2, py = hy * h / 2;
                Canvas.SetLeft(button, left + w / 2 + px * cosine - py * sine - 5);
                Canvas.SetTop(button, top + h / 2 + px * sine + py * cosine - 5);
            }
            if (rotationHandle is not null)
            {
                double distance = h / 2 + 24;
                Canvas.SetLeft(rotationHandle, left + w / 2 + distance * sine - 10);
                Canvas.SetTop(rotationHandle, top + h / 2 - distance * cosine - 10);
            }
        }
        foreach ((int hx, int hy, string label) in new[] { (-1, -1, "左上"), (0, -1, "上"), (1, -1, "右上"),
                     (-1, 0, "左"), (1, 0, "右"), (-1, 1, "左下"), (0, 1, "下"), (1, 1, "右下") })
        {
            DrawingObjectButton handle = new()
            {
                Tag = "resizeHandle",
                Width = 10,
                Height = 10,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Background = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"]
            };
            Canvas.SetZIndex(handle, 1);
            AutomationProperties.SetName(handle, $"{(kind == "image" ? "画像" : "図形")} {id} の{label}のサイズ変更");
            Windows.Foundation.Point origin = default;
            bool resizing = false;
            (double Left, double Top, double Width, double Height) Calculate(Windows.Foundation.Point point)
            {
                double left = x, top = y, right = x + width, bottom = y + height;
                double deltaX = point.X - origin.X, deltaY = point.Y - origin.Y;
                if (AltDown())
                {
                    double handleX = x + width / 2 + hx * width / 2 * cosine - hy * height / 2 * sine;
                    double handleY = y + height / 2 + hx * width / 2 * sine + hy * height / 2 * cosine;
                    deltaX = SnapBoundary(handleX + deltaX, 48, columnWidths, viewColumns) - handleX;
                    deltaY = SnapBoundary(handleY + deltaY, 25, rowHeights, viewRows) - handleY;
                }
                double localX = deltaX * cosine + deltaY * sine, localY = -deltaX * sine + deltaY * cosine;
                if (hx < 0) left += localX;
                else if (hx > 0) right += localX;
                if (hy < 0) top += localY;
                else if (hy > 0) bottom += localY;
                double w = Math.Max(4, right - left), h = Math.Max(4, bottom - top);
                if (hx != 0 && hy != 0)
                {
                    double scaleX = w / width, scaleY = h / height;
                    double scale = Math.Max(4 / Math.Min(width, height), Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY);
                    w = width * scale;
                    h = height * scale;
                }
                left = hx < 0 ? x + width - w : x;
                top = hy < 0 ? y + height - h : y;
                double centerX = left + w / 2 - (x + width / 2), centerY = top + h / 2 - (y + height / 2);
                left = x + width / 2 + centerX * cosine - centerY * sine - w / 2;
                top = y + height / 2 + centerX * sine + centerY * cosine - h / 2;
                return (left, top, w, h);
            }
            handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
                origin = e.GetCurrentPoint(overlay).Position;
                resizing = handle.CapturePointer(e.Pointer);
                e.Handled = true;
            }), true);
            handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
            {
                if (!resizing) return;
                (double Left, double Top, double Width, double Height) bounds = Calculate(e.GetCurrentPoint(overlay).Position);
                Canvas.SetLeft(shape, bounds.Left);
                Canvas.SetTop(shape, bounds.Top);
                Microsoft.UI.Xaml.Media.TransformGroup transforms = new();
                transforms.Children.Add(new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = bounds.Width / width, ScaleY = bounds.Height / height });
                transforms.Children.Add(new Microsoft.UI.Xaml.Media.RotateTransform { Angle = rotation });
                shape.RenderTransform = transforms;
                Canvas.SetLeft(shape, bounds.Left + (bounds.Width - width) / 2);
                Canvas.SetTop(shape, bounds.Top + (bounds.Height - height) / 2);
                PlaceHandles(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
                e.Handled = true;
            }), true);
            handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(async (_, e) =>
            {
                if (!resizing) return;
                (double Left, double Top, double Width, double Height) bounds = Calculate(e.GetCurrentPoint(overlay).Position);
                resizing = false;
                handle.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
                await RunAsync(async () =>
                {
                    bool committed = false;
                    try
                    {
                        await CommitInputAsync();
                        committed = true;
                        await ExecuteAsync("object.move", new
                        {
                            kind,
                            id,
                            dx = dx + bounds.Left - x,
                            dy = dy + bounds.Top - y,
                            width = bounds.Width,
                            height = bounds.Height
                        });
                        dirty = true;
                    }
                    finally
                    {
                        if (committed)
                        {
                            await RefreshAsync();
                            FocusObject(kind, id);
                        }
                        else
                        {
                            Canvas.SetLeft(shape, x);
                            Canvas.SetTop(shape, y);
                            shape.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = rotation };
                            PlaceHandles(x, y, width, height);
                        }
                    }
                });
            }), true);
            handle.PointerCaptureLost += (_, _) =>
            {
                if (!resizing) return;
                resizing = false;
                Canvas.SetLeft(shape, x);
                Canvas.SetTop(shape, y);
                shape.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = rotation };
                PlaceHandles(x, y, width, height);
            }
;
            handle.KeyDown += async (_, e) =>
            {
                double mx = e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0;
                double my = e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0;
                if ((mx == 0 && my == 0) ||
                    ((hx == 0 || Math.Abs(mx * cosine + my * sine) < 1e-9) &&
                     (hy == 0 || Math.Abs(-mx * sine + my * cosine) < 1e-9))) return;
                e.Handled = true;
                origin = new Windows.Foundation.Point(0, 0);
                (double Left, double Top, double Width, double Height) bounds = Calculate(new Windows.Foundation.Point(mx, my));
                await RunAsync(async () =>
                {
                    await ExecuteAsync("object.move", new
                    {
                        kind,
                        id,
                        dx = dx + bounds.Left - x,
                        dy = dy + bounds.Top - y,
                        width = bounds.Width,
                        height = bounds.Height
                    });
                    dirty = true;
                    await RefreshAsync();
                    FocusObject(kind, id);
                });
            }
;
            handles.Add((handle, hx, hy));
            overlay.Children.Add(handle);
        }
        PlaceHandles(x, y, width, height);
        rotationHandle = AddRotationHandle(overlay, shape, kind, id, x, y, width, height, rotation);
    }
    void FocusShape(uint id) => FocusObject("shape", id);
    void FocusObject(string kind, uint id)
    {
        foreach (UIElement element in CellGrid.Children)
            if (element is Canvas canvas)
                foreach (UIElement child in canvas.Children)
                    if (child is Button button && Equals(button.Tag, $"{kind}:{id}"))
                    {
                        button.Focus(FocusState.Programmatic);
                        return;
                    }
    }
    bool TryObjectPosition(JsonElement anchor, out double x, out double y, out double width, out double height)
    {
        JsonElement cell = anchor.GetProperty("cell");
        int row = cell.GetProperty("row").GetInt32() - (firstRow - 1), col = cell.GetProperty("col").GetInt32() - firstColumn;
        width = Math.Clamp(anchor.GetProperty("width").GetDouble(), 0, 10000);
        height = Math.Clamp(anchor.GetProperty("height").GetDouble(), 0, 10000);
        x = 48;
        y = 25;
        if (anchor.TryGetProperty("viewportX", out JsonElement viewportX) && anchor.TryGetProperty("viewportY", out JsonElement viewportY))
        {
            x += viewportX.GetDouble();
            y += viewportY.GetDouble();
            // The engine includes rotated bounds when deciding which objects intersect the viewport.
            return true;
        }
        if (row < 0 || col < 0 || row >= viewRows || col >= viewColumns) return false;
        for (int i = 0; i < col; i++) x += columnWidths[i];
        for (int i = 0; i < row; i++) y += rowHeights[i];
        x += anchor.GetProperty("dx").GetDouble();
        y += anchor.GetProperty("dy").GetDouble();
        return true;
    }
    static Microsoft.UI.Xaml.Media.Brush? ColorBrush(JsonElement value, JsonElement themeColors)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        uint rgb;
        double tint = 0;
        if (value.TryGetProperty("Rgb", out JsonElement explicitColor)) rgb = explicitColor.GetUInt32();
        else if (value.TryGetProperty("Theme", out JsonElement theme) && theme.GetArrayLength() == 2)
        {
            int index = theme[0].GetInt32();
            if (index < 0 || index >= themeColors.GetArrayLength()) return null;
            rgb = themeColors[index].GetUInt32();
            tint = Math.Clamp(theme[1].GetDouble() / 1000, -1, 1);
        }
        else return null;
        double r = ((rgb >> 16) & 255) / 255.0, g = ((rgb >> 8) & 255) / 255.0, b = (rgb & 255) / 255.0;
        if (tint != 0)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double lightness = (max + min) / 2, delta = max - min;
            double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
            double hue = delta == 0 ? 0 : max == r ? ((g - b) / delta + 6) % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            lightness = tint < 0 ? lightness * (1 + tint) : lightness * (1 - tint) + tint;
            double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = chroma * (1 - Math.Abs(hue % 2 - 1)), m = lightness - chroma / 2;
            (r, g, b) = hue switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x,
                chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };
            r += m;
            g += m;
            b += m;
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,
            (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));
    }
    void UpdateMergeButton(JsonElement merges)
    {
        (int row, int col) = ParseCell(activeCell);
        activeCellMerged = merges.EnumerateArray().Any(merge =>
        {
            JsonElement range = merge.GetProperty("range");
            JsonElement start = range.GetProperty("start"), end = range.GetProperty("end");
            return row - 1 >= start.GetProperty("row").GetInt32() && row - 1 <= end.GetProperty("row").GetInt32() &&
                col >= start.GetProperty("col").GetInt32() && col <= end.GetProperty("col").GetInt32();
        });
        MergeButton.IsChecked = activeCellMerged;
        AutomationProperties.SetName(MergeButton, activeCellMerged ? "セルの結合を解除" : "セルを結合");
    }
    void UpdateWrapButton(JsonElement rows, JsonElement styles, JsonElement merges)
    {
        (int row, int col) = ParseCell(activeCell);
        int rowIndex = row - firstRow, colIndex = col - firstColumn;
        if (rowIndex < 0 || rowIndex >= viewRows || colIndex < 0 || colIndex >= viewColumns) return;
        string styleKey = rows[rowIndex][colIndex].GetProperty("style").GetRawText();
        foreach (JsonElement merge in merges.EnumerateArray())
        {
            JsonElement range = merge.GetProperty("range"), start = range.GetProperty("start"), end = range.GetProperty("end");
            if (row - 1 >= start.GetProperty("row").GetInt32() && row - 1 <= end.GetProperty("row").GetInt32() &&
                col >= start.GetProperty("col").GetInt32() && col <= end.GetProperty("col").GetInt32())
            {
                styleKey = merge.GetProperty("style").GetRawText();
                break;
            }
        }
        JsonElement fullStyle = styles.GetProperty(styleKey);
        if (fullStyle.TryGetProperty("numberFormat", out JsonElement numberFormat))
        {
            string? format = numberFormat.GetString();
            updatingNumberFormat = true;
            try
            {
                NumberFormatInput.SelectedItem = NumberFormatInput.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                    item.Tag as string == format);
            }
            finally
            {
                updatingNumberFormat = false;
            }
        }
        JsonElement font = fullStyle.GetProperty("font");
        BoldButton.IsChecked = font.GetProperty("bold").GetBoolean();
        ItalicButton.IsChecked = font.GetProperty("italic").GetBoolean();
        UnderlineButton.IsChecked = font.GetProperty("underline").GetString() != "None";
        JsonElement alignment = fullStyle.GetProperty("align");
        WrapButton.IsChecked = alignment.GetProperty("wrap").GetBoolean();
        string? horizontal = alignment.GetProperty("h").GetString(), vertical = alignment.GetProperty("v").GetString();
        AlignTopButton.IsChecked = vertical == "Top";
        AlignMiddleButton.IsChecked = vertical == "Center";
        AlignBottomButton.IsChecked = vertical == "Bottom";
        AlignLeftButton.IsChecked = horizontal == "Left";
        AlignCenterButton.IsChecked = horizontal is "Center" or "CenterAcross";
        AlignRightButton.IsChecked = horizontal == "Right";
    }
    static (int top, int left, int bottom, int right) ReadSelectionBounds(JsonElement view)
    {
        if (view.TryGetProperty("selectionBounds", out JsonElement bounds))
        {
            JsonElement start = bounds.GetProperty("start");
            JsonElement end = bounds.GetProperty("end");
            return (start.GetProperty("row").GetInt32(), start.GetProperty("col").GetInt32(),
                end.GetProperty("row").GetInt32(), end.GetProperty("col").GetInt32());
        }

        // Fall back to the last A1 range in the selection.
        string selection = view.GetProperty("selection").GetString() ?? "";
        string[] endpoints = selection[(selection.LastIndexOf(',') + 1)..].Split(':', 2);
        string first = endpoints[0];
        string last = endpoints[^1];
        if (first.All(char.IsAsciiDigit)) (first, last) = ($"A{first}", $"XFD{last}");
        else if (first.All(char.IsAsciiLetter)) (first, last) = ($"{first}1", $"{last}1048576");
        (int top, int left) = ParseCell(first);
        (int bottom, int right) = ParseCell(last);
        return (top - 1, left, bottom - 1, right);
    }
}
