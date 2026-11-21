using System.IO.Compression;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace SheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    const double EmuPerPixel = 9525;

    void WriteNativeDrawing(ZipArchive zip, int sheetIndex, Sheet sheet)
    {
        if (sheet.Objects.Count == 0) return;
        int number = sheetIndex + 1;
        Dictionary<int, double> heights = sheet.Fonts.GroupBy(pair => pair.Key.Row).ToDictionary(group => group.Key, group => Math.Max(24, group.Max(pair => EstimateRowHeight(sheet, pair.Key, pair.Value))));
        foreach (KeyValuePair<int, double> entry in sheet.RowHeights) heights[entry.Key] = Math.Max(18, entry.Value);
        Dictionary<int, double> widths = sheet.ColumnWidths.ToDictionary(pair => pair.Key, pair => Math.Max(20, pair.Value));
        XElement drawing = new(SheetDrawingNs + "wsDr", new XAttribute(XNamespace.Xmlns + "xdr", SheetDrawingNs), new XAttribute(XNamespace.Xmlns + "a", DrawingNs), new XAttribute(XNamespace.Xmlns + "r", RelNs));
        XElement relationships = new(PackageNs + "Relationships");
        int imageNumber = 0;
        foreach (DrawingObject item in sheet.Objects)
        {
            (int col, double dx) = NormalizeDrawingPosition(item.Col, item.Dx, widths, 100, 16383);
            (int row, double dy) = NormalizeDrawingPosition(item.Row, item.Dy, heights, 24, 1048575);
            XElement anchor = new(SheetDrawingNs + "oneCellAnchor",
                new XElement(SheetDrawingNs + "from", new XElement(SheetDrawingNs + "col", col), new XElement(SheetDrawingNs + "colOff", (long)Math.Round(dx * EmuPerPixel)), new XElement(SheetDrawingNs + "row", row), new XElement(SheetDrawingNs + "rowOff", (long)Math.Round(dy * EmuPerPixel))),
                new XElement(SheetDrawingNs + "ext", new XAttribute("cx", (long)Math.Round(item.Width * EmuPerPixel)), new XAttribute("cy", (long)Math.Round(item.Height * EmuPerPixel))));
            if (item.Kind == "image")
            {
                byte[] bytes = Convert.FromBase64String(item.Data ?? "");
                string extension = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 ? "png" : "jpg";
                string name = $"image{number}_{++imageNumber}.{extension}", relationship = $"rId{imageNumber}";
                using (Stream output = zip.CreateEntry($"xl/media/{name}").Open()) output.Write(bytes);
                relationships.Add(new XElement(PackageNs + "Relationship", new XAttribute("Id", relationship), new XAttribute("Type", RelNs.NamespaceName + "/image"), new XAttribute("Target", $"../media/{name}")));
                anchor.Add(new XElement(SheetDrawingNs + "pic",
                    new XElement(SheetDrawingNs + "nvPicPr", new XElement(SheetDrawingNs + "cNvPr", new XAttribute("id", item.Id), new XAttribute("name", $"Picture {item.Id}"), new XAttribute("descr", item.Text)), new XElement(SheetDrawingNs + "cNvPicPr", new XElement(DrawingNs + "picLocks", new XAttribute("noChangeAspect", 1)))),
                    new XElement(SheetDrawingNs + "blipFill", new XElement(DrawingNs + "blip", new XAttribute(RelNs + "embed", relationship)), new XElement(DrawingNs + "stretch", new XElement(DrawingNs + "fillRect"))),
                    new XElement(SheetDrawingNs + "spPr", NativeTransform(item), new XElement(DrawingNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(DrawingNs + "avLst")))));
            }
            else anchor.Add(NativeShape(item));
            anchor.Add(new XElement(SheetDrawingNs + "clientData"));
            drawing.Add(anchor);
        }
        WriteXml(zip, $"xl/drawings/drawing{number}.xml", new XDocument(drawing));
        if (imageNumber > 0) WriteXml(zip, $"xl/drawings/_rels/drawing{number}.xml.rels", new XDocument(relationships));
    }

    static (int Index, double Offset) NormalizeDrawingPosition(int index, double offset, Dictionary<int, double> sizes, double fallback, int last)
    {
        while (offset < 0 && index > 0) { index--; offset += sizes.GetValueOrDefault(index, fallback); }
        offset = Math.Max(0, offset);
        while (index < last && offset >= sizes.GetValueOrDefault(index, fallback)) { offset -= sizes.GetValueOrDefault(index, fallback); index++; }
        return (index, offset);
    }

    static XElement NativeShape(DrawingObject item)
    {
        string geometry = item.Type switch { "Rectangle" => "rect", "RoundedRectangle" => "roundRect", "Ellipse" => "ellipse", "Triangle" => "triangle", "Diamond" => "diamond", "Pentagon" => "pentagon", "Hexagon" => "hexagon", "Line" => "line", "Arrow" => "rightArrow", _ => "rect" };
        XElement fill = item.Fill is uint fillColor ? new XElement(DrawingNs + "solidFill", new XElement(DrawingNs + "srgbClr", new XAttribute("val", $"{fillColor:X6}"))) : new XElement(DrawingNs + "noFill");
        XElement lineFill = item.Line is uint lineColor ? new XElement(DrawingNs + "solidFill", new XElement(DrawingNs + "srgbClr", new XAttribute("val", $"{lineColor:X6}"))) : new XElement(DrawingNs + "noFill");
        return new XElement(SheetDrawingNs + "sp",
            new XElement(SheetDrawingNs + "nvSpPr", new XElement(SheetDrawingNs + "cNvPr", new XAttribute("id", item.Id), new XAttribute("name", $"Shape {item.Id}")), new XElement(SheetDrawingNs + "cNvSpPr")),
            new XElement(SheetDrawingNs + "spPr", NativeTransform(item), new XElement(DrawingNs + "prstGeom", new XAttribute("prst", geometry), new XElement(DrawingNs + "avLst")), fill, new XElement(DrawingNs + "ln", lineFill)),
            new XElement(SheetDrawingNs + "txBody", new XElement(DrawingNs + "bodyPr", new XAttribute("anchor", "ctr")), new XElement(DrawingNs + "lstStyle"), new XElement(DrawingNs + "p", new XElement(DrawingNs + "pPr", new XAttribute("algn", "ctr")), new XElement(DrawingNs + "r", new XElement(DrawingNs + "rPr", new XAttribute("lang", "ja-JP"), new XElement(DrawingNs + "solidFill", new XElement(DrawingNs + "srgbClr", new XAttribute("val", "FFFFFF")))), new XElement(DrawingNs + "t", item.Text)), new XElement(DrawingNs + "endParaRPr", new XAttribute("lang", "ja-JP")))));
    }

    static XElement NativeTransform(DrawingObject item) => new(DrawingNs + "xfrm", new XElement(DrawingNs + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(DrawingNs + "ext", new XAttribute("cx", (long)Math.Round(item.Width * EmuPerPixel)), new XAttribute("cy", (long)Math.Round(item.Height * EmuPerPixel))));
}
