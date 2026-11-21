using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record DrawingObject(uint Id, string Kind, string Type, int Row, int Col, double Dx, double Dy, double Width, double Height, uint? Fill, uint? Line, string Text, string? Data);
    sealed record TableFilter(int Column, string[] Values);
    sealed record WorksheetTable(int Top, int Left, int Bottom, int Right, bool Header, TableFilter[]? Filters = null);

    object ShapeView(DrawingObject item, double x, double y) => new { id = item.Id, kind = item.Type, anchor = Anchor(item, x, y), fill = Color(item.Fill), line = Color(item.Line), text = item.Text, format = new { text_color = Color(0xffffff) } };
    object ImageView(DrawingObject item, double x, double y) => new { id = item.Id, anchor = Anchor(item, x, y), alt = item.Text };
    static object Anchor(DrawingObject item, double x, double y) => new { cell = new { row = item.Row, col = item.Col }, dx = item.Dx, dy = item.Dy, width = item.Width, height = item.Height, viewportX = x, viewportY = y };
    static object? Color(uint? color) => color is uint rgb ? new { Rgb = rgb } : null;
    object ReadPicture(JsonElement args)
    {
        DrawingObject item = FindDrawing("image", args.GetProperty("id").GetUInt32());
        return new { base64 = item.Data };
    }
    DrawingObject FindDrawing(string kind, uint id) => Current.Objects.FirstOrDefault(item => item.Kind == kind && item.Id == id) ?? throw new InvalidOperationException("図形または画像が見つかりません。");
    uint NextDrawingId() => Current.Objects.Count == 0 ? 1 : checked(Current.Objects.Max(item => item.Id) + 1);
    static string ShapeType(string kind) => kind.ToLowerInvariant() switch
    {
        "rectangle" => "Rectangle", "roundedrectangle" => "RoundedRectangle", "ellipse" => "Ellipse", "triangle" => "Triangle", "diamond" => "Diamond", "pentagon" => "Pentagon", "hexagon" => "Hexagon", "line" => "Line", "arrow" => "Arrow", _ => throw new InvalidOperationException("図形の種類が無効です。")
    };
    object ExecuteDrawing(string command, JsonElement args)
    {
        if (command is "table.resize" or "table.delete")
        {
            (int row, int col) = ParseCell(args.GetProperty("cell").GetString() ?? "");
            WorksheetTable table = Current.Tables.FirstOrDefault(t => row >= t.Top && row <= t.Bottom && col >= t.Left && col <= t.Right) ?? throw new InvalidOperationException("テーブルが見つかりません。");
            if (command == "table.delete") { Current.Tables.Remove(table); return new { }; }
            (int top, int left, int bottom, int right) = ParseRange(args.GetProperty("range").GetString() ?? "");
            CellRange range = new(top, left, bottom, right);
            if ((long)(bottom - top + 1) * (right - left + 1) > 100_000 || Current.Merges.Any(m => m.Intersects(range)) || Current.Tables.Any(t => t != table && new CellRange(t.Top, t.Left, t.Bottom, t.Right).Intersects(range))) throw new InvalidOperationException("テーブルの範囲が無効か、結合セルまたは別のテーブルと重なっています。");
            TableFilter[]? filters = table.Filters?.Where(filter => filter.Column >= left && filter.Column <= right).ToArray();
            Current.Tables[Current.Tables.IndexOf(table)] = new(top, left, bottom, right, table.Header, filters);
            return new { };
        }
        if (command == "insert.table")
        {
            (int top, int left, int bottom, int right) = ParseRange(args.GetProperty("range").GetString() ?? "");
            if ((long)(bottom - top + 1) * (right - left + 1) > 100_000) throw new InvalidOperationException("テーブルは 100,000 セルまでです。");
            CellRange range = new(top, left, bottom, right);
            CellRange? merged = Current.Merges.FirstOrDefault(m => m.Intersects(range));
            if (merged is not null) throw new InvalidOperationException($"指定範囲は結合セル {Address(merged.Top, merged.Left)}:{Address(merged.Bottom, merged.Right)} と重なっています。");
            WorksheetTable? overlapping = Current.Tables.FirstOrDefault(t => new CellRange(t.Top, t.Left, t.Bottom, t.Right).Intersects(range));
            if (overlapping is not null) throw new InvalidOperationException($"既存テーブル {Address(overlapping.Top, overlapping.Left)}:{Address(overlapping.Bottom, overlapping.Right)} と重複しています。");
            Current.Tables.Add(new(top, left, bottom, right, args.GetProperty("header").GetBoolean()));
            return new { };
        }
        if (command is "insert.shape" or "insert.picture")
        {
            (int row, int col) = ParseCell(args.GetProperty("at").GetString() ?? "");
            string? data = null, type = "", text = "";
            if (command == "insert.picture")
            {
                string path = args.GetProperty("path").GetString() ?? "";
                FileInfo info = new(path);
                if (info.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidOperationException("画像は 16 MiB 以下にしてください。");
                byte[] bytes = File.ReadAllBytes(path);
                if (!(bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) && !(bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)) throw new InvalidOperationException("PNG または JPEG 画像を選択してください。");
                data = Convert.ToBase64String(bytes); text = Path.GetFileName(path);
                if (Current.Objects.Sum(item => (long)(item.Data?.Length ?? 0)) + data.Length > 60 * 1024 * 1024) throw new InvalidOperationException("シート内の画像の合計が大きすぎます。");
            }
            else type = ShapeType(args.GetProperty("kind").GetString() ?? "");
            Current.Objects.Add(new(NextDrawingId(), command == "insert.shape" ? "shape" : "image", type, row, col, 0, 0, 160, 100, 0x4472c4, 0x244a80, text, data));
            return new { };
        }
        string kind = command.StartsWith("shape.") || command.StartsWith("chart.") ? "shape" : args.GetProperty("kind").GetString() ?? "";
        DrawingObject original = FindDrawing(kind, args.GetProperty("id").GetUInt32());
        int index = Current.Objects.IndexOf(original);
        if (command == "object.delete") { Current.Objects.RemoveAt(index); return new { }; }
        if (command is "arrange.bringForward" or "arrange.sendBackward")
        {
            List<int> positions = Current.Objects.Select((item, i) => (item, i)).Where(pair => pair.item.Kind == kind).Select(pair => pair.i).ToList();
            int order = positions.IndexOf(index), destination = command == "arrange.bringForward" ? (args.TryGetProperty("toFront", out JsonElement front) && front.ValueKind == JsonValueKind.True ? positions.Count - 1 : Math.Min(order + 1, positions.Count - 1)) : (args.TryGetProperty("toBack", out JsonElement back) && back.ValueKind == JsonValueKind.True ? 0 : Math.Max(order - 1, 0));
            Current.Objects.RemoveAt(index); Current.Objects.Insert(positions[destination], original); return new { };
        }
        DrawingObject changed = original;
        if (command == "shape.change") changed = original with { Type = ShapeType(args.GetProperty("type").GetString() ?? "") };
        if (command == "shape.setText") changed = original with { Text = args.GetProperty("text").GetString() ?? "" };
        if (command == "chart.shapeFill") changed = original with { Fill = ParseColor(args.GetProperty("color")) };
        if (command == "chart.shapeOutline") changed = original with { Line = ParseColor(args.GetProperty("color")) };
        if (command is "object.move" or "object.duplicate")
        {
            int row = original.Row, col = original.Col;
            if (args.TryGetProperty("at", out JsonElement at) && at.ValueKind == JsonValueKind.String) (row, col) = ParseCell(at.GetString() ?? "");
            double dx = args.TryGetProperty("dx", out JsonElement x) ? x.GetDouble() : original.Dx + (command == "object.duplicate" ? 10 : 0);
            double dy = args.TryGetProperty("dy", out JsonElement y) ? y.GetDouble() : original.Dy + (command == "object.duplicate" ? 10 : 0);
            double width = args.TryGetProperty("width", out JsonElement w) ? w.GetDouble() : original.Width;
            double height = args.TryGetProperty("height", out JsonElement h) ? h.GetDouble() : original.Height;
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(width) || !double.IsFinite(height) || width is < 4 or > 10000 || height is < 4 or > 10000 || Math.Abs(dx) > 100000 || Math.Abs(dy) > 100000) throw new InvalidOperationException("図形の位置またはサイズが無効です。");
            Dictionary<int, double> heights = DrawingRowHeights();
            Dictionary<int, double> widths = Current.ColumnWidths.ToDictionary(pair => pair.Key, pair => Math.Max(20, pair.Value));
            (col, dx) = NormalizeDrawingPosition(col, dx, widths, 100, 16383);
            (row, dy) = NormalizeDrawingPosition(row, dy, heights, 24, 1048575);
            changed = original with { Row = row, Col = col, Dx = dx, Dy = dy, Width = width, Height = height };
        }
        if (command == "object.duplicate")
        {
            if (Current.Objects.Sum(item => (long)(item.Data?.Length ?? 0)) + (changed.Data?.Length ?? 0) > 60 * 1024 * 1024) throw new InvalidOperationException("シート内の画像の合計が大きすぎます。");
            Current.Objects.Add(changed with { Id = NextDrawingId() });
        }
        else Current.Objects[index] = changed;
        return new { };
    }
}
