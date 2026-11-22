using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record ClipboardStyle(int Row, int Column, FontStyle Style);
    sealed record ClipboardMerge(int Top, int Left, int Bottom, int Right);
    sealed record ClipboardCells(string Text, int Rows, int Columns, int SourceRow, int SourceColumn, bool Cut,
        ClipboardStyle[] Styles, ClipboardMerge[] Merges, WorksheetTable[]? Tables = null, string[][]? Values = null);
    sealed record Snapshot(List<Sheet> Sheets, int ActiveSheet);
    readonly Stack<Snapshot> undo = new();
    readonly Stack<Snapshot> redo = new();

    internal bool CanUndo => undo.Count > 0 && CanUseHistory;
    internal bool CanRedo => redo.Count > 0 && CanUseHistory;
    internal bool CanUseHistory => !sheets.Any(sheet => sheet.ProtectionHash.HasValue);

    Snapshot Capture() => new(sheets.Select(CloneSheet).ToList(), activeSheet);
    static Sheet CloneSheet(Sheet source) => new(source.Name)
    {
        Cells = new(source.Cells),
        Fonts = new(source.Fonts),
        Merges = [.. source.Merges],
        RowHeights = new(source.RowHeights),
        ColumnWidths = new(source.ColumnWidths),
        TabColor = source.TabColor,
        ProtectionHash = source.ProtectionHash,
        Objects = [.. source.Objects],
        Tables = [.. source.Tables],
        Selection = source.Selection,
        Active = source.Active
    };
    void Restore(Snapshot snapshot)
    {
        sheets.Clear();
        sheets.AddRange(snapshot.Sheets.Select(CloneSheet));
        activeSheet = snapshot.ActiveSheet;
    }

    object ExecuteTracked(string command, JsonElement args)
    {
        if (command is "review.protectSheet" or "review.unprotectSheet")
        {
            object protectionResult = Execute(command, args);
            undo.Clear();
            redo.Clear();
            return protectionResult;
        }
        if (command == "edit.undo" || command == "edit.redo")
        {
            if (sheets.Any(sheet => sheet.ProtectionHash.HasValue)) throw new InvalidOperationException("保護されたシートがあるため、元に戻す操作はできません。");
            Stack<Snapshot> from = command == "edit.undo" ? undo : redo, to = command == "edit.undo" ? redo : undo;
            if (from.Count == 0) throw new InvalidOperationException(command == "edit.undo" ? "元に戻す操作がありません。" : "やり直す操作がありません。");
            to.Push(Capture());
            Restore(from.Pop());
            drawingHeightCache.Clear();
            filteredRowCache.Clear();
            return new { };
        }
        bool change = command is "cell.set"
            or "edit.clearContents"
            or "edit.cut"
            or "edit.paste"
            or "edit.autoFill"
            or "home.insertSheet"
            or "home.deleteSheet"
            or "sheet.rename"
            or "sheet.move"
            or "sheet.tabColor"
            or "home.rowHeight"
            or "home.columnWidth"
            or "home.autofitRowHeight"
            or "home.autofitColumnWidth"
            or "home.fontSize"
            or "home.fontName"
            or "home.bold"
            or "home.italic"
            or "home.underline"
            or "home.fontColor"
            or "home.fillColor"
            or "home.numberFormat"
            or "home.percent"
            or "home.comma"
            or "home.increaseDecimal"
            or "home.decreaseDecimal"
            or "home.alignLeft"
            or "home.alignCenter"
            or "home.alignRight"
            or "home.alignTop"
            or "home.alignMiddle"
            or "home.alignBottom"
            or "home.wrapText"
            or "home.increaseIndent"
            or "home.decreaseIndent"
            or "home.mergeCells"
            or "home.unmergeCells";
        change |= command is "edit.shiftCells" or "edit.moveRange" or "home.insertRows" or "home.insertColumns";
        change |= command is "insert.shape"
            or "insert.picture"
            or "insert.table"
            or "table.resize"
            or "table.delete"
            or "table.sort"
            or "table.filter"
            or "shape.change"
            or "shape.setText"
            or "chart.shapeFill"
            or "chart.shapeOutline"
            or "object.move"
            or "object.transform"
            or "image.crop"
            or "object.duplicate"
            or "object.delete"
            or "arrange.bringForward"
            or "arrange.sendBackward";
        if (command is "edit.copy"
            or "edit.cut"
            or "edit.clearContents"
            or "home.fontSize"
            or "home.fontName"
            or "home.bold"
            or "home.italic"
            or "home.underline"
            or "home.fontColor"
            or "home.fillColor"
            or "home.numberFormat"
            or "home.percent"
            or "home.comma"
            or "home.increaseDecimal"
            or "home.decreaseDecimal"
            or "home.alignLeft"
            or "home.alignCenter"
            or "home.alignRight"
            or "home.alignTop"
            or "home.alignMiddle"
            or "home.alignBottom"
            or "home.wrapText"
            or "home.increaseIndent"
            or "home.decreaseIndent"
            or "home.mergeCells"
            or "home.unmergeCells")
            EnsureVisibleRows(Current.Selection);
        if (command == "cell.set") EnsureVisibleRows(args.GetProperty("cell").GetString() ?? "");
        if (command == "edit.autoFill") EnsureVisibleRows(args.GetProperty("target").GetString() ?? "");
        if (change && Current.ProtectionHash.HasValue &&
            command is not ("home.insertSheet" or "home.deleteSheet" or "sheet.rename" or "sheet.move" or "sheet.tabColor"))
            throw new InvalidOperationException("このシートは保護されています。編集するには保護を解除してください。");
        Snapshot? before = change ? Capture() : null;
        object result = Execute(command, args);
        if (change || command == "file.open")
        {
            drawingHeightCache.Clear();
            filteredRowCache.Clear();
        }
        if (change && Current.Tables.Any(table => table.Filters is { Length: > 0 })) EnsureActiveRowVisible();
        if (before is not null)
        {
            undo.Push(before);
            redo.Clear();
        }
        if (command == "file.open")
        {
            undo.Clear();
            redo.Clear();
        }
        return result;
    }

    object CopyCells(bool cut)
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        if ((long)(bottom - top + 1) * (right - left + 1) > 1_000_000) throw new InvalidOperationException("選択範囲が大きすぎます。");
        CellRange range = new(top, left, bottom, right);
        if (Current.Merges.Any(m => m.Intersects(range) && (m.Top < top || m.Left < left || m.Bottom > bottom ||
            m.Right > right))) throw new InvalidOperationException("結合セル全体を選択してコピーしてください。");
        List<string> rows = [];
        List<string[]> values = [];
        for (int row = top; row <= bottom; row++)
        {
            List<string> cells = [];
            List<string> valueCells = [];
            for (int col = left; col <= right; col++)
            {
                string value = Current.Cells.GetValueOrDefault((row, col), "");
                valueCells.Add(DisplayCell(Current, (row, col)).Text);
                if (value.IndexOfAny(['\t', '\r', '\n', '"']) >= 0) value = '"' + value.Replace("\"", "\"\"") + '"';
                cells.Add(value);
            }
            rows.Add(string.Join('\t', cells));
            values.Add([.. valueCells]);
        }
        string text = string.Join("\r\n", rows);
        ClipboardStyle[] styles = Current.Fonts.Where(p => p.Key.Row >= top && p.Key.Row <= bottom && p.Key.Col >= left &&
            p.Key.Col <= right)
            .Select(p => new ClipboardStyle(p.Key.Row - top, p.Key.Col - left, p.Value)).ToArray();
        ClipboardMerge[] merges = Current.Merges.Where(m => m.Intersects(range)).Select(m => new ClipboardMerge(m.Top - top,
            m.Left - left, m.Bottom - top, m.Right - left)).ToArray();
        WorksheetTable[] tables = Current.Tables.Where(t => t.Top >= top && t.Left >= left && t.Bottom <= bottom && t.Right <= right)
            .Select(t => t with
            {
                Top = t.Top - top,
                Left = t.Left - left,
                Bottom = t.Bottom - top,
                Right = t.Right - left,
                Filters = t.Filters?.Select(f => f with { Column = f.Column - left }).ToArray()
            }).ToArray();
        string data = JsonSerializer.Serialize(new ClipboardCells(text, bottom - top + 1, right - left + 1, top, left, cut,
            styles, merges, tables, [.. values]));
        if (cut)
        {
            foreach ((int row, int col) in Current.Cells.Keys.Where(p => p.Row >= top && p.Row <= bottom && p.Col >= left &&
                p.Col <= right).ToArray()) Current.Cells.Remove((row, col));
            foreach ((int row, int col) in Current.Fonts.Keys.Where(p => p.Row >= top && p.Row <= bottom && p.Col >= left &&
                p.Col <= right).ToArray()) Current.Fonts.Remove((row, col));
            Current.Merges.RemoveAll(m => m.Intersects(range));
            Current.Tables.RemoveAll(t => t.Top >= top && t.Left >= left && t.Bottom <= bottom && t.Right <= right);
        }
        return new { text, data };
    }

    void PasteCells(string text, string? data, string mode)
    {
        if (mode is not ("all" or "values" or "formulas" or "formats")) throw new InvalidOperationException("貼り付けの種類が無効です。");
        List<List<string>> rows = ParseClipboard(text);
        (int startRow, int startCol) = ParseCell(Current.Active);
        if (rows.Count > 1048576 - startRow || rows.Any(r => r.Count > 16384 - startCol) ||
            rows.Sum(r => (long)r.Count) > 1_000_000) throw new InvalidOperationException("貼り付け範囲が大きすぎます。");
        ClipboardCells? clipboard = null;
        if (data is not null)
        {
            try
            {
                clipboard = JsonSerializer.Deserialize<ClipboardCells>(data);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("クリップボードの書式を読み取れません。");
            }
            if (clipboard is null || clipboard.Text != text || clipboard.Rows != rows.Count || clipboard.Columns < 1 ||
                rows.Any(r => r.Count != clipboard.Columns) || clipboard.SourceRow < 0 || clipboard.SourceColumn < 0 ||
                    clipboard.SourceRow > 1048576 - clipboard.Rows || clipboard.SourceColumn > 16384 - clipboard.Columns ||
                        clipboard.Styles is null || clipboard.Merges is null || clipboard.Styles.Any(s => s is null ||
                            s.Style is null || s.Row < 0 || s.Row >= clipboard.Rows || s.Column < 0 ||
                                s.Column >= clipboard.Columns || !ValidStyle(s.Style)) ||
                                    clipboard.Merges.Any(m => m is null || m.Top < 0 || m.Left < 0 ||
                                        m.Bottom >= clipboard.Rows || m.Right >= clipboard.Columns || m.Top > m.Bottom ||
                                            m.Left > m.Right || (m.Top == m.Bottom && m.Left == m.Right)))
                throw new InvalidOperationException("クリップボードの書式が無効です。");
            if (clipboard.Values is not null && (clipboard.Values.Length != clipboard.Rows ||
                clipboard.Values.Any(row => row is null || row.Length != clipboard.Columns ||
                    row.Any(value => value is null)))) throw new InvalidOperationException("クリップボードの値が無効です。");
            List<CellRange> pastedMerges = [];
            foreach (ClipboardMerge merge in clipboard.Merges)
            {
                CellRange merged = new(startRow + merge.Top, startCol + merge.Left, startRow + merge.Bottom, startCol + merge.Right);
                if (pastedMerges.Any(m => m.Intersects(merged))) throw new InvalidOperationException("クリップボードの結合セルが重なっています。");
                if (mode == "all" && Current.Tables.Any(table => new CellRange(table.Top, table.Left, table.Bottom,
                    table.Right).Intersects(merged))) throw new InvalidOperationException("テーブルと重なる範囲には結合セルを貼り付けできません。");
                pastedMerges.Add(merged);
            }
        }
        List<WorksheetTable> pastedTables = [];
        if (mode == "all" && clipboard?.Tables is not null)
        {
            foreach (WorksheetTable table in clipboard.Tables)
            {
                if (table is null || table.Top < 0 || table.Left < 0 || table.Bottom >= clipboard.Rows ||
                    table.Right >= clipboard.Columns || table.Top > table.Bottom || table.Left > table.Right ||
                    (table.Filters is not null && (table.Filters.Any(f => f is null || f.Column < table.Left ||
                        f.Column > table.Right || f.Values is null || f.Values.Any(v => v is null)) ||
                            table.Filters.Select(f => f.Column).Distinct().Count() != table.Filters.Length ||
                                (!table.Header && table.Filters.Length > 0))))
                    throw new InvalidOperationException("クリップボードのテーブル情報が無効です。");
                WorksheetTable moved = table with
                {
                    Top = startRow + table.Top,
                    Left = startCol + table.Left,
                    Bottom = startRow + table.Bottom,
                    Right = startCol + table.Right,
                    Filters = table.Filters?.Select(f => f with { Column = startCol + f.Column }).ToArray()
                };
                CellRange tableRange = new(moved.Top, moved.Left, moved.Bottom, moved.Right);
                if (Current.Tables.Concat(pastedTables).Any(t => new CellRange(t.Top, t.Left, t.Bottom, t.Right).Intersects(tableRange)) ||
                    Current.Merges.Any(m => m.Intersects(tableRange)) ||
                        clipboard.Merges.Any(m => new CellRange(startRow + m.Top, startCol + m.Left, startRow + m.Bottom,
                            startCol + m.Right).Intersects(tableRange)))
                    throw new InvalidOperationException("テーブルや結合セルに重なる範囲にはテーブルを貼り付けできません。");
                pastedTables.Add(moved);
            }
        }
        if (mode == "formats" && clipboard is null) throw new InvalidOperationException("書式のみの貼り付けには Chlorella からコピーしたセルが必要です。");
        if (mode == "values" && clipboard?.Values is null &&
            rows.Any(row => row.Any(value =>
                value.StartsWith('=')))) throw new InvalidOperationException("数式の計算結果を取得できません。コピー元で値をコピーしてください。");
        int width = rows.Max(r => r.Count);
        CellRange destination = new(startRow, startCol, startRow + rows.Count - 1, startCol + width - 1);
        EnsureVisibleRows($"{Address(destination.Top, destination.Left)}:{Address(destination.Bottom, destination.Right)}");
        if (Current.Merges.Any(m => m.Intersects(destination))) throw new InvalidOperationException("結合セルに重なる範囲には貼り付けできません。結合を解除してください。");
        for (int r = 0; r < rows.Count; r++) for (int c = 0; c < rows[r].Count; c++)
            {
                (int, int) key = (startRow + r, startCol + c);
                string value = NormalizeFullWidthDigits(rows[r][c]);
                if (mode == "values" && clipboard?.Values is not null) value = clipboard.Values[r][c];
                if (mode != "values" && clipboard is not null && !clipboard.Cut &&
                    value.StartsWith('=')) value = ShiftFormula(value, startRow - clipboard.SourceRow, startCol - clipboard.SourceColumn);
                if (mode != "formats")
                {
                    if (value.Length == 0) Current.Cells.Remove(key);
                    else Current.Cells[key] = value;
                }
                if (mode is "all" or "formats" && clipboard is not null) Current.Fonts.Remove(key);
            }
        if (mode is "all" or "formats" &&
            clipboard is not null) foreach (ClipboardStyle item in clipboard.Styles) if (
                item.Style != DefaultFont) Current.Fonts[(startRow + item.Row, startCol + item.Column)] = item.Style;
        if (mode == "all" &&
            clipboard is not null) foreach (ClipboardMerge merge in clipboard.Merges) Current.Merges.Add(new CellRange(
                startRow + merge.Top, startCol + merge.Left, startRow + merge.Bottom, startCol + merge.Right));
        Current.Tables.AddRange(pastedTables);
    }

    static List<List<string>> ParseClipboard(string text)
    {
        List<List<string>> rows = [[]];
        System.Text.StringBuilder value = new();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"' && quoted && i + 1 < text.Length && text[i + 1] == '"')
            {
                value.Append('"');
                i++;
            }
            else if (ch == '"' && (quoted || value.Length == 0)) quoted = !quoted;
            else if (!quoted && ch is '\t' or '\r' or '\n')
            {
                rows[^1].Add(value.ToString());
                value.Clear();
                if (ch != '\t')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    if (i + 1 < text.Length) rows.Add([]);
                }
            }
            else value.Append(ch);
        }
        if (quoted) throw new InvalidOperationException("クリップボードの引用符が閉じられていません。");
        if (value.Length > 0 || rows[^1].Count == 0 || text.EndsWith('\t')) rows[^1].Add(value.ToString());
        return rows;
    }

    void MoveSheet(JsonElement args)
    {
        Sheet sheet = FindSheet(args.GetProperty("sheet").GetString());
        int target = Math.Clamp(args.GetProperty("to").GetInt32(), 0, sheets.Count);
        bool copy = args.TryGetProperty("copy", out JsonElement copied) && copied.ValueKind == JsonValueKind.True;
        Sheet active = Current;
        if (copy)
        {
            sheet = CloneSheet(sheet);
            string baseName = sheet.Name;
            int suffix = 2;
            do
            {
                string ending = $" ({suffix++})";
                sheet.Name = baseName[..Math.Min(baseName.Length, 31 - ending.Length)] + ending;
            }
            while (sheets.Any(s => s.Name.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            sheets.Remove(sheet);
            target = Math.Min(target, sheets.Count);
        }
        sheets.Insert(target, sheet);
        activeSheet = sheets.IndexOf(active);
    }
}
