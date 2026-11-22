using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static (int Imported, int Skipped) ImportDrawingParts(ZipArchive zip, string worksheetPart, XDocument worksheet, Sheet sheet)
    {
        XElement? drawingReference = worksheet.Root?.Element(MainNs + "drawing");
        if (drawingReference is null) return (0, 0);
        string id = (string?)drawingReference.Attribute(RelNs + "id") ?? "";
        Dictionary<string, PackageRelationship> sheetRelations = ReadPartRelationships(zip, worksheetPart);
        if (!sheetRelations.TryGetValue(id,
            out PackageRelationship? relation) ||
                relation.Type != RelNs.NamespaceName + "/drawing") throw new InvalidDataException("図形の参照が無効です。");
        XElement drawing = ReadXml(zip, relation.Target).Root ?? throw new InvalidDataException("図形のデータが無効です。");
        if (drawing.Name != SheetDrawingNs + "wsDr") throw new InvalidDataException("図形のデータが無効です。");
        Dictionary<string, PackageRelationship> drawingRelations = ReadPartRelationships(zip, relation.Target);
        Dictionary<int, double> widths = sheet.ColumnWidths.ToDictionary(pair => pair.Key, pair => Math.Max(20, pair.Value));
        Dictionary<int, double> heights = sheet.RowHeights.ToDictionary(pair => pair.Key, pair => Math.Max(18, pair.Value));
        AxisOffsets columns = new(widths, 100), rows = new(heights, 24);
        int imported = 0, skipped = 0;
        foreach (XElement anchor in drawing.Elements())
        {
            if (imported + skipped >= 10000) throw new InvalidDataException("図形や画像が多すぎます。");
            if (!TryImportAnchor(anchor, columns, rows, out int row, out int col, out double dx, out double dy,
                out double width, out double height))
            {
                skipped++;
                continue;
            }
            XElement? shape = anchor.Element(SheetDrawingNs + "sp"), picture = anchor.Element(SheetDrawingNs + "pic");
            XElement? transform = (shape ?? picture)?.Element(SheetDrawingNs + "spPr")?.Element(DrawingNs + "xfrm");
            bool isLine = (string?)shape?.Element(SheetDrawingNs + "spPr")?.Element(DrawingNs + "prstGeom")?
                .Attribute("prst") == "line";
            bool flipH = (string?)transform?.Attribute("flipH") is "1" or "true";
            bool flipV = (string?)transform?.Attribute("flipV") is "1" or "true";
            if (!isLine && (flipH || flipV))
            {
                skipped++;
                continue;
            }
            double rotation = ((long?)transform?.Attribute("rot") ?? 0) / 60000.0;
            if (isLine) flipV ^= flipH;
            double cropLeft = 0, cropTop = 0, cropRight = 0, cropBottom = 0;
            bool textBold = false, textItalic = false, textUnderline = false;
            double textSize = 14;
            uint textColor = 0xffffff;
            string kind, type = "", text = "";
            uint? fill = null, line = null;
            string? data = null;
            if (shape is not null)
            {
                string geometry = (string?)shape.Element(SheetDrawingNs + "spPr")?.Element(DrawingNs + "prstGeom")?.Attribute("prst") ?? "";
                type = geometry switch
                {
                    "rect" => "Rectangle",
                    "roundRect" => "RoundedRectangle",
                    "ellipse" => "Ellipse",
                    "triangle" => "Triangle",
                    "diamond" => "Diamond",
                    "pentagon" => "Pentagon",
                    "hexagon" => "Hexagon",
                    "line" => "Line",
                    "rightArrow" => "Arrow",
                    _ => ""
                };
                if (type.Length == 0)
                {
                    skipped++;
                    continue;
                }
                XElement? properties = shape.Element(SheetDrawingNs + "spPr");
                fill = ImportDrawingColor(properties, 0x4472c4);
                line = ImportDrawingColor(properties?.Element(DrawingNs + "ln"), 0x244a80);
                IEnumerable<XElement> paragraphs = shape.Element(SheetDrawingNs + "txBody")?.Elements(DrawingNs + "p") ?? [];
                text = string.Join('\n',
                    paragraphs.Select(paragraph => string.Concat(paragraph.Descendants(DrawingNs + "t").Select(node => node.Value))));
                XElement? runProperties = paragraphs.SelectMany(paragraph => paragraph.Descendants(DrawingNs + "rPr")).FirstOrDefault();
                if (runProperties is not null)
                {
                    textBold = (string?)runProperties.Attribute("b") is "1" or "true";
                    textItalic = (string?)runProperties.Attribute("i") is "1" or "true";
                    textUnderline = (string?)runProperties.Attribute("u") is string underline && underline != "none";
                    if ((int?)runProperties.Attribute("sz") is int size) textSize = Math.Clamp(size / 75.0, 4, 400);
                    textColor = ImportDrawingColor(runProperties, 0xffffff) ?? 0xffffff;
                }
                kind = "shape";
            }
            else if (picture is not null)
            {
                XElement? fillElement = picture.Element(SheetDrawingNs + "blipFill");
                XElement? crop = fillElement?.Element(DrawingNs + "srcRect");
                cropLeft = ((int?)crop?.Attribute("l") ?? 0) / 100000.0;
                cropTop = ((int?)crop?.Attribute("t") ?? 0) / 100000.0;
                cropRight = ((int?)crop?.Attribute("r") ?? 0) / 100000.0;
                cropBottom = ((int?)crop?.Attribute("b") ?? 0) / 100000.0;
                if (cropLeft < 0 || cropTop < 0 || cropRight < 0 || cropBottom < 0 || cropLeft + cropRight >= 1 || cropTop + cropBottom >= 1)
                {
                    skipped++;
                    continue;
                }
                string imageId = (string?)fillElement?.Element(DrawingNs + "blip")?.Attribute(RelNs + "embed") ?? "";
                if (!drawingRelations.TryGetValue(imageId,
                    out PackageRelationship? imageRelation) || imageRelation.Type != RelNs.NamespaceName + "/image")
                {
                    skipped++;
                    continue;
                }
                ZipArchiveEntry entry = zip.GetEntry(imageRelation.Target) ?? throw new InvalidDataException("画像の参照が無効です。");
                if (entry.Length is <= 0 or > 16 * 1024 * 1024)
                {
                    skipped++;
                    continue;
                }
                using Stream input = entry.Open();
                using MemoryStream output = new();
                input.CopyTo(output);
                byte[] bytes = output.ToArray();
                bool png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                bool jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
                if (!png && !jpeg)
                {
                    skipped++;
                    continue;
                }
                data = Convert.ToBase64String(bytes);
                if (sheet.Objects.Sum(item => (long)(item.Data?.Length ?? 0)) + data.Length > 60 * 1024 * 1024)
                {
                    skipped++;
                    continue;
                }
                XElement? props = picture.Element(SheetDrawingNs + "nvPicPr")?.Element(SheetDrawingNs + "cNvPr");
                text = (string?)props?.Attribute("descr") ?? (string?)props?.Attribute("name") ?? "画像";
                kind = "image";
            }
            else
            {
                skipped++;
                continue;
            }
            (col, dx) = NormalizeDrawingPosition(col, dx, widths, 100, 16383);
            (row, dy) = NormalizeDrawingPosition(row, dy, heights, 24, 1048575);
            sheet.Objects.Add(new((uint)(sheet.Objects.Count + 1), kind, type, row, col, dx, dy, width, height, fill, line, text, data,
                rotation, cropLeft, cropTop, cropRight, cropBottom, flipV, textBold, textItalic, textUnderline, textSize, textColor));
            imported++;
        }
        return (imported, skipped);
    }

    static uint? ImportDrawingColor(XElement? properties, uint fallback)
    {
        if (properties?.Element(DrawingNs + "noFill") is not null) return null;
        string? value = (string?)properties?.Element(DrawingNs + "solidFill")?.Element(DrawingNs + "srgbClr")?.Attribute("val");
        return value is not null && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
            out uint color) && color <= 0xffffff ? color : fallback;
    }

    static bool TryImportAnchor(XElement anchor, AxisOffsets columns, AxisOffsets rows, out int row, out int col,
        out double dx, out double dy, out double width, out double height)
    {
        row = col = 0;
        dx = dy = width = height = 0;
        XElement? from = anchor.Element(SheetDrawingNs + "from"), extent = anchor.Element(SheetDrawingNs + "ext"),
            to = anchor.Element(SheetDrawingNs + "to");
        if (from is not null)
        {
            col = (int?)from.Element(SheetDrawingNs + "col") ?? -1;
            row = (int?)from.Element(SheetDrawingNs + "row") ?? -1;
            dx = ((long?)from.Element(SheetDrawingNs + "colOff") ?? 0) / EmuPerPixel;
            dy = ((long?)from.Element(SheetDrawingNs + "rowOff") ?? 0) / EmuPerPixel;
        }
        else if (anchor.Name == SheetDrawingNs + "absoluteAnchor")
        {
            XElement? position = anchor.Element(SheetDrawingNs + "pos");
            if (position is null) return false;
            dx = ((long?)position.Attribute("x") ?? 0) / EmuPerPixel;
            dy = ((long?)position.Attribute("y") ?? 0) / EmuPerPixel;
        }
        else return false;
        if (row is < 0 or >= 1048576 || col is < 0 or >= 16384 || Math.Abs(dx) > 100000 || Math.Abs(dy) > 100000) return false;
        if (extent is not null)
        {
            width = ((long?)extent.Attribute("cx") ?? 0) / EmuPerPixel;
            height = ((long?)extent.Attribute("cy") ?? 0) / EmuPerPixel;
        }
        else if (to is not null)
        {
            int endCol = (int?)to.Element(SheetDrawingNs + "col") ?? -1, endRow = (int?)to.Element(SheetDrawingNs + "row") ?? -1;
            if (endCol is < 0 or >= 16384 || endRow is < 0 or >= 1048576) return false;
            double endX = ((long?)to.Element(SheetDrawingNs + "colOff") ?? 0) / EmuPerPixel,
                endY = ((long?)to.Element(SheetDrawingNs + "rowOff") ?? 0) / EmuPerPixel;
            width = columns.At(endCol) - columns.At(col) + endX - dx;
            height = rows.At(endRow) - rows.At(row) + endY - dy;
        }
        XElement? transformExtent = (anchor.Element(SheetDrawingNs + "sp") ??
            anchor.Element(SheetDrawingNs + "pic"))?.Element(SheetDrawingNs + "spPr")?.Element(DrawingNs + "xfrm")?.Element(
                DrawingNs + "ext");
        if (transformExtent is not null && (long?)transformExtent.Attribute("cx") is long cx &&
            (long?)transformExtent.Attribute("cy") is long cy && cx > 0 && cy > 0)
        {
            width = cx / EmuPerPixel;
            height = cy / EmuPerPixel;
        }
        bool isLine = (string?)anchor.Element(SheetDrawingNs + "sp")?.Element(SheetDrawingNs + "spPr")?
            .Element(DrawingNs + "prstGeom")?.Attribute("prst") == "line";
        if (isLine && width >= 0 && height >= 0)
        {
            width = Math.Max(.01, width);
            height = Math.Max(.01, height);
        }
        double minimum = isLine ? .01 : 4;
        return double.IsFinite(width) && double.IsFinite(height) && width >= minimum && width <= 10000 &&
            height >= minimum && height <= 10000;
    }
}
