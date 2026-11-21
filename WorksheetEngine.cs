using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chlorella;

// The in-process implementation of the UI's worksheet protocol.
sealed partial class WorksheetEngine : IWorksheetDataSource
{
    sealed class Sheet(string name)
    {
        public string Name = name;
        public Dictionary<(int Row, int Col), string> Cells = new();
        public Dictionary<(int Row, int Col), FontStyle> Fonts = new();
        public List<CellRange> Merges = [];
        public Dictionary<int, double> RowHeights = new();
        public Dictionary<int, double> ColumnWidths = new();
        public uint? TabColor;
        public ushort? ProtectionHash;
        public List<DrawingObject> Objects = [];
        public List<WorksheetTable> Tables = [];
        public string Selection = "A1";
        public string Active = "A1";
    }

    readonly List<Sheet> sheets = [new("Sheet1")];
    int activeSheet;
    string? documentPath;
    string? importedSourcePath;
    Sheet Current => sheets[activeSheet];

    public Task<JsonElement> SendAsync(string method, object parameters)
    {
        JsonElement args = JsonSerializer.SerializeToElement(parameters);
        object result = method switch
        {
            "sheet.viewport" => Viewport(args),
            "sheet.picture" => ReadPicture(args),
            "sheet.nextVisibleRow" => NextVisibleRow(args),
            "sheet.resolveVisibleRow" => ResolveVisibleRow(args),
            "file.rename" => RenameFile(args),
            "engine.execute" => ExecuteTracked(args.GetProperty("command").GetString() ?? "", args.GetProperty("params")),
            _ => throw new NotSupportedException($"未対応の操作です: {method}"),
        };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    object Execute(string command, JsonElement args)
    {
        switch (command)
        {
            case "document.inspect":
                return new
                {
                    path = documentPath,
                    activeSheet,
                    sheets = sheets.Select(s => new
                    {
                        name = s.Name,
                        visibility = "Visible",
                        tabColor = s.TabColor is uint color ? $"#{color:X6}" : null,
                        @protected = s.ProtectionHash.HasValue
                    }).ToArray()
                };
            case "review.protectSheet":
            case "review.unprotectSheet":
                SetSheetProtection(command, args);
                return new { };
            case "file.saveAs": return new { headerlessTables = SaveWorkbook(args.GetProperty("path").GetString() ?? "") };
            case "file.save":
                return new
                {
                    headerlessTables = SaveWorkbook(documentPath ??
                throw new InvalidOperationException("保存先を指定してください。"))
                };
            case "file.open": return OpenWorkbook(args.GetProperty("path").GetString() ?? "");
            case "file.exportCsv":
                ExportText(args.GetProperty("path").GetString() ?? "", false);
                return new { };
            case "file.exportHtml":
                ExportText(args.GetProperty("path").GetString() ?? "", true);
                return new { };
            case "file.exportPdf":
                ExportPdf(args.GetProperty("path").GetString() ?? "");
                return new { };
            case "edit.copy": return CopyCells(false);
            case "edit.cut": return CopyCells(true);
            case "edit.paste":
                PasteCells(args.GetProperty("text").GetString() ?? "", args.TryGetProperty("data",
                    out JsonElement data) && data.ValueKind == JsonValueKind.String ? data.GetString() : null,
                        args.TryGetProperty("mode", out JsonElement pasteMode) ? pasteMode.GetString() ?? "all" : "all");
                return new { };
            case "edit.autoFill":
                AutoFill(args);
                return new { };
            case "edit.moveRange":
                MoveRange(args);
                return new { };
            case "home.mergeCells":
                MergeCells();
                return new { };
            case "home.unmergeCells":
                UnmergeCells();
                return new { };
            case "sheet.move":
                MoveSheet(args);
                return new { };
            case "table.sort":
                SortTable(args);
                return new { };
            case "table.filterChoices": return FilterChoices(args);
            case "table.filter":
                ApplyTableFilter(args);
                return new { };
            case "insert.shape":
            case "insert.picture":
            case "insert.table":
            case "table.resize":
            case "table.delete":
            case "shape.change":
            case "shape.setText":
            case "chart.shapeFill":
            case "chart.shapeOutline":
            case "object.move":
            case "object.duplicate":
            case "object.delete":
            case "arrange.bringForward":
            case "arrange.sendBackward": return ExecuteDrawing(command, args);
            case "edit.shiftCells":
                ShiftCells(args);
                return new { };
            case "home.insertRows":
            case "home.insertColumns":
                InsertCells(command, args);
                return new { };
            case "sheet.tabColor":
                FindSheet(args.GetProperty("sheet").GetString()).TabColor = ParseColor(args.GetProperty("color"));
                return new { };
            case "home.rowHeight":
            case "home.columnWidth":
            case "home.autofitRowHeight":
            case "home.autofitColumnWidth":
                SetDimensions(command, args);
                return new { };
            case "home.fontSize":
            case "home.fontName":
            case "home.bold":
            case "home.italic":
            case "home.underline":
                ApplyFont(command, args);
                return new { };
            case "home.fontColor":
            case "home.fillColor":
            case "home.numberFormat":
            case "home.percent":
            case "home.comma":
            case "home.increaseDecimal":
            case "home.decreaseDecimal":
            case "home.alignLeft":
            case "home.alignCenter":
            case "home.alignRight":
            case "home.alignTop":
            case "home.alignMiddle":
            case "home.alignBottom":
            case "home.wrapText":
            case "home.increaseIndent":
            case "home.decreaseIndent":
                ApplyCellFormat(command, args);
                return new { };
            case "selection.set":
                string selection = args.GetProperty("range").GetString() ?? "A1";
                (int top, int left, _, _) = ParseRange(selection);
                string selectedActive = args.TryGetProperty("active",
                    out JsonElement active) && active.ValueKind == JsonValueKind.String ? active.GetString() ?? Address(top,
                        left) : Address(top, left);
                (int activeRow, int activeCol) = ParseCell(selectedActive);
                if (FilteredRows().Contains(activeRow)) throw new InvalidOperationException("フィルターで非表示の行は選択できません。");
                CellRange? merged = Current.Merges.FirstOrDefault(m => m.Contains(activeRow, activeCol));
                if (merged is not null && FilteredRows().Contains(merged.Top)) throw new InvalidOperationException("結合セルの先頭行がフィルターで非表示です。");
                Current.Selection = selection;
                Current.Active = selectedActive;
                if (merged is not null)
                {
                    Current.Active = Address(merged.Top, merged.Left);
                    (int sTop, int sLeft, int sBottom, int sRight) = ParseRange(selection);
                    if (sTop == sBottom &&
                        sLeft == sRight) Current.Selection = $"{Address(merged.Top, merged.Left)}:{Address(merged.Bottom, merged.Right)}";
                }
                Current.Cells.TryGetValue(ParseCell(Current.Active), out string? selectedInput);
                (int selectedTop, int selectedLeft, int selectedBottom, int selectedRight) = ParseRange(Current.Selection);
                return new
                {
                    activeCell = Current.Active,
                    selection = Current.Selection,
                    input = selectedInput ?? "",
                    selectionBounds = new { start = new { row = selectedTop, col = selectedLeft },
                        end = new { row = selectedBottom, col = selectedRight } }
                };
            case "cell.set":
                (int row, int col) = ParseCell(args.GetProperty("cell").GetString() ?? "");
                string input = CompleteFormulaParentheses(NormalizeFullWidthDigits(args.GetProperty("input").GetString() ?? ""));
                if (input.Length == 0) Current.Cells.Remove((row, col));
                else Current.Cells[(row, col)] = input;
                return new { };
            case "edit.clearContents":
                (int r1, int c1, int r2, int c2) = ParseRange(Current.Selection);
                foreach ((int r, int c) in Current.Cells.Keys.Where(p => p.Row >= r1 && p.Row <= r2 && p.Col >= c1 &&
                    p.Col <= c2).ToArray()) Current.Cells.Remove((r, c));
                return new { };
            case "home.insertSheet":
                int number = 1;
                while (sheets.Any(s => s.Name.Equals($"Sheet{number}", StringComparison.OrdinalIgnoreCase))) number++;
                sheets.Add(new Sheet($"Sheet{number}"));
                activeSheet = sheets.Count - 1;
                return new { };
            case "sheet.activate":
                int index = args.GetProperty("sheet").GetInt32();
                if (index < 0 || index >= sheets.Count) throw new InvalidOperationException("シートが見つかりません。");
                activeSheet = index;
                return new { };
            case "sheet.rename":
                Sheet sheet = FindSheet(args.GetProperty("sheet").GetString());
                string name = (args.GetProperty("name").GetString() ?? "").Trim();
                if (name.Length is 0 or > 31 || name.IndexOfAny([':', '\\', '/', '?', '*', '[',
                    ']']) >= 0 || sheets.Any(s => s != sheet && s.Name.Equals(name,
                        StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("シート名が無効か、既に使用されています。");
                sheet.Name = name;
                return new { };
            case "home.deleteSheet":
                if (sheets.Count == 1) throw new InvalidOperationException("最後のシートは削除できません。");
                Sheet removed = FindSheet(args.GetProperty("sheet").GetString());
                int removedIndex = sheets.IndexOf(removed);
                sheets.RemoveAt(removedIndex);
                if (removedIndex < activeSheet) activeSheet--;
                else if (activeSheet >= sheets.Count) activeSheet = sheets.Count - 1;
                return new { };
            default: throw new NotSupportedException($"この操作はまだ実装されていません: {command}");
        }
    }

    Sheet FindSheet(string? name) => sheets.FirstOrDefault(s => s.Name == name) ?? throw new InvalidOperationException("シートが見つかりません。");

    object Viewport(JsonElement args)
    {
        (int top, int left, int bottom, int right) = ParseRange(args.GetProperty("ranges")[0].GetString() ?? "A1");
        HashSet<int> hiddenRows = FilteredRows();
        List<FontStyle> fonts = [DefaultFont];
        foreach (FontStyle font in Current.Fonts.Where(p => p.Key.Row >= top && p.Key.Row <= bottom && p.Key.Col >= left &&
            p.Key.Col <= right).Select(p => p.Value).Distinct()) if (!fonts.Contains(font)) fonts.Add(font);
        foreach (CellRange merge in Current.Merges.Where(m => m.Bottom >= top && m.Top <= bottom && m.Right >= left && m.Left <= right))
        {
            FontStyle font = Current.Fonts.GetValueOrDefault((merge.Top, merge.Left), DefaultFont);
            if (!fonts.Contains(font)) fonts.Add(font);
        }
        Dictionary<int, double> heights = Current.Fonts.Where(p => p.Key.Row >= top && p.Key.Row <= bottom).GroupBy(p => p.Key.Row)
            .ToDictionary(g => g.Key, g => Math.Max(24d, g.Max(p => EstimateRowHeight(Current, p.Key, p.Value))));
        List<(DrawingObject Item, double X, double Y)> visibleObjects = VisibleDrawingObjects(top, left, bottom, right);
        object[][] cells = Enumerable.Range(top, bottom - top + 1).Select(row => Enumerable.Range(left, right - left + 1).Select(col =>
        {
            FontStyle format = Current.Fonts.GetValueOrDefault((row, col), DefaultFont);
            (string displayed, bool numeric) = FormatCell(Current, (row, col), format);
            int style = fonts.IndexOf(format);
            return (object)new { text = displayed, style, numeric };
        }).ToArray()).ToArray();
        (int r1, int c1, int r2, int c2) = ParseRange(Current.Selection);
        Current.Cells.TryGetValue(ParseCell(Current.Active), out string? input);
        return new
        {
            sheet = Current.Name,
            activeCell = Current.Active,
            selection = Current.Selection,
            input = input ?? "",
            selectionBounds = new { start = new { row = r1, col = c1 }, end = new { row = r2, col = c2 } },
            regions = new[] { new { cells, columnWidths = Enumerable.Range(left,
                right - left + 1).Select(col => Current.ColumnWidths.GetValueOrDefault(col, 100d)).ToArray(),
                    rowHeights = Enumerable.Range(top,
                        bottom - top + 1).Select(row =>
                            hiddenRows.Contains(row) ? 0d : Current.RowHeights.GetValueOrDefault(row,
                                heights.GetValueOrDefault(row, 24d))).ToArray() } },
            styles = fonts.Select((font, index) => (font,
                index)).ToDictionary(p => p.index.ToString(CultureInfo.InvariantCulture),
                    p => (object)new
                    {
                        font = new
                        {
                            name = p.font.Name,
                            size = p.font.Size,
                            color = new { Rgb = p.font.Color ?? 0 },
                            bold = p.font.Bold,
                            italic = p.font.Italic,
                            underline = p.font.Underline ? "Single" : "None"
                        },
                        fill = new
                        {
                            pattern = p.font.Fill.HasValue ? "Solid" : "None",
                            fg = new { Rgb = p.font.Fill ?? 0 }
                        },
                        align = new
                        {
                            wrap = p.font.Wrap,
                            h = p.font.Horizontal,
                            v = p.font.Vertical,
                            indent = p.font.Indent
                        },
                        numberFormat = p.font.Format
                    }),
            theme = new
            {
                colors = new uint[] { 0xffffff, 0, 0xeeeeee, 0x44546a, 0x4472c4, 0xed7d31, 0xa5a5a5, 0xffc000,
                0x5b9bd5, 0x70ad47, 0x0563c1, 0x954f72 }
            },
            merges = Current.Merges.Where(m => m.Bottom >= top && m.Top <= bottom && m.Right >= left &&
                m.Left <= right).Select(m => new
                {
                    range = new
                    {
                        start = new { row = m.Top, col = m.Left },
                        end = new { row = m.Bottom, col = m.Right }
                    },
                    text = FormatCell(Current, (m.Top, m.Left),
                        Current.Fonts.GetValueOrDefault((m.Top, m.Left), DefaultFont)).Text,
                    style = fonts.IndexOf(Current.Fonts.GetValueOrDefault((m.Top, m.Left), DefaultFont))
                }).ToArray(),
            tables = Current.Tables.Select(t => new
            {
                range = new
                {
                    start = new { row = t.Top, col = t.Left },
                    end = new { row = t.Bottom, col = t.Right }
                },
                header = t.Header,
                filterButton = t.Header,
                filteredColumns = (t.Filters ?? []).Select(filter => filter.Column).ToArray()
            }).ToArray(),
            shapes = visibleObjects.Where(o => o.Item.Kind == "shape").Select(o => ShapeView(o.Item, o.X, o.Y)).ToArray(),
            images = visibleObjects.Where(o => o.Item.Kind == "image").Select(o => ImageView(o.Item, o.X, o.Y)).ToArray()
        };
    }

    static (int Row, int Col) ParseCell(string address)
    {
        Match match = Regex.Match(address, @"^\$?([A-Za-z]{1,3})\$?([1-9][0-9]*)$");
        if (!match.Success || !int.TryParse(match.Groups[2].Value,
            out int row) || row > 1048576) throw new InvalidOperationException("セルの参照が無効です。");
        int col = 0;
        foreach (char c in match.Groups[1].Value.ToUpperInvariant()) col = col * 26 + c - 'A' + 1;
        if (col > 16384) throw new InvalidOperationException("セルの参照が無効です。");
        return (row - 1, col - 1);
    }

    static (int Top, int Left, int Bottom, int Right) ParseRange(string range)
    {
        string[] parts = range.Split(':');
        if (parts.Length is < 1 or > 2) throw new InvalidOperationException("範囲が無効です。");
        string first = parts[0], last = parts.Length == 2 ? parts[1] : first;
        if (Regex.IsMatch(first, @"^[A-Za-z]+$") && Regex.IsMatch(last, @"^[A-Za-z]+$"))
        {
            first += "1";
            last += "1048576";
        }
        else if (Regex.IsMatch(first, @"^[0-9]+$") && Regex.IsMatch(last, @"^[0-9]+$"))
        {
            first = "A" + first;
            last = "XFD" + last;
        }
        (int r1, int c1) = ParseCell(first);
        (int r2, int c2) = ParseCell(last);
        return (Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
    }

    static string Address(int row, int col)
    {
        string name = "";
        int n = col + 1;
        while (n > 0)
        {
            n--;
            name = (char)('A' + n % 26) + name;
            n /= 26;
        }
        return name + (row + 1).ToString(CultureInfo.InvariantCulture);
    }
}
