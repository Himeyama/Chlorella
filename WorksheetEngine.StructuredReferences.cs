using System.Text.RegularExpressions;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed partial class FormulaParser
    {
        bool TryStructuredReference(out (int Top, int Left, int Bottom, int Right) range)
        {
            range = default;
            Match match = Regex.Match(text[position..], @"^(?<name>[\p{L}_\\][\p{L}\p{N}_.\\]*)?\[(?:\[#(?<item>This Row|Data)\],\[(?<nested>[^\]]+)\]|(?<simple>[^\[\]]+))\]", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            position += match.Length;
            string name = match.Groups["name"].Value;
            WorksheetTable? table = name.Length == 0
                ? sheet.Tables.FirstOrDefault(t => cell.Row >= t.Top && cell.Row <= t.Bottom && cell.Col >= t.Left && cell.Col <= t.Right)
                : sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (table is null || !table.Header) throw new FormulaException("#REF!");
            string columnName = match.Groups["nested"].Success ? match.Groups["nested"].Value : match.Groups["simple"].Value;
            bool thisRow = match.Groups["item"].Value.Equals("This Row", StringComparison.OrdinalIgnoreCase);
            if (columnName.StartsWith('@'))
            {
                thisRow = true;
                columnName = columnName[1..];
            }
            int column = -1;
            for (int c = table.Left; c <= table.Right; c++)
                if (string.Equals(sheet.Cells.GetValueOrDefault((table.Top, c), ""), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    column = c;
                    break;
                }
            if (column < 0) throw new FormulaException("#REF!");
            int top = table.Top + 1, bottom = table.Bottom - (table.Totals ? 1 : 0);
            if (thisRow)
            {
                if (cell.Row < top || cell.Row > bottom) throw new FormulaException("#VALUE!");
                top = bottom = cell.Row;
            }
            range = (top, column, bottom, column);
            return true;
        }
    }
}
