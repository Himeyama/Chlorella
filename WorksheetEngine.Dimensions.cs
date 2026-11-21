using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void SetDimensions(string command, JsonElement args)
    {
        bool row = command is "home.rowHeight" or "home.autofitRowHeight";
        bool automatic = command is "home.autofitRowHeight" or "home.autofitColumnWidth";
        (int top, int left, int bottom, int right) = ParseRange(args.GetProperty(row ? "rows" : "cols").GetString() ?? "");
        int first = row ? top : left, last = row ? bottom : right;
        if (last - first > 10000) throw new InvalidOperationException("一度に変更できる行または列が多すぎます。");
        double size = automatic ? 0 : args.GetProperty(row ? "height" : "width").GetDouble();
        if (!automatic && (!double.IsFinite(size) || size <= 0 || size > 10000)) throw new InvalidOperationException("サイズは 0 より大きく 10000 以下にしてください。");
        Dictionary<int, double> dimensions = row ? Current.RowHeights : Current.ColumnWidths;
        for (int index = first; index <= last; index++)
        {
            if (!automatic) { dimensions[index] = size; continue; }
            if (row)
            {
                double height = Current.Fonts.Where(p => p.Key.Row == index)
                    .Select(p => EstimateRowHeight(Current, p.Key, p.Value)).DefaultIfEmpty(24).Max();
                dimensions[index] = Math.Clamp(height, 24, 10000);
            }
            else
            {
                double width = Current.Cells.Where(p => p.Key.Col == index).Select(p =>
                {
                    FontStyle font = Current.Fonts.GetValueOrDefault(p.Key, DefaultFont);
                    string text = FormatCell(Current, p.Key, font).Text;
                    int length = text.Split('\n').Max(line => line.Sum(ch => ch > 255 ? 2 : 1));
                    return length * font.Size * 0.6 + 16;
                }).DefaultIfEmpty(100).Max();
                dimensions[index] = Math.Clamp(width, 20, 10000);
            }
        }
    }
}
