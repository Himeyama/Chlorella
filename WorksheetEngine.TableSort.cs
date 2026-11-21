using System.Globalization;
using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record SortRow(int Row, int Category, double Number, string Text);

    void SortTable(JsonElement args)
    {
        (int selectedRow, int selectedColumn) = ParseCell(args.GetProperty("cell").GetString() ?? "");
        WorksheetTable table = Current.Tables.FirstOrDefault(t => selectedRow >= t.Top && selectedRow <= t.Bottom && selectedColumn >= t.Left && selectedColumn <= t.Right) ?? throw new InvalidOperationException("テーブルが見つかりません。");
        if (FilteredRows().Any(row => row >= table.Top && row <= table.Bottom)) throw new InvalidOperationException("非表示の行があるテーブルは、フィルターを解除してから並べ替えてください。");
        string direction = args.GetProperty("direction").GetString() ?? "";
        if (direction is not ("ascending" or "descending")) throw new InvalidOperationException("並べ替えの方向が無効です。");
        CellRange range = new(table.Top, table.Left, table.Bottom, table.Right);
        if (Current.Merges.Any(merge => merge.Intersects(range))) throw new InvalidOperationException("結合セルを含むテーブルは並べ替えできません。");
        int firstRow = table.Top + (table.Header ? 1 : 0);
        if (firstRow >= table.Bottom) return;
        List<SortRow> rows = [];
        for (int row = firstRow; row <= table.Bottom; row++)
        {
            (int, int) key = (row, selectedColumn);
            string raw = Current.Cells.GetValueOrDefault(key, "");
            (string text, bool numeric) = FormatCell(Current, key, Current.Fonts.GetValueOrDefault(key, DefaultFont));
            if (raw.Length == 0 || text.Length == 0) rows.Add(new(row, 2, 0, ""));
            else if (numeric && double.TryParse(DisplayCell(Current, key).Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) rows.Add(new(row, 0, number, ""));
            else rows.Add(new(row, 1, 0, text));
        }
        rows.Sort((first, second) =>
        {
            int category = first.Category == 2 ? (second.Category == 2 ? 0 : 1) : second.Category == 2 ? -1 : first.Category.CompareTo(second.Category) * (direction == "ascending" ? 1 : -1);
            if (category != 0) return category;
            int comparison = first.Category switch { 0 => first.Number.CompareTo(second.Number), 1 => StringComparer.CurrentCultureIgnoreCase.Compare(first.Text, second.Text), _ => 0 };
            return comparison == 0 ? first.Row.CompareTo(second.Row) : comparison * (direction == "ascending" ? 1 : -1);
        });
        Dictionary<(int Row, int Col), string> inputs = Current.Cells.Where(pair => range.Contains(pair.Key.Row, pair.Key.Col) && pair.Key.Row >= firstRow).ToDictionary(pair => pair.Key, pair => pair.Value);
        Dictionary<(int Row, int Col), FontStyle> styles = Current.Fonts.Where(pair => range.Contains(pair.Key.Row, pair.Key.Col) && pair.Key.Row >= firstRow).ToDictionary(pair => pair.Key, pair => pair.Value);
        for (int destination = firstRow; destination <= table.Bottom; destination++)
        {
            int source = rows[destination - firstRow].Row;
            for (int col = table.Left; col <= table.Right; col++)
            {
                (int, int) key = (destination, col), original = (source, col);
                if (inputs.TryGetValue(original, out string? value)) Current.Cells[key] = value.StartsWith('=') ? ShiftFormula(value, destination - source, 0) : value;
                else Current.Cells.Remove(key);
                if (styles.TryGetValue(original, out FontStyle? style)) Current.Fonts[key] = style;
                else Current.Fonts.Remove(key);
            }
        }
    }
}
