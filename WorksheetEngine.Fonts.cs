using System.Text.Json;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record FontStyle(string Name, double Size, bool Bold, bool Italic, bool Underline,
        uint? Color = null, uint? Fill = null, string Format = "General", int Decimals = 2,
        string Horizontal = "General", string Vertical = "Center", bool Wrap = false, int Indent = 0);
    static readonly FontStyle DefaultFont = new("Yu Gothic", 11, false, false, false);

    static XDocument MakeStyles(List<FontStyle> fonts) => new(new XElement(MainNs + "styleSheet",
        new XElement(MainNs + "numFmts", new XAttribute("count", fonts.Count), fonts.Select((font,
            i) => new XElement(MainNs + "numFmt", new XAttribute("numFmtId", 164 + i), new XAttribute("formatCode", ExcelFormat(font))))),
        new XElement(MainNs + "fonts", new XAttribute("count", fonts.Count), fonts.Select(font => new XElement(MainNs + "font",
            new XElement(MainNs + "sz", new XAttribute("val", font.Size)), new XElement(MainNs + "name", new XAttribute("val", font.Name)),
            font.Color is uint color ? new XElement(MainNs + "color", new XAttribute("rgb", $"FF{color:X6}")) : null,
            font.Bold ? new XElement(MainNs + "b") : null, font.Italic ? new XElement(MainNs + "i") : null,
                font.Underline ? new XElement(MainNs + "u") : null))),
        new XElement(MainNs + "fills", new XAttribute("count", fonts.Count + 2), new XElement(MainNs + "fill",
            new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))), new XElement(MainNs + "fill",
                new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125"))),
                    fonts.Select(font => new XElement(MainNs + "fill", new XElement(MainNs + "patternFill",
                        new XAttribute("patternType", font.Fill.HasValue ? "solid" : "none"),
                            font.Fill is uint fill ? new XElement(MainNs + "fgColor", new XAttribute("rgb", $"FF{fill:X6}")) : null)))),
        new XElement(MainNs + "borders", new XAttribute("count", 1), new XElement(MainNs + "border",
            new XElement(MainNs + "left"), new XElement(MainNs + "right"), new XElement(MainNs + "top"),
                new XElement(MainNs + "bottom"), new XElement(MainNs + "diagonal"))),
        new XElement(MainNs + "cellStyleXfs", new XAttribute("count", 1), new XElement(MainNs + "xf",
            new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
        new XElement(MainNs + "cellXfs", new XAttribute("count", fonts.Count), fonts.Select((font,
            i) => new XElement(MainNs + "xf", new XAttribute("numFmtId", 164 + i), new XAttribute("fontId", i),
                new XAttribute("fillId", font.Fill.HasValue ? i + 2 : 0), new XAttribute("borderId", 0),
                    new XAttribute("xfId", 0), new XAttribute("applyFont", 1), new XAttribute("applyFill", 1),
                        new XAttribute("applyNumberFormat", 1), new XAttribute("applyAlignment", 1),
                            new XElement(MainNs + "alignment", new XAttribute("horizontal",
                                font.Horizontal == "General" ? "general" : font.Horizontal.ToLowerInvariant()),
                                    new XAttribute("vertical", font.Vertical.ToLowerInvariant()), new XAttribute("wrapText",
                                        font.Wrap ? 1 : 0), new XAttribute("indent", font.Indent))))),
        new XElement(MainNs + "cellStyles", new XAttribute("count", 1), new XElement(MainNs + "cellStyle",
            new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));

    static List<FontStyle> ReadStyles(System.IO.Compression.ZipArchive zip)
    {
        if (zip.GetEntry("xl/styles.xml") is null) return [DefaultFont];
        XDocument styles = ReadXml(zip, "xl/styles.xml");
        List<FontStyle> fonts = styles.Root?.Element(MainNs + "fonts")?.Elements(MainNs + "font").Select(font => new FontStyle(
            (string?)font.Element(MainNs + "name")?.Attribute("val") ?? DefaultFont.Name,
            (double?)font.Element(MainNs + "sz")?.Attribute("val") ?? DefaultFont.Size,
            font.Element(MainNs + "b") is not null, font.Element(MainNs + "i") is not null,
                font.Element(MainNs + "u") is not null, ReadRgb(font.Element(MainNs + "color")))).ToList() ?? [];
        List<uint?> fills = styles.Root?.Element(MainNs + "fills")?.Elements(MainNs + "fill").Select(fill =>
            ReadRgb(fill.Element(MainNs + "patternFill")?.Element(MainNs + "fgColor"))).ToList() ?? [];
        Dictionary<int, string> formats =
            styles.Root?.Element(MainNs + "numFmts")?.Elements(MainNs + "numFmt").ToDictionary(x =>
                (int?)x.Attribute("numFmtId") ?? 0, x => (string?)x.Attribute("formatCode") ?? "General") ?? [];
        List<FontStyle> result = [];
        foreach (XElement xf in styles.Root?.Element(MainNs + "cellXfs")?.Elements(MainNs + "xf") ?? [])
        {
            int id = (int?)xf.Attribute("fontId") ?? 0;
            if (id < 0 || id >= fonts.Count || fonts[id].Size is < 1 or > 409 ||
                !double.IsFinite(fonts[id].Size)) throw new InvalidDataException("書体の情報が無効です。");
            int fillId = (int?)xf.Attribute("fillId") ?? 0;
            if (fillId < 0 || fillId >= fills.Count) throw new InvalidDataException("塗りつぶしの情報が無効です。");
            string code = formats.GetValueOrDefault((int?)xf.Attribute("numFmtId") ?? 0, "General");
            (string format, int decimals) = DecodeExcelFormat(code);
            XElement? alignment = xf.Element(MainNs + "alignment");
            string horizontal = (string?)alignment?.Attribute("horizontal") switch
            {
                "left" => "Left",
                "center" => "Center",
                "right" => "Right",
                _ => "General"
            };
            string vertical = (string?)alignment?.Attribute("vertical") switch { "top" => "Top", "bottom" => "Bottom", _ => "Center" };
            int indent = (int?)alignment?.Attribute("indent") ?? 0;
            if (indent is < 0 or > 15) throw new InvalidDataException("インデントが無効です。");
            result.Add(fonts[id] with
            {
                Fill = fills[fillId],
                Format = format,
                Decimals = decimals,
                Horizontal = horizontal,
                Vertical = vertical,
                Wrap = (string?)alignment?.Attribute("wrapText") == "1",
                Indent = indent
            });
        }
        if (result.Count == 0) throw new InvalidDataException("書式の情報がありません。");
        return result;
    }

    void ApplyFont(string command, JsonElement args)
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        if ((long)(bottom - top + 1) * (right - left + 1) > 100_000) throw new InvalidOperationException("書式を設定できる範囲は 100,000 セルまでです。");
        string name = command == "home.fontName" ? (args.GetProperty("name").GetString() ?? "").Trim() : "";
        if (command == "home.fontName" && (name.Length == 0 || name.Length > 255)) throw new InvalidOperationException("書体名を確認してください。");
        double size = command == "home.fontSize" ? args.GetProperty("size").GetDouble() : 0;
        if (command == "home.fontSize" && (!double.IsFinite(size) || size < 1 ||
            size > 409)) throw new InvalidOperationException("フォントサイズには 1 から 409 までの数値を入力してください。");
        FontStyle active = Current.Fonts.GetValueOrDefault(ParseCell(Current.Active), DefaultFont);
        for (int row = top; row <= bottom; row++) for (int col = left; col <= right; col++)
            {
                (int, int) key = (row, col);
                FontStyle font = Current.Fonts.GetValueOrDefault(key, DefaultFont);
                font = command switch
                {
                    "home.fontSize" => font with { Size = size },
                    "home.fontName" => font with { Name = name },
                    "home.bold" => font with { Bold = !active.Bold },
                    "home.italic" => font with { Italic = !active.Italic },
                    "home.underline" => font with { Underline = !active.Underline },
                    _ => font,
                };
                if (font == DefaultFont) Current.Fonts.Remove(key);
                else Current.Fonts[key] = font;
            }
    }
}
