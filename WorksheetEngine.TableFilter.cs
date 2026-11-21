using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    readonly Dictionary<Sheet, HashSet<int>> filteredRowCache = new();

    void EnsureVisibleRows(string range)
    {
        (int top, _, int bottom, _) = ParseRange(range);
        if (FilteredRows().Any(row => row >= top && row <= bottom)) throw new InvalidOperationException("選択範囲にフィルターで非表示の行があります。フィルターを解除してから操作してください。");
    }

    void EnsureActiveRowVisible()
    {
        (int row, int col) = ParseCell(Current.Active);
        HashSet<int> hidden = FilteredRows();
        if (!hidden.Contains(row)) return;
        int visible = row;
        while (visible < 1048575 && hidden.Contains(visible)) visible++;
        if (hidden.Contains(visible)) { visible = row; while (visible > 0 && hidden.Contains(visible)) visible--; }
        Current.Active = Address(visible, col);
        Current.Selection = Current.Active;
    }

    HashSet<int> FilteredRows() => FilteredRows(Current);

    HashSet<int> FilteredRows(Sheet sheet)
    {
        if (filteredRowCache.TryGetValue(sheet, out HashSet<int>? cached)) return cached;
        HashSet<int> hidden = [];
        foreach (WorksheetTable table in sheet.Tables)
        {
            TableFilter[] filters = table.Filters ?? [];
            if (filters.Length == 0) continue;
            Dictionary<int, HashSet<string>> accepted = filters.ToDictionary(filter => filter.Column, filter => new HashSet<string>(filter.Values, StringComparer.OrdinalIgnoreCase));
            for (int row = table.Top + (table.Header ? 1 : 0); row <= table.Bottom; row++)
                if (accepted.Any(filter => !filter.Value.Contains(DisplayCell(sheet, (row, filter.Key)).Text))) hidden.Add(row);
        }
        filteredRowCache[sheet] = hidden;
        return hidden;
    }

    WorksheetTable TableAt(string address, out int column)
    {
        (int row, int col) = ParseCell(address); column = col;
        WorksheetTable table = Current.Tables.FirstOrDefault(item => row >= item.Top && row <= item.Bottom && col >= item.Left && col <= item.Right) ?? throw new InvalidOperationException("テーブルが見つかりません。");
        if (!table.Header) throw new InvalidOperationException("見出しのないテーブルにはフィルターを設定できません。");
        return table;
    }

    object FilterChoices(JsonElement args)
    {
        WorksheetTable table = TableAt(args.GetProperty("cell").GetString() ?? "", out int col);
        TableFilter? active = (table.Filters ?? []).FirstOrDefault(filter => filter.Column == col);
        HashSet<string>? selected = active is null ? null : new(active.Values, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (string Label, int Count)> choices = new(StringComparer.OrdinalIgnoreCase);
        for (int row = table.Top + 1; row <= table.Bottom; row++)
        {
            (int, int) key = (row, col);
            string value = DisplayCell(Current, key).Text;
            string label = FormatCell(Current, key, Current.Fonts.GetValueOrDefault(key, DefaultFont)).Text;
            if (choices.TryGetValue(value, out (string Label, int Count) item)) choices[value] = (item.Label, item.Count + 1);
            else choices.Add(value, (label, 1));
        }
        return new { active = active is not null, startRow = table.Top + 1, endRow = table.Bottom + 1, values = choices.OrderBy(pair => pair.Value.Label, StringComparer.CurrentCultureIgnoreCase).Select(pair => new { value = pair.Key, label = pair.Value.Label, count = pair.Value.Count, selected = selected is null || selected.Contains(pair.Key) }).ToArray() };
    }

    void ApplyTableFilter(JsonElement args)
    {
        WorksheetTable table = TableAt(args.GetProperty("cell").GetString() ?? "", out int col);
        List<TableFilter> filters = (table.Filters ?? []).Where(filter => filter.Column != col).ToList();
        JsonElement values = args.GetProperty("values");
        if (values.ValueKind != JsonValueKind.Null)
        {
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 100000) throw new InvalidOperationException("フィルターの値が無効です。");
            string[] accepted = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : throw new InvalidOperationException("フィルターの値が無効です。")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            filters.Add(new(col, accepted));
        }
        Current.Tables[Current.Tables.IndexOf(table)] = table with { Filters = [.. filters] };
        filteredRowCache.Clear(); drawingHeightCache.Clear();
        (int activeRow, _) = ParseCell(Current.Active);
        if (FilteredRows().Contains(activeRow))
        {
            HashSet<int> hidden = FilteredRows();
            int visibleRow = table.Top;
            while (visibleRow < 1048575 && hidden.Contains(visibleRow)) visibleRow++;
            while (visibleRow > 0 && hidden.Contains(visibleRow)) visibleRow--;
            Current.Active = Address(visibleRow, col);
            Current.Selection = Current.Active;
        }
    }

    object NextVisibleRow(JsonElement args)
    {
        int row = Math.Clamp(args.GetProperty("row").GetInt32() - 1, 0, 1048575);
        int direction = args.GetProperty("direction").GetInt32();
        int count = args.GetProperty("count").GetInt32();
        if (direction is not (-1 or 1) || count is < 1 or > 1000) throw new InvalidOperationException("スクロール位置が無効です。");
        HashSet<int> hidden = FilteredRows();
        int current = row;
        for (int step = 0; step < count; step++)
        {
            int candidate = current;
            do { candidate += direction; } while (candidate is >= 0 and < 1048576 && hidden.Contains(candidate));
            if (candidate is < 0 or >= 1048576) break;
            current = candidate;
        }
        return new { row = current + 1 };
    }

    object ResolveVisibleRow(JsonElement args)
    {
        int row = Math.Clamp(args.GetProperty("row").GetInt32() - 1, 0, 1048575), original = row;
        HashSet<int> hidden = FilteredRows();
        while (row < 1048575 && hidden.Contains(row)) row++;
        if (hidden.Contains(row)) { row = original; while (row > 0 && hidden.Contains(row)) row--; }
        return new { row = row + 1 };
    }
}
