using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void InsertCells(string command, JsonElement args)
    {
        bool rows = command == "home.insertRows";
        (int top, int left, int bottom, int right) = ParseRange(args.GetProperty(rows ? "rows" : "cols").GetString() ?? "");
        int start = rows ? top : left, count = rows ? bottom - top + 1 : right - left + 1;
        int limit = rows ? 1048576 : 16384;
        if (Current.Cells.Keys.Union(Current.Fonts.Keys).Any(key => (rows ? key.Row : key.Col) >= limit - count) ||
            Current.Merges.Any(merge => (rows ? merge.Bottom : merge.Right) >= limit - count) ||
            Current.Tables.Any(table => (rows ? table.Bottom : table.Right) >= limit - count) ||
            Current.Objects.Any(item => (rows ? item.Row : item.Col) >= limit - count) ||
            (rows ? Current.RowHeights : Current.ColumnWidths).Keys.Any(index => index >= limit - count))
            throw new InvalidOperationException("挿入するとシートの末尾にあるデータが失われます。");

        (int Row, int Col) Move((int Row, int Col) cell) => rows
            ? (cell.Row >= start ? cell.Row + count : cell.Row, cell.Col)
            : (cell.Row, cell.Col >= start ? cell.Col + count : cell.Col);
        Dictionary<(int Row, int Col), string> cells = new();
        foreach (KeyValuePair<(int Row, int Col), string> cell in Current.Cells)
            cells.Add(Move(cell.Key), cell.Value.StartsWith('=') ? AdjustInsertedReferences(cell.Value, rows, start, count) : cell.Value);
        Current.Cells = cells;
        Current.Fonts = Current.Fonts.ToDictionary(pair => Move(pair.Key), pair => pair.Value);
        Current.Merges = Current.Merges.Select(merge => rows
            ? new CellRange(merge.Top >= start ? merge.Top + count : merge.Top, merge.Left, merge.Bottom >= start ? merge.Bottom + count : merge.Bottom, merge.Right)
            : new CellRange(merge.Top, merge.Left >= start ? merge.Left + count : merge.Left, merge.Bottom, merge.Right >= start ? merge.Right + count : merge.Right)).ToList();
        Current.Tables = Current.Tables.Select(table => rows
            ? table with { Top = table.Top >= start ? table.Top + count : table.Top, Bottom = table.Bottom >= start ? table.Bottom + count : table.Bottom }
            : table with { Left = table.Left >= start ? table.Left + count : table.Left, Right = table.Right >= start ? table.Right + count : table.Right, Filters = table.Filters?.Select(filter => filter with { Column = filter.Column >= start ? filter.Column + count : filter.Column }).ToArray() }).ToList();
        Current.Objects = Current.Objects.Select(item => rows
            ? item with { Row = item.Row >= start ? item.Row + count : item.Row }
            : item with { Col = item.Col >= start ? item.Col + count : item.Col }).ToList();
        Dictionary<int, double> dimensions = rows ? Current.RowHeights : Current.ColumnWidths;
        Dictionary<int, double> moved = dimensions.ToDictionary(pair => pair.Key >= start ? pair.Key + count : pair.Key, pair => pair.Value);
        if (rows) Current.RowHeights = moved; else Current.ColumnWidths = moved;
        (int activeRow, int activeColumn) = Move(ParseCell(Current.Active));
        activeRow = Math.Min(activeRow, 1048575); activeColumn = Math.Min(activeColumn, 16383);
        Current.Active = Address(activeRow, activeColumn);
        (int selectionTop, int selectionLeft, int selectionBottom, int selectionRight) = ParseRange(Current.Selection);
        (selectionTop, selectionLeft) = Move((selectionTop, selectionLeft));
        (selectionBottom, selectionRight) = Move((selectionBottom, selectionRight));
        selectionTop = Math.Min(selectionTop, 1048575); selectionBottom = Math.Min(selectionBottom, 1048575);
        selectionLeft = Math.Min(selectionLeft, 16383); selectionRight = Math.Min(selectionRight, 16383);
        Current.Selection = $"{Address(selectionTop, selectionLeft)}:{Address(selectionBottom, selectionRight)}";
    }

    static string AdjustInsertedReferences(string formula, bool rows, int start, int count)
    {
        string[] parts = Regex.Split(formula, "(\"(?:\"\"|[^\"])*\")");
        for (int i = 0; i < parts.Length; i += 2)
            parts[i] = Regex.Replace(parts[i], @"(?<![A-Za-z0-9_.!])(\$?)([A-Za-z]{1,3})(\$?)([1-9][0-9]*)(?![A-Za-z0-9_(])", match =>
            {
                try
                {
                    (int row, int col) = ParseCell(match.Value);
                    if (rows && row >= start) row += count;
                    if (!rows && col >= start) col += count;
                    if (row >= 1048576 || col >= 16384) return "#REF!";
                    string address = Address(row, col);
                    int split = address.TakeWhile(char.IsLetter).Count();
                    return match.Groups[1].Value + address[..split] + match.Groups[3].Value + address[split..];
                }
                catch (InvalidOperationException) { return match.Value; }
            });
        return string.Concat(parts);
    }
}
