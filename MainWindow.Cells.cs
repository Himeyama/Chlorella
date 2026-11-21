using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    void AddHeader(string text, string tag, int row, int column)
    {
        SheetHeaderButton label = new()
        {
            Content = text == "Select all" ? "◢" : text,
            Tag = tag,
            Style = (Style)RootGrid.Resources["HeaderCellStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (tag == "all" || tag.StartsWith("row:"))
        {
            Microsoft.UI.Xaml.Media.TranslateTransform offset = new() { X = columnScrollFraction * columnWidths[0] };
            label.RenderTransform = offset;
            rowHeaderOffsets.Add(offset);
            Canvas.SetZIndex(label, 5);
        }
        AutomationProperties.SetName(label, text == "Select all" ? "すべて選択" : tag.StartsWith("row:") ? $"{text} 行を選択" : $"{text} 列を選択");
        label.Click += Header_Click;
        if (tag != "all")
        {
            label.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Header_PointerPressed), true);
            label.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Header_PointerMoved), true);
            label.PointerExited += Header_PointerExited;
            MenuFlyout menu = new();
            MenuFlyoutItem size = new() { Text = tag.StartsWith("row:") ? "行の高さ..." : "列の幅...", Icon = new FontIcon { Glyph = "\uE8A3" } };
            size.Click += async (_, _) => await SetHeaderSizeAsync(tag);
            MenuFlyoutItem insert = new() { Text = tag.StartsWith("row:") ? "行を挿入" : "列を挿入", Icon = new FontIcon { Glyph = "\uE710" } };
            insert.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                await InsertHeaderAsync(tag);
                dirty = true;
                await RefreshAsync();
            });
            menu.Items.Add(size);
            menu.Items.Add(insert);
            label.ContextFlyout = menu;
        }
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        CellGrid.Children.Add(label);
    }
    async void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string address })
            await RunAsync(async () => await SelectRangeAsync(address, address));
    }
    async void Cell_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Tag: string address } cell && e.GetCurrentPoint(CellGrid).Properties.IsLeftButtonPressed)
        {
            Windows.Foundation.Point position = e.GetCurrentPoint(cell).Position;
            (int pressedRow, int pressedCol) = ParseCell(address);
            bool onEdge = (pressedRow - 1 == selectionBounds.Top && position.Y <= 5) ||
                (pressedRow - 1 == selectionBounds.Bottom && position.Y >= cell.ActualHeight - 5) ||
                (pressedCol == selectionBounds.Left && position.X <= 5) ||
                (pressedCol == selectionBounds.Right && position.X >= cell.ActualWidth - 5);
            if (onEdge && pressedRow - 1 >= selectionBounds.Top && pressedRow - 1 <= selectionBounds.Bottom &&
                pressedCol >= selectionBounds.Left && pressedCol <= selectionBounds.Right)
            {
                cell.ReleasePointerCapture(e.Pointer);
                BeginRangeMove(e, pressedRow - 1, pressedCol, false, false);
                return;
            }
            if (cell.Content is FontIcon check && Math.Abs(position.X - cell.ActualWidth / 2) <= 14 &&
                Math.Abs(position.Y - cell.ActualHeight / 2) <= 14)
            {
                e.Handled = true;
                await RunAsync(async () => await ToggleBooleanCellAsync(address, check.Glyph == CheckedGlyph));
                return;
            }
            long now = Environment.TickCount64;
            bool doubleClick = lastCellClick == address && now - lastCellClickTime <= 500;
            lastCellClick = doubleClick ? null : address;
            lastCellClickTime = now;
            if (doubleClick)
            {
                e.Handled = true;
                await RunAsync(async () =>
                {
                    await SelectRangeAsync(address, address);
                    StartCellEdit(address, false);
                });
                return;
            }
            cell.ReleasePointerCapture(e.Pointer);
            dragging = true;
            dragStart = address;
            dragEnd = address;
            if (!CellGrid.CapturePointer(e.Pointer)) dragging = false;
            if (dragging && !inputPending) UpdateDragPreview();
            e.Handled = dragging;
        }
    }
    void CellGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (movingRange)
        {
            UpdateRangeMove(e);
            return;
        }
        if (resizingHeader)
        {
            bool resizingRow = resizeTag.StartsWith("row:");
            Windows.Foundation.Point resizePoint = e.GetCurrentPoint(CellGrid).Position;
            double size = Math.Max(resizingRow ? 18 : 20, resizeStart + (resizingRow ? resizePoint.Y : resizePoint.X) - resizeOrigin);
            int index = int.Parse(resizeTag[4..]) - (resizingRow ? firstRow : firstColumn);
            if (resizingRow && index >= 0 && index < viewRows)
            {
                rowHeights[index] = size;
                CellGrid.RowDefinitions[index + 1].Height = new GridLength(size);
            }
            if (!resizingRow && index >= 0 && index < viewColumns)
            {
                columnWidths[index] = size;
                CellGrid.ColumnDefinitions[index + 1].Width = new GridLength(size);
            }
            e.Handled = true;
            return;
        }
        if (selectingHeader)
        {
            Windows.Foundation.Point headerPoint = e.GetCurrentPoint(CellGrid).Position;
            headerSelectionEnd = selectingHeaderRow
                ? firstRow + IndexAt(headerPoint.Y - 25, rowHeights, viewRows)
                : firstColumn + IndexAt(headerPoint.X - 48, columnWidths, viewColumns);
            if (!inputPending) UpdateHeaderDragPreview();
            e.Handled = true;
            return;
        }
        if (!dragging) return;
        Windows.Foundation.Point point = e.GetCurrentPoint(CellGrid).Position;
        int col = IndexAt(point.X - 48, columnWidths, viewColumns);
        int row = IndexAt(point.Y - 25, rowHeights, viewRows);
        string next = CellAddress(firstRow + row, firstColumn + col);
        if (next == dragEnd) return;
        lastCellClick = null;
        dragEnd = next;
        UpdateDragPreview();
    }
    async void CellGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (movingRange)
        {
            await FinishRangeMoveAsync(e);
            return;
        }
        if (resizingHeader)
        {
            resizingHeader = false;
            CellGrid.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            string tag = resizeTag;
            bool row = tag.StartsWith("row:");
            int index = int.Parse(tag[4..]) - (row ? firstRow : firstColumn);
            double size = row ? rowHeights[index] : columnWidths[index];
            if (Math.Abs(size - resizeStart) < 0.01) return;
            lastHeaderEdge = null;
            await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ChangeHeaderSizeAsync(tag, size);
                dirty = true;
                await RefreshAsync();
            });
            return;
        }
        if (selectingHeader)
        {
            selectingHeader = false;
            CellGrid.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            int start = headerSelectionStart, end = headerSelectionEnd;
            bool row = selectingHeaderRow;
            await RunAsync(async () => await SelectHeaderRangeAsync(row, start, end));
            return;
        }
        if (!dragging) return;
        dragging = false;
        CellGrid.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        await RunAsync(async () => await SelectRangeAsync(dragStart, dragEnd));
    }
    async void CellGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (movingRange)
        {
            movingRange = false;
            ClearMovePreview();
            SetStatus("");
            await RunAsync(async () => await RefreshAsync(refreshSheets: false));
            return;
        }
        dragging = false;
        if (resizingHeader)
        {
            resizingHeader = false;
            await RunAsync(async () => await RefreshAsync(refreshSheets: false));
            return;
        }
        if (!selectingHeader) return;
        selectingHeader = false;
        await RunAsync(async () => await RefreshAsync(refreshSheets: false));
    }
    static int IndexAt(double point, double[] sizes, int count)
    {
        for (int i = 0; i < count - 1; i++)
        {
            if (point < sizes[i]) return i;
            point -= sizes[i];
        }
        return count - 1;
    }
    async void Header_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } header || !e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
        Windows.Foundation.Point local = e.GetCurrentPoint(header).Position;
        bool row = tag.StartsWith("row:");
        string? edgeTag = HeaderResizeTag(tag, header, local);
        int headerIndex = int.Parse(tag[4..]);
        bool selectedHeader = row
            ? selectionBounds.Left == 0 && selectionBounds.Right == 16383 && headerIndex - 1 >= selectionBounds.Top &&
                headerIndex - 1 <= selectionBounds.Bottom
            : selectionBounds.Top == 0 && selectionBounds.Bottom == 1048575 && headerIndex >= selectionBounds.Left &&
                headerIndex <= selectionBounds.Right;
        if (edgeTag is null && selectedHeader && ShiftDown())
        {
            header.ReleasePointerCapture(e.Pointer);
            BeginRangeMove(e, row ? headerIndex - 1 : 0, row ? 0 : headerIndex, row, !row);
            return;
        }
        if (edgeTag is null)
        {
            header.ReleasePointerCapture(e.Pointer);
            selectingHeader = CellGrid.CapturePointer(e.Pointer);
            if (selectingHeader)
            {
                selectingHeaderRow = row;
                headerSelectionStart = headerSelectionEnd = int.Parse(tag[4..]);
                if (!inputPending) UpdateHeaderDragPreview();
                e.Handled = true;
            }
            return;
        }
        tag = edgeTag;
        Windows.Foundation.Point point = e.GetCurrentPoint(CellGrid).Position;
        long now = Environment.TickCount64;
        bool doubleClick = lastHeaderEdge == tag && now - lastHeaderEdgeTime <= 500 &&
            Math.Abs(point.X - lastHeaderEdgePoint.X) <= 4 && Math.Abs(point.Y - lastHeaderEdgePoint.Y) <= 4;
        lastHeaderEdge = doubleClick ? null : tag;
        lastHeaderEdgeTime = now;
        lastHeaderEdgePoint = point;
        if (doubleClick)
        {
            e.Handled = true;
            await RunAsync(async () =>
            {
                await CommitInputAsync();
                if (row) await ExecuteAsync("home.autofitRowHeight", new { rows = $"{tag[4..]}:{tag[4..]}" });
                else
                {
                    string column = ColumnName(int.Parse(tag[4..]));
                    await ExecuteAsync("home.autofitColumnWidth", new { cols = $"{column}:{column}" });
                }
                dirty = true;
                await RefreshAsync();
            });
            return;
        }
        resizeTag = tag;
        resizeOrigin = row ? point.Y : point.X;
        int resizeIndex = int.Parse(tag[4..]) - (row ? firstRow : firstColumn);
        resizeStart = row ? rowHeights[resizeIndex] : columnWidths[resizeIndex];
        header.ReleasePointerCapture(e.Pointer);
        resizingHeader = CellGrid.CapturePointer(e.Pointer);
        e.Handled = resizingHeader;
    }
    string? HeaderResizeTag(string tag, Button header, Windows.Foundation.Point local)
    {
        bool row = tag.StartsWith("row:");
        double coordinate = row ? local.Y : local.X;
        double size = row ? header.ActualHeight : header.ActualWidth;
        int index = int.Parse(tag[4..]);
        int first = row ? firstRow : firstColumn;
        if (coordinate <= 4 && index > first) return $"{(row ? "row" : "col")}:{index - 1}";
        return size - coordinate <= 7 ? tag : null;
    }
    void Header_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not SheetHeaderButton { Tag: string tag } header) return;
        bool headerRow = tag.StartsWith("row:");
        Windows.Foundation.Point local = e.GetCurrentPoint(header).Position;
        header.SetResizeCursor(headerRow, resizingHeader || HeaderResizeTag(tag, header, local) is not null);
    }
    void Header_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is SheetHeaderButton header) header.SetResizeCursor(false, false);
    }
    Task<JsonElement> ChangeHeaderSizeAsync(string tag, double size) => tag.StartsWith("row:")
        ? ExecuteAsync("home.rowHeight", new { rows = $"{tag[4..]}:{tag[4..]}", height = size })
        : ExecuteAsync("home.columnWidth",
            new { cols = $"{ColumnName(int.Parse(tag[4..]))}:{ColumnName(int.Parse(tag[4..]))}", width = size });
    Task<JsonElement> InsertHeaderAsync(string tag) => tag.StartsWith("row:")
        ? ExecuteAsync("home.insertRows", new { rows = $"{tag[4..]}:{tag[4..]}" })
        : ExecuteAsync("home.insertColumns", new { cols = $"{ColumnName(int.Parse(tag[4..]))}:{ColumnName(int.Parse(tag[4..]))}" });
    async Task SetHeaderSizeAsync(string tag)
    {
        bool row = tag.StartsWith("row:");
        int index = int.Parse(tag[4..]) - (row ? firstRow : firstColumn);
        TextBox input = new()
        {
            Text =
            (row ? rowHeights[index] : columnWidths[index]).ToString(System.Globalization.CultureInfo.InvariantCulture),
            InputScope = new InputScope { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } }
        };
        ContentDialog dialog = new()
        {
            Title = row ? "行の高さ" : "列の幅",
            Content = input,
            PrimaryButtonText = "変更",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            if (!double.TryParse(input.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out double size) || !double.IsFinite(size) || size <= 0)
                throw new FormatException("0 より大きい数値を入力してください。");
            await CommitInputAsync();
            await ChangeHeaderSizeAsync(tag, size);
            dirty = true;
            await RefreshAsync();
        });
    }
    void UpdateDragPreview()
    {
        (int row1, int col1) = ParseCell(dragStart);
        (int row2, int col2) = ParseCell(dragEnd);
        UpdateSelectionVisuals(Math.Min(row1, row2) - 1, Math.Min(col1, col2), Math.Max(row1, row2) - 1, Math.Max(col1, col2));
    }
    void UpdateSelectionVisuals(int top, int left, int bottom, int right, bool updateCorner = true)
    {
        selectionBounds = (top, left, bottom, right);
        UpdateHeaderSelectionVisuals(top, left, bottom, right);
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not Button { Tag: string tag } button || tag.Contains(':') || tag == "all") continue;
            (int row, int col) = ParseCell(tag);
            row--;
            bool selected = row >= top && row <= bottom && col >= left && col <= right;
            Style style = (Style)RootGrid.Resources[selected ? "SelectedCellStyle" : "SheetCellStyle"];
            Thickness border = new(col == left ? 2 : 0, row == top ? 2 : 0,
                col + Grid.GetColumnSpan(button) - 1 == right ? 2 : 0, row + Grid.GetRowSpan(button) - 1 == bottom ? 2 : 0);
            if (button.Style != style) button.Style = style;
            if (selected && button.BorderThickness != border) button.BorderThickness = border;
            else if (!selected && button.ReadLocalValue(Button.BorderThicknessProperty) != DependencyProperty.UnsetValue)
                button.ClearValue(Button.BorderThicknessProperty);
            if (button.Content is TextBlock text)
            {
                string name = $"{tag}: {text.Text}{(selected ? "、選択中" : "")}";
                if (AutomationProperties.GetName(button) != name) AutomationProperties.SetName(button, name);
            }
        }
        if (updateCorner) UpdateSelectionCorner(bottom, right);
    }
    void UpdateSelectionCorner(int bottom, int right)
    {
        if (selectionCorner is not null) CellGrid.Children.Remove(selectionCorner);
        selectionCorner = null;
        int row = bottom - (firstRow - 1), col = right - firstColumn;
        if (row < 0 || row >= viewRows || col < 0 || col >= viewColumns) return;
        selectionCorner = new Border
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -7, -7),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = new Border { Width = 8, Height = 8, Background = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"] }
        };
        Border handle = selectionCorner;
        Windows.Foundation.Point start = default;
        bool filling = false;
        (int Top, int Left, int Bottom, int Right) sourceBounds = selectionBounds;
        (int Top, int Left, int Bottom, int Right) previewBounds = sourceBounds;
        string source = currentSelection;
        (int Top, int Left, int Bottom, int Right) BoundsAt(Windows.Foundation.Point point)
        {
            if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) < 4) return sourceBounds;
            int endRow = firstRow - 1 + IndexAt(point.Y - 25, rowHeights, viewRows);
            int endCol = firstColumn + IndexAt(point.X - 48, columnWidths, viewColumns);
            int dr = endRow < sourceBounds.Top ? endRow - sourceBounds.Top : Math.Max(0, endRow - sourceBounds.Bottom);
            int dc = endCol < sourceBounds.Left ? endCol - sourceBounds.Left : Math.Max(0, endCol - sourceBounds.Right);
            if (dr == 0 && dc == 0) return sourceBounds;
            bool vertical = Math.Abs(point.Y - start.Y) >= Math.Abs(point.X - start.X);
            if (vertical && dr == 0) vertical = false;
            if (!vertical && dc == 0) vertical = true;
            return vertical
                ? (Math.Min(sourceBounds.Top, endRow), sourceBounds.Left, Math.Max(sourceBounds.Bottom, endRow), sourceBounds.Right)
                : (sourceBounds.Top, Math.Min(sourceBounds.Left, endCol), sourceBounds.Bottom, Math.Max(sourceBounds.Right, endCol));
        }
        void ShowPreview((int Top, int Left, int Bottom, int Right) bounds)
        {
            if (previewBounds == bounds) return;
            previewBounds = bounds;
            UpdateSelectionVisuals(bounds.Top, bounds.Left, bounds.Bottom, bounds.Right, updateCorner: false);
            Grid.SetRow(handle, bounds.Bottom - (firstRow - 1) + 1);
            Grid.SetColumn(handle, bounds.Right - firstColumn + 1);
        }
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            start = e.GetCurrentPoint(CellGrid).Position;
            filling = handle.CapturePointer(e.Pointer);
            e.Handled = true;
        }
