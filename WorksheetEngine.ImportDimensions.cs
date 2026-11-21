using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static void ImportDimensions(XDocument worksheet, Sheet sheet)
    {
        foreach (XElement column in worksheet.Root?.Element(MainNs + "cols")?.Elements(MainNs + "col") ?? [])
        {
            int min = (int?)column.Attribute("min") ?? 0, max = (int?)column.Attribute("max") ?? 0;
            if (min < 1 || max < min || max > 16384) throw new InvalidDataException("列の情報が無効です。");
            double? width = (double?)column.Attribute("width");
            if (width is null) continue;
            double pixels = width.Value * 7;
            if (!double.IsFinite(pixels) || pixels < 0 || pixels > 10000) throw new InvalidDataException("列幅が無効です。");
            if (pixels == 0) continue;
            for (int col = min - 1; col < max; col++) sheet.ColumnWidths[col] = pixels;
        }
        foreach (XElement row in worksheet.Root?.Element(MainNs + "sheetData")?.Elements(MainNs + "row") ?? [])
        {
            if (row.Attribute("ht") is null) continue;
            int index = (int?)row.Attribute("r") ?? 0;
            double height = ((double?)row.Attribute("ht") ?? 0) / 0.75;
            if (index < 1 || index > 1048576 || !double.IsFinite(height) || height < 0 ||
                height > 10000) throw new InvalidDataException("行の高さが無効です。");
            if (height > 0) sheet.RowHeights[index - 1] = height;
        }
    }
}
