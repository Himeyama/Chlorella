using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void MoveRange(JsonElement args)
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        int dr = args.GetProperty("rowOffset").GetInt32();
        int dc = args.GetProperty("columnOffset").GetInt32();
        bool swap = args.GetProperty("swap").GetBoolean();
        if (swap && args.TryGetProperty("insertIndex", out JsonElement insertion))
        {
            InsertMovedRange(args.GetProperty("vertical").GetBoolean(), insertion.GetInt32());
            return;
        }
        if (dr == 0 && dc == 0) return;
        if (top + dr < 0 || bottom + dr >= 1048576 || left + dc < 0 || right + dc >= 16384)
            throw new InvalidOperationException("移動先がシートの範囲外です。");
        CellRange source = new(top, left, bottom, right);
        CellRange target = new(top + dr, left + dc, bottom + dr, right + dc);
        if (swap && source.Intersects(target)) throw new InvalidOperationException("入れ替える範囲が重なっています。");
        EnsureVisibleRows(Current.Selection);
        EnsureVisibleRows($"{Address(target.Top, target.Left)}:{Address(target.Bottom, target.Right)}");
        if (Current.Merges.Any(m => (m.Intersects(source) && !ContainsRange(source, m)) ||
            (m.Intersects(target) && !ContainsRange(source, m))) ||
            Current.Tables.Any(t => new CellRange(t.Top, t.Left, t.Bottom,
                t.Right).Intersects(source) || new CellRange(t.Top, t.Left, t.Bottom, t.Right).Intersects(target)))
            throw new InvalidOperationException("結合セルまたはテーブルを含む範囲は移動できません。");
        if (Current.Cells.Values.Any(value => value.StartsWith('=')))
            throw new InvalidOperationException("数式を含むシートでは範囲を移動できません。");
        bool rows = left == 0 && right == 16383;
        bool columns = top == 0 && bottom == 1048575;
        if (Current.Objects.Any(item => source.Contains(item.Row, item.Col) || target.Contains(item.Row, item.Col)))
            throw new InvalidOperationException("図形や画像を含む範囲は移動できません。");
        Current.Merges = Current.Merges.Select(m => ContainsRange(source, m)
            ? new CellRange(m.Top + dr, m.Left + dc, m.Bottom + dr, m.Right + dc) : m).ToList();
        MoveDictionary(Current.Cells, source, target, dr, dc, swap);
        MoveDictionary(Current.Fonts, source, target, dr, dc, swap);
        if (rows) MoveDimensions(Current.RowHeights, top, bottom, dr, swap);
        if (columns) MoveDimensions(Current.ColumnWidths, left, right, dc, swap);
        Current.Selection = $"{Address(target.Top, target.Left)}:{Address(target.Bottom, target.Right)}";
        (int activeRow, int activeCol) = ParseCell(Current.Active);
        Current.Active = Address(activeRow + dr, activeCol + dc);
    }

    void InsertMovedRange(bool vertical, int insertion)
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        int start = vertical ? top : left, end = vertical ? bottom : right;
        int limit = vertical ? 1048576 : 16384;
        if (insertion < 0 || insertion > limit) throw new InvalidOperationException("移動先がシートの範囲外です。");
        if (insertion >= start && insertion <= end + 1) return;
        int count = end - start + 1;
        int destination = insertion > end ? insertion - count : insertion;
        int offset = destination - start;
        CellRange affected = vertical
            ? new(Math.Min(start, destination), left, Math.Max(end, destination + count - 1), right)
            : new(top, Math.Min(start, destination), bottom, Math.Max(end, destination + count - 1));
        EnsureVisibleRows($"{Address(affected.Top, affected.Left)}:{Address(affected.Bottom, affected.Right)}");
        if (Current.Merges.Any(m => m.Intersects(affected) && !ContainsRange(affected, m)) || Current.Tables.Any(t => new CellRange(t.Top, t.Left, t.Bottom,
            t.Right).Intersects(affected)))
            throw new InvalidOperationException("結合セルまたはテーブルを含む範囲は移動できません。");
        if (Current.Cells.Values.Any(value => value.StartsWith('='))) throw new InvalidOperationException("数式を含むシートでは範囲を移動できません。");
        if (Current.Objects.Any(item => affected.Contains(item.Row, item.Col))) throw new InvalidOperationException("図形や画像を含む範囲は移動できません。");

        int Map(int index) => index >= start && index <= end ? index + offset
            : offset > 0 && index > end && index < insertion ? index - count
            : offset < 0 && index >= insertion && index < start ? index + count : index;
        (int Row, int Col) MapCell((int Row, int Col) cell) => !affected.Contains(cell.Row, cell.Col) ? cell
            : vertical ? (Map(cell.Row), cell.Col) : (cell.Row, Map(cell.Col));
        List<CellRange> mappedMerges = [];
        foreach (CellRange merge in Current.Merges)
        {
            if (!merge.Intersects(affected)) { mappedMerges.Add(merge); continue; }
            (int mt, int ml) = MapCell((merge.Top, merge.Left));
            (int mb, int mr) = MapCell((merge.Bottom, merge.Right));
            if (mb - mt != merge.Bottom - merge.Top || mr - ml != merge.Right - merge.Left)
                throw new InvalidOperationException("結合セルを分割する位置には移動できません。");
            mappedMerges.Add(new CellRange(mt, ml, mb, mr));
        }
        Current.Merges = mappedMerges;
        Current.Cells = Current.Cells.ToDictionary(pair => MapCell(pair.Key), pair => pair.Value);
        Current.Fonts = Current.Fonts.ToDictionary(pair => MapCell(pair.Key), pair => pair.Value);
        if (vertical && left == 0 &&
            right == 16383) Current.RowHeights = Current.RowHeights.ToDictionary(pair => Map(pair.Key), pair => pair.Value);
        if (!vertical && top == 0 &&
            bottom == 1048575) Current.ColumnWidths = Current.ColumnWidths.ToDictionary(pair => Map(pair.Key), pair => pair.Value);
        int dr = vertical ? offset : 0, dc = vertical ? 0 : offset;
        Current.Selection = $"{Address(top + dr, left + dc)}:{Address(bottom + dr, right + dc)}";
        (int activeRow, int activeCol) = ParseCell(Current.Active);
        Current.Active = Address(activeRow + dr, activeCol + dc);
    }

    static bool ContainsRange(CellRange outer, CellRange inner) =>
        outer.Contains(inner.Top, inner.Left) && outer.Contains(inner.Bottom, inner.Right);

    static void MoveDictionary<T>(Dictionary<(int Row, int Col), T> items, CellRange source, CellRange target, int dr, int dc, bool swap)
    {
        KeyValuePair<(int Row, int Col), T>[] original = items.Where(p => source.Contains(p.Key.Row, p.Key.Col)).ToArray();
        KeyValuePair<(int Row, int Col), T>[] replaced = swap ? items.Where(p => target.Contains(p.Key.Row, p.Key.Col)).ToArray() : [];
        foreach ((int row, int col) in items.Keys.Where(p => source.Contains(p.Row, p.Col) || target.Contains(p.Row,
            p.Col)).ToArray()) items.Remove((row, col));
        foreach (KeyValuePair<(int Row, int Col), T> item in original) items[(item.Key.Row + dr, item.Key.Col + dc)] = item.Value;
        foreach (KeyValuePair<(int Row, int Col), T> item in replaced) items[(item.Key.Row - dr, item.Key.Col - dc)] = item.Value;
    }

    static void MoveDimensions(Dictionary<int, double> items, int start, int end, int offset, bool swap)
    {
        KeyValuePair<int, double>[] original = items.Where(p => p.Key >= start && p.Key <= end).ToArray();
        KeyValuePair<int, double>[] replaced = swap ? items.Where(p => p.Key >= start + offset && p.Key <= end + offset).ToArray() : [];
        foreach (int key in items.Keys.Where(k => (k >= start && k <= end) || (k >= start + offset &&
            k <= end + offset)).ToArray()) items.Remove(key);
        foreach (KeyValuePair<int, double> item in original) items[item.Key + offset] = item.Value;
        foreach (KeyValuePair<int, double> item in replaced) items[item.Key - offset] = item.Value;
    }
}