;
        handle.PointerMoved += (_, e) =>
        {
            if (!filling) return;
            ShowPreview(BoundsAt(e.GetCurrentPoint(CellGrid).Position));
            e.Handled = true;
        }
;
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!filling) return;
            filling = false;
            ShowPreview(sourceBounds);
        }
;
        handle.PointerReleased += async (_, e) =>
        {
            if (!filling) return;
            (int Top, int Left, int Bottom, int Right) bounds = BoundsAt(e.GetCurrentPoint(CellGrid).Position);
            filling = false;
            handle.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            ShowPreview(bounds);
            if (bounds == sourceBounds) return;
            string target = $"{CellAddress(bounds.Top + 1, bounds.Left)}:{CellAddress(bounds.Bottom + 1, bounds.Right)}";
            await RunAsync(async () =>
            {
                try
                {
                    await CommitInputAsync();
                    await ExecuteAsync("edit.autoFill", new { source, target });
                }
                catch
                {
                    ShowPreview(sourceBounds);
                    throw;
                }
                dirty = true;
                await RefreshAsync();
                FocusCell(activeCell);
            });
        }
;
        Grid.SetRow(handle, row + 1);
        Grid.SetColumn(handle, col + 1);
        CellGrid.Children.Add(handle);
    }
    void DrawCopyOutline()
    {
        foreach (UIElement element in CellGrid.Children.ToArray())
            if (element is Microsoft.UI.Xaml.Shapes.Rectangle { Tag: string tag } &&
                tag == "copy-outline") CellGrid.Children.Remove(element);
        if (copiedCells is not { } copied || copied.Sheet != currentSheet) return;
        int top = Math.Max(copied.Top, firstRow - 1), left = Math.Max(copied.Left, firstColumn);
        int bottom = Math.Min(copied.Bottom, firstRow + viewRows - 2), right = Math.Min(copied.Right, firstColumn + viewColumns - 1);
        if (top > bottom || left > right) return;
        Microsoft.UI.Xaml.Shapes.Rectangle outline = new()
        {
            Tag = "copy-outline",
            IsHitTestVisible = false,
            Stroke = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"],
            StrokeThickness = 2,
            StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 3, 2 },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(outline, top - (firstRow - 1) + 1);
        Grid.SetColumn(outline, left - firstColumn + 1);
        Grid.SetRowSpan(outline, bottom - top + 1);
        Grid.SetColumnSpan(outline, right - left + 1);
        CellGrid.Children.Add(outline);
    }
    void UpdateHeaderSelectionVisuals(int top, int left, int bottom, int right)
    {
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not SheetHeaderButton { Tag: string tag } header) continue;
            bool selected = tag.StartsWith("row:") && left == 0 && right == 16_383 &&
                int.TryParse(tag[4..], out int row) && row - 1 >= top && row - 1 <= bottom;
            selected |= tag.StartsWith("col:") && top == 0 && bottom == 1_048_575 &&
                int.TryParse(tag[4..], out int col) && col >= left && col <= right;
            if (selected) header.Background = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["SelectionFillBrush"];
            else header.ClearValue(Button.BackgroundProperty);
        }
    }
    async Task SelectRangeAsync(string start, string end)
    {
        if (selectedShape is not null || selectedImage is not null)
        {
            selectedShape = null;
            selectedImage = null;
            ShapeTab.Visibility = Visibility.Collapsed;
            if (RibbonSelector.SelectedItem == ShapeTab) RibbonSelector.SelectedItem = RibbonSelector.Items[1];
            viewportDirty = true;
        }
        bool changedCells = inputPending || viewportDirty || cellEditor is not null;
        await CommitInputAsync();
        (int row1, int col1) = ParseCell(start);
        (int row2, int col2) = ParseCell(end);
        string firstAddress = CellAddress(Math.Min(row1, row2), Math.Min(col1, col2));
        string lastAddress = CellAddress(Math.Max(row1, row2), Math.Max(col1, col2));
        string range = $"{firstAddress}:{lastAddress}";
        await ExecuteAsync("selection.set", new { range, active = start });
        selectionAnchor = start;
        selectionLead = end;
        await RefreshAsync(refreshSheets: false, selectionOnly: !changedCells, updateCellValues: changedCells);
        FocusCell(end);
    }
    static (int row, int col) ParseCell(string address)
    {
        string text = address.Trim().Replace("$", "").ToUpperInvariant();
        int i = 0, col = 0;
        while (i < text.Length && text[i] is >= 'A' and <= 'Z')
        {
            col = checked(col * 26 + text[i] - 'A' + 1);
            i++;
        }
        if (i == 0 || i == text.Length || !int.TryParse(text[i..], out int row) || row is < 1 or > 1_048_576 || col is < 1 or > 16_384)
            throw new FormatException("セル番地を入力してください。例: A1");
        return (row, col - 1);
    }
    static string ColumnName(int col)
    {
        string name = "";
        for (int n = col + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }
    static string CellAddress(int row, int col) => $"{ColumnName(col)}{row}";
    void FocusCell(string address)
    {
        Button? navigationCell = null;
        Button? inputCell = null;
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not Button { Tag: string tag } button) continue;
            if (tag == address) navigationCell = button;
            if (tag == activeCell) inputCell = button;
        }
        if (navigationCell is not null)
            ArmCellInput(inputCell is null ? address : activeCell, navigationCell, inputCell ?? navigationCell);
    }
    async void Cell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Button { Tag: string address }) return;
        if (scrollPreview is not null)
        {
            e.Handled = true;
            return;
        }
        (int row, int col) = ParseCell(address);
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        bool control = (InputKeyboardSource.GetKeyStateForCurrentThread(
            VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (control && e.Key is VirtualKey.C or VirtualKey.X or VirtualKey.V)
        {
            e.Handled = true;
            string command = e.Key == VirtualKey.C ? "edit.copy" : e.Key == VirtualKey
                .X ? "edit.cut" : shift ? "edit.paste.values" : "edit.paste";
            await RunAsync(async () => await ClipboardCommandAsync(command));
            return;
        }
        if (!control && e.Key == VirtualKey.Space && sender is Button { Content: FontIcon check })
        {
            e.Handled = true;
            await RunAsync(async () => await ToggleBooleanCellAsync(address, check.Glyph == CheckedGlyph));
            return;
        }
        if (control && e.Key == VirtualKey.A)
        {
            e.Handled = true;
            await RunAsync(async () => await SelectHeaderAsync("all"));
            return;
        }
        if (control && e.Key == VirtualKey.E)
        {
            e.Handled = true;
            await FlashFillAsync();
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Left:
                col = control ? 0 : col - 1;
                break;
            case VirtualKey.Right:
                col = control ? 16_383 : col + 1;
                break;
            case VirtualKey.Up:
                row = control ? 1 : row - 1;
                break;
            case VirtualKey.Down:
                row = control ? 1_048_576 : row + 1;
                break;
            case VirtualKey.Enter:
                row++;
                break;
            case VirtualKey.Delete:
                e.Handled = true;
                await RunAsync(async () =>
                {
                    await ExecuteAsync("edit.clearContents", new { });
                    dirty = true;
                    await RefreshAsync(refreshSheets: false, updateCellValues: true);
                    FocusCell(address);
                });
                return;
            case VirtualKey.F2:
                StartCellEdit(activeCell, false);
                e.Handled = true;
                return;
            default:
                int key = (int)e.Key;
                if (!control && (e.Key == VirtualKey.Space || key is >= 48 and <= 90 or >= 96 and <= 111 or >= 186 and <= 222 or 229))
                    StartCellEdit(activeCell, true);
                return;
        }
        e.Handled = true;
        if (!control && e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Enter)
        {
            (int originalRow, _) = ParseCell(address);
            row = await MoveVisibleRowAsync(originalRow, e.Key == VirtualKey.Up ? -1 : 1, 1);
        }
        row = Math.Clamp(row, 1, 1_048_576);
        col = Math.Clamp(col, 0, 16_383);
        string next = CellAddress(row, col);
        EnsureVisible(row, col);
        await RunAsync(async () => await SelectRangeAsync(shift ? selectionAnchor : next, next));
    }
    async void Clipboard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command })
            await RunAsync(async () => await ClipboardCommandAsync(command));
    }
    async Task ClipboardCommandAsync(string command)
    {
        await CommitInputAsync();
        if (command is "edit.copy" or "edit.cut")
        {
            objectClipboard = null;
            objectClipboardToken = null;
            JsonElement result = await ExecuteAsync(command, new { html = true });
            if (command == "edit.cut") dirty = true;
            string text = result.GetProperty("text").GetString() ?? "";
            DataPackage package = new();
            package.SetText(text);
            if (result.TryGetProperty("data",
                out JsonElement internalData) && internalData.ValueKind == JsonValueKind.String &&
                    internalData.GetString() is string payload)
                package.SetData("Chlorella.Cells.v1", payload);
            if (result.TryGetProperty("html", out JsonElement html) && html.GetString() is string markup)
                package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(markup));
            Clipboard.SetContent(package);
            if (command == "edit.cut") await RefreshAsync();
            copiedCells = (selectionBounds.Top, selectionBounds.Left, selectionBounds.Bottom, selectionBounds.Right, currentSheet);
            DrawCopyOutline();
            cutClipboardText = command == "edit.cut" ? text : null;
            cutPasted = false;
            SetStatus(command == "edit.cut" ? "切り取りました。" : "コピーしました。");
        }
        else
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains("Chlorella.ObjectReference"))
            {
                if (command != "edit.paste") throw new InvalidOperationException("図形や画像には形式を選択した貼り付けを利用できません。");
                string token = (await content.GetDataAsync("Chlorella.ObjectReference"))?.ToString() ?? "";
                if (objectClipboard is not { } copied || token != objectClipboardToken)
                    throw new InvalidOperationException("この図形または画像は、この画面では貼り付けできません。");
                if (copied.Sheet != currentSheet) throw new InvalidOperationException("図形と画像の貼り付けは現在、コピー元と同じシート内で利用できます。");
                if (copied.Cut)
                {
                    await ExecuteAsync("object.move", new { kind = copied.Kind, id = copied.Id, at = activeCell, dx = 0, dy = 0 });
                    objectClipboard = null;
                    objectClipboardToken = null;
                }
                else await ExecuteAsync("object.duplicate", new { kind = copied.Kind, id = copied.Id, at = activeCell });
                dirty = true;
                await RefreshAsync();
                FocusCell(selectionLead);
                SetStatus("貼り付けました。");
                return;
            }
            if (!content.Contains(StandardDataFormats.Text)) throw new InvalidOperationException("クリップボードに貼り付け可能なテキストがありません。");
            string text = await content.GetTextAsync();
            if (cutPasted && text == cutClipboardText) throw new InvalidOperationException("切り取ったセルは既に貼り付けられています。");
            if (text != cutClipboardText)
            {
                cutClipboardText = null;
                cutPasted = false;
            }
            string? html = content.Contains(StandardDataFormats.Html) ? await content.GetHtmlFormatAsync() : null;
            string? data = content.Contains("Chlorella.Cells.v1") ? (await content.GetDataAsync("Chlorella.Cells.v1"))?.ToString() : null;
            string mode = command switch
            {
                "edit.paste.values" => "values",
                "edit.paste.formulas" => "formulas",
                "edit.paste.formats" => "formats",
                _ => "all"
            };
            await ExecuteAsync("edit.paste", new { text, html, data, mode });
            copiedCells = null;
            if (cutClipboardText is not null && text == cutClipboardText) cutPasted = true;
            dirty = true;
            await RefreshAsync();
            SetStatus("貼り付けました。");
        }
        FocusCell(selectionLead);
    }
    void EnsureVisible(int row, int col)
    {
        int previousColumn = firstColumn;
        if (row < firstRow) firstRow = row;
        int visibleRows = VisibleCount(rowHeights, viewRows, Math.Max(1, GridScroll.ActualHeight / GridScroll.ZoomFactor - 25));
        if (row >= firstRow + visibleRows) firstRow = Math.Min(1_048_576, row - visibleRows + 1);
        if (col < firstColumn) firstColumn = col;
        int visibleColumns = VisibleCount(columnWidths, viewColumns, Math.Max(1, GridScroll.ActualWidth / GridScroll.ZoomFactor - 48));
        if (col >= firstColumn + visibleColumns) firstColumn = Math.Min(16_383, col - visibleColumns + 1);
        if (firstColumn != previousColumn || (col == firstColumn && columnScrollFraction > 0))
        {
            columnScrollFraction = 0;
            UpdateHorizontalOffset();
        }
    }
    static int VisibleCount(double[] sizes, int count, double space)
    {
        int visible = 0;
        while (visible < count && (sizes[visible] <= 0 || space >= sizes[visible]))
        {
            space -= Math.Max(0, sizes[visible]);
            visible++;
        }
        return Math.Max(1, visible);
    }
    void ScheduleScroll()
    {
        if (!scrollTimer.IsEnabled) scrollTimer.Start();
    }
    void RowScroll_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (updatingScroll) return;
        int next = Math.Clamp((int)Math.Round(e.NewValue), 1, 1_048_576);
        if (next == firstRow) return;
        firstRow = next;
        ShowScrollPreview();
        ScheduleScroll();
    }
    void ColumnScroll_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (updatingScroll) return;
        double position = Math.Clamp(e.NewValue, 0, 16_383);
        int next = (int)Math.Floor(position);
        columnScrollFraction = position - next;
        UpdateHorizontalOffset();
        if (next == firstColumn) return;
        firstColumn = next;
        ShowScrollPreview();
        ScheduleScroll();
    }
    void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (GridScroll is null || ZoomReset is null) return;
        int percent = (int)Math.Round(e.NewValue);
        GridScroll.ChangeView(null, null, percent / 100f, true);
        ZoomReset.Content = $"{percent}%";
        AutomationProperties.SetName(ZoomReset, $"表示倍率 {percent}%、100% に戻す");
        if (dataSource is not null) ScheduleScroll();
    }
    void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 10);
    void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 10);
    void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = 100;
    async void GridScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(GridScroll).Properties.MouseWheelDelta;
        if (delta == 0) return;
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (shift) firstColumn = Math.Clamp(firstColumn - Math.Sign(delta) * 3, 0, 16_383);
        else firstRow = await MoveVisibleRowAsync(firstRow, -Math.Sign(delta), activeTableFilter ? 1 : 3);
        e.Handled = true;
        ShowScrollPreview();
        ScheduleScroll();
    }
    async void ScrollTimer_Tick(object? sender, object e)
    {
        scrollTimer.Stop();
        if (scrollBusy)
        {
            ScheduleScroll();
            return;
        }
        scrollBusy = true;
        try
        {
            await CommitInputAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (renderedRow >= 1 && renderedColumn >= 0)
            {
                firstRow = renderedRow;
                firstColumn = renderedColumn;
                columnScrollFraction = renderedColumnScrollFraction;
                UpdateHorizontalOffset();
                updatingScroll = true;
                try
                {
                    RowScroll.Value = firstRow;
                    ColumnScroll.Value = firstColumn + columnScrollFraction;
                }
                finally
                {
                    updatingScroll = false;
                }
            }
            RemoveScrollPreview();
            SetStatus(ex.Message);
        }
        finally
        {
            scrollBusy = false;
        }
    }
    // Keep a text input focused before typing starts so the IME receives the first key.
    void ArmCellInput(string address, Button navigationCell, Button inputCell)
    {
        TextBox editor = CreateCellEditor(address, "");
        editor.Opacity = 0;
        editor.ContextFlyout = inputCell.ContextFlyout;
        AutomationProperties.SetName(editor, AutomationProperties.GetName(inputCell));
        cellInputWaiting = true;
        editor.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (cellInputWaiting)
            {
                editor.ReleasePointerCapture(e.Pointer);
                Cell_PointerPressed(inputCell, e);
            }
        }), true);
        void BeginInput()
        {
            if (!cellInputWaiting) return;
            cellInputWaiting = false;
            editor.Opacity = 1;
            editor.ContextFlyout = null;
            AutomationProperties.SetName(editor, $"{address} の入力");
        }
        editor.TextCompositionStarted += (_, _) => BeginInput();
        editor.TextChanging += (_, _) => BeginInput();
        editor.PreviewKeyDown += (_, e) =>
        {
            if (!cellInputWaiting) return;
            bool control = (InputKeyboardSource.GetKeyStateForCurrentThread(
                VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if ((e.Key == VirtualKey.Space && inputCell.Content is FontIcon) ||
                e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey
                    .Enter or VirtualKey.Delete or VirtualKey.F2 ||
                (control && e.Key is VirtualKey.A or VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.E))
                Cell_KeyDown(navigationCell, e);
        }
;
        bool focusPending = false;
        editor.Loaded += (_, _) =>
        {
            if (focusPending && cellEditor == editor && cellInputWaiting &&
                ReferenceEquals(FocusManager.GetFocusedElement(CellGrid.XamlRoot), inputCell))
                editor.Focus(FocusState.Programmatic);
        }
;
        ShowCellEditor(editor, address);
        if (!editor.Focus(FocusState.Programmatic))
        {
            // A newly added control may not be ready for focus until it is loaded.
            focusPending = true;
            inputCell.Focus(FocusState.Programmatic);
        }
    }

    TextBox CreateCellEditor(string address, string text)
    {
        TextBox editor = new()
        {
            Text = text,
            AcceptsReturn = true,
            Padding = new Thickness(3, 0, 3, 0),
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(editor, $"{address} の入力");
        editor.TextChanged += CellEditor_TextChanged;
        bool composing = false;
        editor.TextCompositionStarted += (_, _) => composing = true;
        editor.TextCompositionEnded += (_, _) => composing = false;
        editor.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((sender, e) =>
        {
            if (!composing && !cellInputWaiting) CellEditor_KeyDown(sender, e);
        }), true);
        return editor;
    }

    void ShowCellEditor(TextBox editor, string address)
    {
        if (cellEditor is not null) CellGrid.Children.Remove(cellEditor);
        (int row, int col) = ParseCell(address);
        Grid.SetRow(editor, row - firstRow + 1);
        Grid.SetColumn(editor, col - firstColumn + 1);
        Canvas.SetZIndex(editor, 2);
        CellGrid.Children.Add(editor);
        cellEditor = editor;
    }

    void StartCellEdit(string address, bool replace)
    {
        (int row, int col) = ParseCell(address);
        if (row < firstRow || row >= firstRow + viewRows || col < firstColumn || col >= firstColumn + viewColumns) return;
        cellInputWaiting = false;
        TextBox editor = CreateCellEditor(address, replace ? "" : FormulaInput.Text);
        ShowCellEditor(editor, address);
        editor.Focus(FocusState.Programmatic);
        editor.SelectionStart = editor.Text.Length;
        if (replace)
        {
            FormulaInput.Text = "";
            inputPending = true;
            dirty = true;
        }
    }
    void CellEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox editor && editor == cellEditor)
        {
            FormulaInput.Text = editor.Text;
            inputPending = true;
            dirty = true;
        }
    }
    async void CellEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            bool alt = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (alt && sender is TextBox editor)
            {
                int start = editor.SelectionStart;
                editor.Text = editor.Text.Remove(start, editor.SelectionLength).Insert(start, "\n");
                editor.SelectionStart = start + 1;
                return;
            }
            await RunAsync(async () =>
            {
                await CommitInputAsync();
                (int row, int col) = ParseCell(activeCell);
                row = await MoveVisibleRowAsync(row, 1, 1);
                EnsureVisible(row, col);
                string next = CellAddress(row, col);
                await SelectRangeAsync(next, next);
            });
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            inputPending = false;
            await RunAsync(async () =>
            {
                await RefreshAsync();
                FocusCell(activeCell);
            });
        }
    }
    async void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }) await RunAsync(async () => await SelectHeaderAsync(tag));
    }
    void UpdateHeaderDragPreview()
    {
        int first = Math.Min(headerSelectionStart, headerSelectionEnd);
        int last = Math.Max(headerSelectionStart, headerSelectionEnd);
        if (selectingHeaderRow) UpdateSelectionVisuals(first - 1, 0, last - 1, 16_383);
        else UpdateSelectionVisuals(0, first, 1_048_575, last);
    }
    async Task SelectHeaderRangeAsync(bool row, int start, int end)
    {
        await CommitInputAsync();
        int first = Math.Min(start, end), last = Math.Max(start, end);
        string range = row ? $"{first}:{last}" : $"{ColumnName(first)}:{ColumnName(last)}";
        string active = row ? CellAddress(start, firstColumn) : CellAddress(firstRow, start);
        await ExecuteAsync("selection.set", new { range, active });
        await RefreshAsync();
        selectionAnchor = activeCell;
        selectionLead = activeCell;
        FocusCell(activeCell);
    }
    async Task SelectHeaderAsync(string tag)
    {
        await CommitInputAsync();
        string range = tag switch
        {
            "all" => "A1:XFD1048576",
            _ when tag.StartsWith("row:") => $"{tag[4..]}:{tag[4..]}",
            _ when tag.StartsWith("col:") => $"{ColumnName(int.Parse(tag[4..]))}:{ColumnName(int.Parse(tag[4..]))}",
            _ => throw new InvalidOperationException("選択範囲を認識できません。"),
        };
        await ExecuteAsync("selection.set", new { range });
        await RefreshAsync();
        selectionAnchor = activeCell;
        selectionLead = activeCell;
        FocusCell(activeCell);
    }
}
