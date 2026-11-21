using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void AutoFill(JsonElement args)
    {
        string target = args.GetProperty("target").GetString() ?? "";
        (int st, int sl, int sb, int sr) = ParseRange(args.GetProperty("source").GetString() ?? "");
        (int tt, int tl, int tb, int tr) = ParseRange(target);
        bool vertical = tl == sl && tr == sr && tt <= st && tb >= sb;
        bool horizontal = tt == st && tb == sb && tl <= sl && tr >= sr;
        if ((!vertical && !horizontal) || (long)(tb - tt + 1) * (tr - tl + 1) > 100_000) throw new InvalidOperationException("オートフィルの範囲が無効か、大きすぎます。");
        if (Current.Merges.Any(m => m.Intersects(new CellRange(tt, tl, tb, tr)))) throw new InvalidOperationException("結合セルを含む範囲のオートフィルには対応していません。");
        Dictionary<(int, int), string> inputs = new(Current.Cells);
        Dictionary<(int, int), FontStyle> fonts = new(Current.Fonts);
        for (int row = tt; row <= tb; row++) for (int col = tl; col <= tr; col++)
        {
            if (row >= st && row <= sb && col >= sl && col <= sr) continue;
            int sourceRow = st + Mod(row - st, sb - st + 1), sourceCol = sl + Mod(col - sl, sr - sl + 1);
            string value = inputs.GetValueOrDefault((sourceRow, sourceCol), "");
            int count = vertical ? sb - st + 1 : sr - sl + 1;
            if (count >= 2 && TrySeries(inputs, vertical, st, sl, sb, sr, row, col, out double number)) value = number.ToString("G15", CultureInfo.InvariantCulture);
            else if (value.StartsWith('=')) value = ShiftFormula(value, row - sourceRow, col - sourceCol);
            if (value.Length == 0) Current.Cells.Remove((row, col)); else Current.Cells[(row, col)] = value;
            FontStyle font = fonts.GetValueOrDefault((sourceRow, sourceCol), DefaultFont);
            if (font == DefaultFont) Current.Fonts.Remove((row, col)); else Current.Fonts[(row, col)] = font;
        }
        Current.Selection = target;
    }
    static int Mod(int n, int divisor) => (n % divisor + divisor) % divisor;
    static bool TrySeries(Dictionary<(int, int), string> cells, bool vertical, int top, int left, int bottom, int right, int row, int col, out double result)
    {
        int count = vertical ? bottom - top + 1 : right - left + 1;
        result = 0; double first = 0, step = 0;
        for (int i = 0; i < count; i++)
        {
            (int, int) key = vertical ? (top + i, col) : (row, left + i);
            if (!double.TryParse(cells.GetValueOrDefault(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)) return false;
            if (i == 0) first = n; else if (i == 1) step = n - first; else if (Math.Abs(n - (first + step * i)) > 1e-10 * Math.Max(1, Math.Abs(n))) return false;
        }
        result = first + step * (vertical ? row - top : col - left);
        return double.IsFinite(result);
    }
    static string ShiftFormula(string formula, int rowOffset, int colOffset)
    {
        // Quoted strings are not references; leave their contents untouched.
        string[] parts = Regex.Split(formula, "(\"(?:\"\"|[^\"])*\")");
        for (int i = 0; i < parts.Length; i += 2)
            parts[i] = Regex.Replace(parts[i], @"(?<![A-Za-z0-9_.!])(\$?)([A-Za-z]{1,3})(\$?)([1-9][0-9]*)(?![A-Za-z0-9_(])", match =>
            {
                try
                {
                    (int row, int col) = ParseCell(match.Value);
                    if (match.Groups[1].Value.Length == 0) col += colOffset;
                    if (match.Groups[3].Value.Length == 0) row += rowOffset;
                    if (row is < 0 or >= 1048576 || col is < 0 or >= 16384) return "#REF!";
                    string address = Address(row, col);
                    int split = address.TakeWhile(char.IsLetter).Count();
                    return match.Groups[1].Value + address[..split] + match.Groups[3].Value + address[split..];
                }
                catch (InvalidOperationException) { return match.Value; }
            });
        return string.Concat(parts);
    }
}
