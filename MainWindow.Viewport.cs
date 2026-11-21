using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Chlorella;

public sealed partial class MainWindow
{
    Task<JsonElement> RequestViewportAsync()
    {
        string start = CellAddress(firstRow, firstColumn);
        string end = CellAddress(firstRow + viewRows - 1, firstColumn + viewColumns - 1);
        return SendAsync("sheet.viewport", new { ranges = new[] { $"{start}:{end}" } });
    }

    async Task RefreshAsync(
        bool refreshSheets = true,
        bool selectionOnly = false,
        bool updateCellValues = false,
        bool updateFontStyles = false)
    {
        int requestedRow = firstRow;
        if (dataSource is WorksheetEngine)
        {
            JsonElement resolved = await SendAsync("sheet.resolveVisibleRow", new { row = requestedRow });
            if (requestedRow != firstRow)
            {
                ScheduleScroll();
                return;
            }
            firstRow = resolved.GetProperty("row").GetInt32();
            requestedRow = firstRow;
        }
        int requestedColumn = firstColumn;
        double scale = ZoomSlider.Value / 100.0;
        viewRows = Math.Min(Math.Min(PageRows, (int)Math.Ceiling(40 / scale)), 1_048_577 - firstRow);
        viewColumns = Math.Min(Math.Min(PageColumns, (int)Math.Ceiling(24 / scale)), 16_384 - firstColumn);
        JsonElement view = await RequestViewportAsync();
        if (requestedRow != firstRow || requestedColumn != firstColumn)
        {
            ScheduleScroll();
            return;
        }
        if (dataSource is WorksheetEngine && viewRows < PageRows &&
            view.GetProperty("regions")[0].GetProperty("rowHeights")
                .EnumerateArray().Any(height => height.GetDouble() == 0))
        {
            viewRows = Math.Min(PageRows, 1_048_577 - firstRow);
            view = await RequestViewportAsync();
            if (requestedRow != firstRow || requestedColumn != firstColumn)
            {
                ScheduleScroll();
                return;
            }
        }
        if (!view.TryGetProperty("merges", out JsonElement merges) ||
            !view.TryGetProperty("tables", out _) ||
            !view.TryGetProperty("shapes", out _) ||
            !view.TryGetProperty("images", out _))
            throw new IncompatibleDesktopHostException();
        activeTableFilter = view.GetProperty("tables").EnumerateArray().Any(table =>
            table.TryGetProperty("filteredColumns", out JsonElement columns) &&
            columns.ValueKind == JsonValueKind.Array && columns.GetArrayLength() > 0);
        activeCell = view.GetProperty("activeCell").GetString() ?? "A1";
        string sheetName = view.GetProperty("sheet").GetString() ?? "";
        if (currentSheet != sheetName)
        {
            selectedShape = null;
            selectedImage = null;
            ShapeTab.Visibility = Visibility.Collapsed;
            if (RibbonSelector.SelectedItem == ShapeTab) RibbonSelector.SelectedItem = RibbonSelector.Items[1];
        }
        currentSheet = sheetName;
        currentSelection = view.GetProperty("selection").GetString() ?? activeCell;
        AddressInput.Text = activeCell;
        updatingInput = true;
        FormulaInput.Text = view.GetProperty("input").ValueKind == JsonValueKind.String ? view.GetProperty("input").GetString() : "";
        updatingInput = false;
        inputPending = false;
        JsonElement rows = view.GetProperty("regions")[0].GetProperty("cells");
        JsonElement region = view.GetProperty("regions")[0];
        (int top, int left, int bottom, int right) = ReadSelectionBounds(view);
        selectionBounds = (top, left, bottom, right);
        paletteTheme = view.GetProperty("theme").GetProperty("colors").Clone();
        UpdateMergeButton(merges);
        UpdateWrapButton(rows, view.GetProperty("styles"), merges);
        if ((updateCellValues || updateFontStyles) && !refreshSheets && dataSource is WorksheetEngine &&
            renderedRow == firstRow && renderedColumn == firstColumn &&
            renderedRows == viewRows && renderedColumns == viewColumns && merges.GetArrayLength() == 0 &&
            view.GetProperty("tables").GetArrayLength() == 0 && view.GetProperty("shapes").GetArrayLength() == 0 &&
            view.GetProperty("images").GetArrayLength() == 0)
        {
            UpdateVisibleCellValues(rows, view.GetProperty("styles"), updateFontStyles);
            for (int i = 0; i < viewRows; i++)
            {
                double rawHeight = region.GetProperty("rowHeights")[i].GetDouble();
                double height = rawHeight == 0 ? 0 : Math.Max(18, rawHeight);
                if (rowHeights[i] != height)
                {
                    rowHeights[i] = height;
                    CellGrid.RowDefinitions[i + 1].Height = new GridLength(height);
                }
            }
            if (cellEditor is not null)
            {
                CellGrid.Children.Remove(cellEditor);
                cellEditor = null;
            }
            UpdateSelectionVisuals(top, left, bottom, right);
            UpdateActiveFont(rows, view.GetProperty("styles"));
            viewportDirty = false;
            return;
        }
        if (selectionOnly && renderedRow == firstRow && renderedColumn == firstColumn && renderedRows == viewRows &&
            renderedColumns == viewColumns && cellEditor is null)
        {
            UpdateSelectionVisuals(top, left, bottom, right);
            foreach (UIElement element in CellGrid.Children)
            {
                if (element is Button { Tag: string tag, Content: TextBlock text } && tag == activeCell)
                {
                    updatingFont = true;
                    FontNameInput.Text = text.FontFamily.Source;
                    FontSizeInput.Text = (text.FontSize * 72.0 / 96.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    updatingFont = false;
                    break;
                }
            }
            return;
        }
        for (int i = 0; i < viewColumns; i++) columnWidths[i] = Math.Max(20, region.GetProperty("columnWidths")[i].GetDouble());
        for (int i = 0; i < viewRows; i++)
        {
            double height = region.GetProperty("rowHeights")[i].GetDouble();
            rowHeights[i] = height == 0 ? 0 : Math.Max(18, height);
        }
        UpdateHorizontalOffset();
        JsonElement styles = view.GetProperty("styles");
        CellGrid.Children.Clear();
        rowHeaderOffsets.Clear();
        scrollPreview = null;
        selectionCorner = null;
        cellEditor = null;
        CellGrid.RowDefinitions.Clear();
        CellGrid.ColumnDefinitions.Clear();
        CellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        for (int col = 0; col < viewColumns; col++) CellGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width =
            new GridLength(columnWidths[col])
        });
        CellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
        for (int row = 0; row < viewRows; row++) CellGrid.RowDefinitions.Add(new RowDefinition
        {
            Height =
            new GridLength(rowHeights[row])
        });
        AddHeader("Select all", "all", 0, 0);
        for (int col = 0; col < viewColumns; col++) AddHeader(ColumnName(firstColumn + col), $"col:{firstColumn + col}", 0, col + 1);
        for (int row = 0; row < viewRows; row++)
        {
            AddHeader((firstRow + row).ToString(), $"row:{firstRow + row}", row + 1, 0);
            for (int col = 0; col < viewColumns; col++)
            {
                int absoluteCol = firstColumn + col;
                string address = CellAddress(firstRow + row, absoluteCol);
                string value = rows[row][col].GetProperty("text").GetString() ?? "";
                int absoluteRow = firstRow + row - 1;
                int spanRows = 1, spanColumns = 1;
                string styleKey = rows[row][col].GetProperty("style").GetRawText();
                bool covered = false;
                foreach (JsonElement merge in merges.EnumerateArray())
                {
                    JsonElement range = merge.GetProperty("range");
                    int mr1 = range.GetProperty("start").GetProperty("row").GetInt32();
                    int mc1 = range.GetProperty("start").GetProperty("col").GetInt32();
                    int mr2 = range.GetProperty("end").GetProperty("row").GetInt32();
                    int mc2 = range.GetProperty("end").GetProperty("col").GetInt32();
                    if (absoluteRow < mr1 || absoluteRow > mr2 || absoluteCol < mc1 || absoluteCol > mc2) continue;
                    if (absoluteRow != Math.Max(mr1, firstRow - 1) || absoluteCol != Math.Max(mc1, firstColumn))
                    {
                        covered = true;
                        break;
                    }
                    spanRows = Math.Min(mr2, firstRow + viewRows - 2) - absoluteRow + 1;
                    spanColumns = Math.Min(mc2, firstColumn + viewColumns - 1) - absoluteCol + 1;
                    value = merge.GetProperty("text").GetString() ?? "";
                    styleKey = merge.GetProperty("style").GetRawText();
                    break;
                }
                if (covered) continue;
                bool selected = absoluteRow >= top && absoluteRow <= bottom && absoluteCol >= left && absoluteCol <= right;
                JsonElement cellData = rows[row][col];
                JsonElement fullStyle = cellData.TryGetProperty("tableStyle",
                    out JsonElement tableStyle) && tableStyle.ValueKind == JsonValueKind.Object
                    ? tableStyle : styles.GetProperty(styleKey);
                bool tableHeader = IsTableHeader(view.GetProperty("tables"), absoluteRow, absoluteCol, out bool filterButton);
                bool tableCell = TryGetTableAppearance(view.GetProperty("tables"), absoluteRow, absoluteCol, spanRows,
                    spanColumns, out bool alternateRow, out Thickness tableEdge);
                JsonElement style = fullStyle.GetProperty("font");
                JsonElement fillStyle = fullStyle.GetProperty("fill");
                JsonElement themeColors = view.GetProperty("theme").GetProperty("colors");
                Microsoft.UI.Xaml.Media.Brush? fontBrush = ColorBrush(style.GetProperty("color"), themeColors);
                Microsoft.UI.Xaml.Media.Brush? fillBrush = fillStyle.GetProperty("pattern").GetString() == "Solid"
                    ? ColorBrush(fillStyle.GetProperty("fg"), themeColors) : null;
                if (fillBrush is null &&
                    tableCell) fillBrush =
                        (Microsoft.UI.Xaml.Media.Brush)RootGrid
                            .Resources[tableHeader ? "TableHeaderFillBrush" :
                                alternateRow ? "TableAlternateFillBrush" : "TableBodyFillBrush"];
                TextBlock text = new()
                {
                    Text = value,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(style.GetProperty("name").GetString() ?? "Segoe UI"),
                    FontSize = style.GetProperty("size").GetDouble() * 96.0 / 72.0
                };
                if (address == activeCell)
                {
                    updatingFont = true;
                    FontNameInput.Text = style.GetProperty("name").GetString() ?? "";
                    FontSizeInput.Text = style.GetProperty("size").GetDouble().ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture);
                    updatingFont = false;
                }
                if (fontBrush is not null) text.Foreground = fontBrush;
                JsonElement alignment = fullStyle.GetProperty("align");
                text.TextWrapping = alignment.GetProperty("wrap").GetBoolean() ? TextWrapping.Wrap : TextWrapping.NoWrap;
                string? horizontal = alignment.GetProperty("h").GetString();
                HorizontalAlignment horizontalAlignment = horizontal switch
                {
                    "Center" or "CenterAcross" => HorizontalAlignment.Center,
                    "Right" => HorizontalAlignment.Right,
                    "General" when !tableHeader && cellData.GetProperty("numeric").GetBoolean() => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                };
                bool booleanCell = IsBooleanCell(value);
                if (booleanCell) horizontalAlignment = HorizontalAlignment.Center;
                text.HorizontalAlignment = horizontalAlignment;
                if (style.GetProperty("bold").GetBoolean() || tableHeader) text.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                if (style.GetProperty("italic").GetBoolean()) text.FontStyle = Windows.UI.Text.FontStyle.Italic;
                if (style.GetProperty("underline").GetString() != "None") text.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                SheetCellButton cell = new()
                {
                    Content = booleanCell ? CreateBooleanIcon(value) : text,
                    Tag = address,
                    Style = (Style)RootGrid.Resources[selected ? "SelectedCellStyle" : "SheetCellStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    FocusVisualPrimaryThickness = new Thickness(0),
                    FocusVisualSecondaryThickness = new Thickness(0),
                };
                AttachTableMenu(cell, view.GetProperty("tables"), absoluteRow, absoluteCol, address);
                if (tableStyle.ValueKind == JsonValueKind.Object)
                {
                    cell.BorderThickness = new Thickness(1, 1, 1, 1);
                }
                cell.Padding = new Thickness(5 + alignment.GetProperty("indent").GetInt32() * 12, 0, 5, 0);
                cell.HorizontalContentAlignment = horizontalAlignment;
                if (filterButton) cell.Padding = new Thickness(cell.Padding.Left, 0, 29, 0);
                cell.VerticalContentAlignment = alignment.GetProperty("v").GetString() switch
                {
                    "Top" => VerticalAlignment.Top,
                    "Bottom" => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center,
                };
                if (booleanCell)
                {
                    cell.Padding = new Thickness(0);
                    cell.VerticalContentAlignment = VerticalAlignment.Center;
                }
                if (selected)
                    cell.BorderThickness = new Thickness(absoluteCol == left ? 2 : 0, absoluteRow == top ? 2 : 0,
                        absoluteCol + spanColumns - 1 == right ? 2 : 0, absoluteRow + spanRows - 1 == bottom ? 2 : 0);
                AutomationProperties.SetName(cell, $"{address}: {value}{(selected ? "、選択中" : "")}");
                cell.Click += Cell_Click;
                cell.EnterPressed += Cell_KeyDown;
                cell.KeyDown += Cell_KeyDown;
                cell.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Cell_PointerPressed), true);
                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, col + 1);
                Grid.SetRowSpan(cell, spanRows);
                Grid.SetColumnSpan(cell, spanColumns);
                if (fillBrush is not null)
                {
                    Border fill = new() { Background = fillBrush, IsHitTestVisible = false };
                    Grid.SetRow(fill, row + 1);
                    Grid.SetColumn(fill, col + 1);
                    Grid.SetRowSpan(fill, spanRows);
                    Grid.SetColumnSpan(fill, spanColumns);
                    CellGrid.Children.Add(fill);
                }
                CellGrid.Children.Add(cell);
                if (tableCell)
                {
                    Border outline = new()
                    {
                        BorderBrush =
                        (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"],
                        BorderThickness = tableEdge,
                        IsHitTestVisible = false
                    };
                    Grid.SetRow(outline, row + 1);
                    Grid.SetColumn(outline, col + 1);
                    Grid.SetRowSpan(outline, spanRows);
                    Grid.SetColumnSpan(outline, spanColumns);
                    CellGrid.Children.Add(outline);
                }
                if (filterButton)
                {
                    bool activeFilter = IsTableColumnFiltered(view.GetProperty("tables"), absoluteRow, absoluteCol);
                    Button filter = new()
                    {
                        Content = new FontIcon { Glyph = activeFilter ? "\uE71C" : "\uE70D", FontSize = 10 },
                        Width = 24,
                        MinWidth = 0,
                        MinHeight = 0,
                        Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    string filterLabel = $"{value} のフィルター{(activeFilter ? "（適用中）" : "")}";
                    AutomationProperties.SetName(filter, filterLabel);
                    ToolTipService.SetToolTip(filter, filterLabel);
                    filter.Click += async (_, _) => await RunAsync(() => ShowTableFilterAsync(address));
                    Grid.SetRow(filter, row + 1);
                    Grid.SetColumn(filter, col + 1);
                    CellGrid.Children.Add(filter);
                }
            }
        }
        await AddDrawingObjectsAsync(view);
        if (requestedRow != firstRow || requestedColumn != firstColumn)
        {
            renderedRow = requestedRow;
            renderedColumn = requestedColumn;
            renderedRows = viewRows;
            renderedColumns = viewColumns;
            ShowScrollPreview();
            ScheduleScroll();
            return;
        }
        UpdateHeaderSelectionVisuals(top, left, bottom, right);
        UpdateSelectionCorner(bottom, right);
        DrawCopyOutline();
        renderedRow = firstRow;
        renderedColumn = firstColumn;
        renderedRows = viewRows;
        renderedColumns = viewColumns;
        renderedColumnScrollFraction = columnScrollFraction;
        viewportDirty = false;
        if (refreshSheets) await RefreshSheetsAsync();
        updatingScroll = true;
        scrollRowLimit = Math.Min(1_048_576, Math.Max(scrollRowLimit, firstRow + viewRows * 2));
        scrollColumnLimit = Math.Min(16_383, Math.Max(scrollColumnLimit, firstColumn + viewColumns * 2));
        RowScroll.Maximum = scrollRowLimit;
        ColumnScroll.Maximum = scrollColumnLimit;
        RowScroll.ViewportSize = viewRows;
        ColumnScroll.ViewportSize = viewColumns;
        RowScroll.LargeChange = viewRows;
        ColumnScroll.LargeChange = viewColumns;
        RowScroll.Value = firstRow;
        ColumnScroll.Value = firstColumn + columnScrollFraction;
        updatingScroll = false;
    }

    static bool IsTableHeader(JsonElement tables, int row, int col, out bool filterButton)
    {
        filterButton = false;
        foreach (JsonElement table in tables.EnumerateArray())
        {
            JsonElement range = table.GetProperty("range"), start = range.GetProperty("start"), end = range.GetProperty("end");
            int first = start.GetProperty("row").GetInt32();
            if (row == first && col >= start.GetProperty("col").GetInt32() && col <= end.GetProperty("col").GetInt32() &&
                table.GetProperty("header").GetBoolean())
            {
                filterButton = table.GetProperty("filterButton").GetBoolean();
                return true;
            }
        }
        return false;
    }
    static bool TryGetTableAppearance(JsonElement tables, int row, int col, int rowSpan, int columnSpan,
        out bool alternateRow, out Thickness edge)
    {
        alternateRow = false;
        edge = new Thickness(0);
        foreach (JsonElement table in tables.EnumerateArray())
        {
            JsonElement range = table.GetProperty("range"), start = range.GetProperty("start"), end = range.GetProperty("end");
            int top = start.GetProperty("row").GetInt32(), left = start.GetProperty("col").GetInt32();
            int bottom = end.GetProperty("row").GetInt32(), right = end.GetProperty("col").GetInt32();
            if (row < top || row > bottom || col < left || col > right) continue;
            int firstBodyRow = top + (table.GetProperty("header").GetBoolean() ? 1 : 0);
            alternateRow = (row - firstBodyRow) % 2 != 0;
            edge = new Thickness(col == left ? 1 : 0, row == top ? 1 : 0,
                col + columnSpan - 1 == right ? 1 : 0, row + rowSpan - 1 == bottom ? 1 : 0);
            return true;
        }
        return false;
    }
    static bool IsTableColumnFiltered(JsonElement tables, int row, int col)
    {
        foreach (JsonElement table in tables.EnumerateArray())
        {
            JsonElement range = table.GetProperty("range"), start = range.GetProperty("start"), end = range.GetProperty("end");
            if (row != start.GetProperty("row").GetInt32() || col < start.GetProperty("col").GetInt32() ||
                col > end.GetProperty("col").GetInt32()) continue;
            return table.TryGetProperty("filteredColumns",
                out JsonElement columns) && columns.ValueKind == JsonValueKind.Array &&
                    columns.EnumerateArray().Any(item => item.GetInt32() == col);
        }
        return false;
    }
}
