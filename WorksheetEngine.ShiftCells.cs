using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void ShiftCells(JsonElement args)
    {
        (int top, int left, int bottom, int right) = ParseRange(args.GetProperty("range").GetString() ?? "A1");
        bool insert = args.GetProperty("insert").GetBoolean();
        bool vertical = args.GetProperty("vertical").GetBoolean();
        int start = vertical ? top : left;
        int end = vertical ? bottom : right;
        int count = end - start + 1;
        int limit = vertical ? 1048576 : 16384;
        CellRange affected = vertical ? new(top, left, limit - 1, right) : new(top, left, bottom, limit - 1);
        if (Current.Tables.Any(t =>
            new CellRange(t.Top, t.Left, t.Bottom, t.Right).Intersects(affected)) ||
            Current.Objects.Any(o => affected.Contains(o.Row, o.Col)))
            throw new InvalidOperationException("テーブル、図形または画像を含む範囲はシフトできません。");
        if (Current.Cells.Values.Any(value => value.StartsWith('=')))
            throw new InvalidOperationException("数式を含むシートではセルをシフトできません。");
        if (insert && Current.Cells.Keys.Union(Current.Fonts.Keys).Any(key => affected.Contains(key.Row, key.Col) &&
            (vertical ? key.Row : key.Col) >= limit - count))
            throw new InvalidOperationException("挿入するとシートの末尾にあるデータが失われます。");

        (int Row, int Col)? Map((int Row, int Col) cell)
        {
            if (!affected.Contains(cell.Row, cell.Col)) return cell;
            int index = vertical ? cell.Row : cell.Col;
            if (!insert && index <= end) return null;
            int next = index + (insert ? count : -count);
            return vertical ? (next, cell.Col) : (cell.Row, next);
        }
        Dictionary<(int Row, int Col), T> Shift<T>(Dictionary<(int Row, int Col), T> source)
        {
            Dictionary<(int Row, int Col), T> result = new();
            foreach (KeyValuePair<(int Row, int Col), T> pair in source)
                if (Map(pair.Key) is { } destination) result.Add(destination, pair.Value);
            return result;
        }
        List<CellRange> shiftedMerges = [];
        foreach (CellRange merge in Current.Merges)
        {
            if (!merge.Intersects(affected))
            {
                shiftedMerges.Add(merge);
                continue;
            }
            bool acrossContained = vertical ? merge.Left >= left && merge.Right <= right
                : merge.Top >= top && merge.Bottom <= bottom;
            int mergeStart = vertical ? merge.Top : merge.Left;
            int mergeEnd = vertical ? merge.Bottom : merge.Right;
            if (!acrossContained || (insert && mergeStart < start) ||
                (!insert && mergeStart <= end && (mergeStart < start || mergeEnd > end)))
                throw new InvalidOperationException("結合セルを分割する範囲はシフトできません。");
            if (!insert && mergeStart <= end) continue;
            if (insert && mergeEnd >= limit - count)
                throw new InvalidOperationException("挿入するとシートの末尾にある結合セルが失われます。");
            int offset = insert ? count : -count;
            shiftedMerges.Add(vertical
                ? new CellRange(merge.Top + offset, merge.Left, merge.Bottom + offset, merge.Right)
                : new CellRange(merge.Top, merge.Left + offset, merge.Bottom, merge.Right + offset));
        }
        Current.Merges = shiftedMerges;
        Current.Cells = Shift(Current.Cells);
        Current.Fonts = Shift(Current.Fonts);
        Current.Active = Address(top, left);
        Current.Selection = $"{Address(top, left)}:{Address(bottom, right)}";
    }
}
