using System.Globalization;
using System.Text;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    const double PdfScale = 0.75, PdfPageWidth = 523, PdfPageHeight = 760;
    sealed record PdfSegment(double Start, double End, int First, int Last);

    void ExportPdf(string path)
    {
        int maxRow = Current.Cells.Keys.Select(c => c.Row).Concat(Current.Fonts.Keys.Select(c =>
            c.Row)).Concat(Current.Merges.Select(m => m.Bottom)).Concat(Current.Tables.Select(t => t.Bottom)).DefaultIfEmpty(0).Max();
        int maxCol = Current.Cells.Keys.Select(c => c.Col).Concat(Current.Fonts.Keys.Select(c =>
            c.Col)).Concat(Current.Merges.Select(m => m.Right)).Concat(Current.Tables.Select(t => t.Right)).DefaultIfEmpty(0).Max();
        maxRow = Math.Max(maxRow, Current.Objects.Select(item => item.Row).DefaultIfEmpty(0).Max());
        maxCol = Math.Max(maxCol, Current.Objects.Select(item => item.Col).DefaultIfEmpty(0).Max());
        if ((long)(maxRow + 1) * (maxCol + 1) > 1_000_000) throw new InvalidOperationException("PDF の出力範囲が大きすぎます。");
        Dictionary<int, double> automaticHeights = Current.Fonts.GroupBy(p => p.Key.Row).ToDictionary(group => group.Key,
            group => Math.Max(24, group.Max(p => EstimateRowHeight(Current, p.Key, p.Value))));
        HashSet<int> hiddenRows = FilteredRows();
        double RowHeight(int row) => hiddenRows.Contains(row) ? 0 : Math.Max(18, Current.RowHeights.GetValueOrDefault(row,
            automaticHeights.GetValueOrDefault(row, 24)));
        double ColumnWidth(int col) => Math.Max(20, Current.ColumnWidths.GetValueOrDefault(col, 100));
        List<double> rowEdges = [0], colEdges = [0];
        for (int row = 0; row <= maxRow; row++) rowEdges.Add(rowEdges[^1] + RowHeight(row));
        for (int col = 0; col <= maxCol; col++) colEdges.Add(colEdges[^1] + ColumnWidth(col));
        foreach (DrawingObject drawing in Current.Objects)
        {
            double right = colEdges[drawing.Col] + drawing.Dx + drawing.Width;
            double bottom = rowEdges[drawing.Row] + drawing.Dy + drawing.Height;
            while (colEdges[^1] < right && colEdges.Count <= 16384) colEdges.Add(colEdges[^1] + ColumnWidth(colEdges.Count - 1));
            while (rowEdges[^1] < bottom && rowEdges.Count <= 1048576) rowEdges.Add(rowEdges[^1] + RowHeight(rowEdges.Count - 1));
        }
        if ((long)(rowEdges.Count - 1) * (colEdges.Count - 1) > 1_000_000) throw new InvalidOperationException("PDF の出力範囲が大きすぎます。");
        List<PdfSegment> rowPages = PaginatePdfAxis(rowEdges, PdfPageHeight / PdfScale);
        List<PdfSegment> colPages = PaginatePdfAxis(colEdges, PdfPageWidth / PdfScale);
        if ((long)rowPages.Count * colPages.Count > 10000) throw new InvalidOperationException("PDF のページ数が多すぎます。");
        Dictionary<uint, PdfImage> images = Current.Objects.Where(item => item.Kind == "image").ToDictionary(item => item.Id,
            DecodePdfImage);
        List<string> pages = [];
        foreach (PdfSegment rowPage in rowPages)
            foreach (PdfSegment colPage in colPages)
            {
                StringBuilder content = new();
                double pageWidth = (colPage.End - colPage.Start) * PdfScale, pageHeight = (rowPage.End - rowPage.Start) * PdfScale;
                content.AppendFormat(CultureInfo.InvariantCulture, "q 36 {0:F3} {1:F3} {2:F3} re W n\n", 806 - pageHeight,
                    pageWidth, pageHeight);
                for (int row = rowPage.First; row <= rowPage.Last; row++)
                    for (int col = colPage.First; col <= colPage.Last; col++)
                    {
                        if (hiddenRows.Contains(row)) continue;
                        CellRange? merge = Current.Merges.FirstOrDefault(m => m.Contains(row, col));
                        int top = merge?.Top ?? row, left = merge?.Left ?? col, bottom = merge?.Bottom ?? row, right = merge?.Right ?? col;
                        if (row != Math.Max(rowPage.First, top) || col != Math.Max(colPage.First, left)) continue;
                        double x = 36 + (colEdges[left] - colPage.Start) * PdfScale, y = 806 - (rowEdges[top] - rowPage.Start) * PdfScale;
                        double width = (colEdges[right + 1] - colEdges[left]) * PdfScale,
                            height = (rowEdges[bottom + 1] - rowEdges[top]) * PdfScale;
                        FontStyle style = Current.Fonts.GetValueOrDefault((top, left), DefaultFont);
                        uint? fill = style.Fill;
                        if (fill is null && Current.Tables.Any(t => t.Header && top == t.Top && left >= t.Left &&
                            left <= t.Right)) fill = 0xd9e5f5;
                        if (fill is uint background)
                        {
                            SetColor(content, background, false);
                            content.AppendFormat(CultureInfo.InvariantCulture, "{0} {1} {2} {3} re f\n", x, y - height, width, height);
                        }
                        content.AppendFormat(CultureInfo.InvariantCulture, "0.75 G 0.4 w {0} {1} {2} {3} re S\n", x,
                            y - height, width, height);
                        if (row != top || col != left || rowEdges[top] < rowPage.Start || colEdges[left] < colPage.Start) continue;
                        (string value, bool numeric) = FormatCell(Current, (top, left), style);
                        if (value.Length > 0) PdfCellText(content, value, x, y, width, height, style, numeric);
                    }
                foreach (DrawingObject item in Current.Objects)
                {
                    if (hiddenRows.Contains(item.Row)) continue;
                    double x = 36 + (colEdges[item.Col] + item.Dx - colPage.Start) * PdfScale;
                    double y = 806 - (rowEdges[item.Row] + item.Dy - rowPage.Start) * PdfScale;
                    double width = item.Width * PdfScale, height = item.Height * PdfScale;
                    if (x + width <= 36 || x >= 36 + pageWidth || y <= 806 - pageHeight || y - height >= 806) continue;
                    if (item.Kind == "image") content.AppendFormat(CultureInfo.InvariantCulture,
                        "q {0:F3} 0 0 {1:F3} {2:F3} {3:F3} cm /Im{4} Do Q\n", width, height, x, y - height, item.Id);
                    else PdfShape(content, item, x, y, width, height);
                }
                content.Append("Q\n");
                pages.Add(content.ToString());
            }
        WritePdf(path, pages, images);
    }

    static List<PdfSegment> PaginatePdfAxis(List<double> edges, double available)
    {
        List<PdfSegment> pages = [];
        double start = 0;
        while (start < edges[^1])
        {
            double limit = Math.Min(edges[^1], start + available);
            int upper = edges.BinarySearch(limit);
            if (upper < 0) upper = ~upper - 1;
            double end = edges[upper] > start ? edges[upper] : limit;
            int first = edges.BinarySearch(start);
            first = first >= 0 ? first : ~first - 1;
            int last = edges.BinarySearch(end);
            last = last >= 0 ? last - 1 : ~last - 1;
            pages.Add(new(start, end, Math.Clamp(first, 0, edges.Count - 2), Math.Clamp(last, 0, edges.Count - 2)));
            if (pages.Count > 10000) throw new InvalidOperationException("PDF のページ数が多すぎます。");
            start = end;
        }
        return pages;
    }

    static void SetColor(StringBuilder content, uint color,
        bool stroke) => content.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} {3}\n",
            ((color >> 16) & 255) / 255d, ((color >> 8) & 255) / 255d, (color & 255) / 255d, stroke ? "RG" : "rg");
    static void PdfText(StringBuilder content, string value, double x, double y, double width, double height, uint color, double size)
    {
        content.AppendFormat(CultureInfo.InvariantCulture, "q {0:F3} {1:F3} {2:F3} {3:F3} re W n\n", x + 2, y - height + 1,
            Math.Max(0, width - 4), Math.Max(0, height - 2));
        SetColor(content, color, false);
        PdfDrawText(content, value, x + 3, y - Math.Min(height - 2, size + 3), size, false, false);
        content.Append("Q\n");
    }

    static void WritePdf(string path, List<string> pages, Dictionary<uint, PdfImage> images)
    {
        List<string> objects = ["<< /Type /Catalog /Pages 2 0 R >>", "",
            "<< /Type /Font /Subtype /Type0 /BaseFont /HeiseiKakuGo-W5 /Encoding /UniJIS-UCS2-H /DescendantFonts [4 0 R] >>",
                "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /HeiseiKakuGo-W5" +
                    " /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 5 >> >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"];
        Dictionary<uint, int> imageIds = [];
        foreach (KeyValuePair<uint, PdfImage> pair in images)
        {
            PdfImage image = pair.Value;
            int maskId = 0;
            if (image.Alpha is not null)
            {
                maskId = objects.Count + 1;
                objects.Add(PdfImageObject(image.Width, image.Height, "DeviceGray", image.Alpha, false, 0));
            }
            imageIds.Add(pair.Key, objects.Count + 1);
            objects.Add(PdfImageObject(image.Width, image.Height, image.ColorSpace, image.Data, image.Jpeg, maskId));
        }
        string xobjects = string.Join(' ', imageIds.Select(pair => $"/Im{pair.Key} {pair.Value} 0 R"));
        List<int> pageIds = [];
        foreach (string page in pages)
        {
            int pageId = objects.Count + 1, contentId = pageId + 1;
            pageIds.Add(pageId);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842]" +
                $" /Resources << /Font << /F1 3 0 R /F2 5 0 R >> /XObject << {xobjects} >> >> /Contents {contentId} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(page)} >>\nstream\n{page}endstream");
        }
        objects[1] = $"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] >>";
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        void Write(string value)
        {
            byte[] data = Encoding.ASCII.GetBytes(value);
            stream.Write(data);
        }
        Write("%PDF-1.4\n%Chlorella\n");
        List<long> offsets = [0];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = stream.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
    static string PdfImageObject(int width, int height, string colorSpace, byte[] data, bool jpeg, int maskId)
    {
        string encoded = Convert.ToHexString(data) + ">";
        string mask = maskId == 0 ? "" : $" /SMask {maskId} 0 R";
        return $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /{colorSpace}" +
            $" /BitsPerComponent 8 /Filter [/ASCIIHexDecode /{(jpeg ? "DCTDecode" : "FlateDecode")}]" +
            $" /Length {encoded.Length}{mask} >>\nstream\n{encoded}\nendstream";
    }
}
