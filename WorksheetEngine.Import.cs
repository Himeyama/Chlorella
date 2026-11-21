using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    (int ImportedTables, int SkippedTables, int SkippedFilters, int ImportedDrawings, int SkippedDrawings) ImportWorkbook(ZipArchive zip, string sourcePath)
    {
        XDocument workbook = ReadXml(zip, "xl/workbook.xml");
        XDocument relations = ReadXml(zip, "xl/_rels/workbook.xml.rels");
        Dictionary<string, string> targets = new();
        foreach (XElement relation in relations.Root?.Elements(PackageNs + "Relationship") ?? [])
        {
            if ((string?)relation.Attribute("TargetMode") == "External") continue;
            if ((string?)relation.Attribute("Type") != RelNs.NamespaceName + "/worksheet") continue;
            string id = (string?)relation.Attribute("Id") ?? throw new InvalidDataException("シートの参照が無効です。");
            string target = (string?)relation.Attribute("Target") ?? throw new InvalidDataException("シートの参照が無効です。");
            Uri resolved = new(new Uri("http://localhost/xl/workbook.xml"), target);
            if (resolved.Host != "localhost") throw new InvalidDataException("シートの参照が無効です。");
            targets.Add(id, Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/')));
        }
        List<string> shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] :
            (ReadXml(zip, "xl/sharedStrings.xml").Root?.Elements(MainNs + "si").Select(ImportRichText).ToList() ?? []);
        XElement[] elements = workbook.Root?.Element(MainNs + "sheets")?.Elements(MainNs + "sheet").ToArray() ?? [];
        if (elements.Length is 0 or > 1000) throw new InvalidDataException("シート情報が無効です。");
        List<Sheet> loaded = [];
        int cellCount = 0, importedTables = 0, skippedTables = 0, skippedFilters = 0, importedDrawings = 0, skippedDrawings = 0;
        foreach (XElement element in elements)
        {
            string name = (string?)element.Attribute("name") ?? throw new InvalidDataException("シート名がありません。");
            string id = (string?)element.Attribute(RelNs + "id") ?? "";
            if (!targets.TryGetValue(id, out string? target)) throw new NotSupportedException("ワークシート以外のシートは読み込めません。");
            XDocument xml = ReadXml(zip, target);
            if (xml.Root?.Element(MainNs + "sheetProtection") is not null) throw new NotSupportedException("保護された外部シートは読み込めません。保護を解除してください。");
            Sheet sheet = new(name);
            ImportDimensions(xml, sheet);
            foreach (XElement cell in xml.Root?.Element(MainNs + "sheetData")?.Descendants(MainNs + "c") ?? [])
            {
                if (++cellCount > 1_000_000) throw new InvalidOperationException("読み込めるセルは 100 万個までです。");
                (int row, int col) = ParseCell((string?)cell.Attribute("r") ?? "");
                XElement? formula = cell.Element(MainNs + "f");
                if (formula is not null && ((string?)formula.Attribute("t") is string type && type != "normal" || formula.Value.Length == 0))
                    throw new NotSupportedException("共有数式または配列数式を含むブックは読み込めません。");
                string raw = (string?)cell.Element(MainNs + "v") ?? "";
                string value = formula is not null ? "=" + formula.Value : ((string?)cell.Attribute("t") ?? "n") switch
                {
                    "s" => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < shared.Count ? shared[index] : throw new InvalidDataException("共有文字列の参照が無効です。"),
                    "inlineStr" => ImportRichText(cell.Element(MainNs + "is")),
                    "b" => raw == "1" ? "TRUE" : raw == "0" ? "FALSE" : throw new InvalidDataException("論理値が無効です。"),
                    "n" or "str" or "e" => raw,
                    _ => throw new NotSupportedException("未対応のセル形式を含むブックです。"),
                };
                if (value.Length > 0) sheet.Cells.Add((row, col), value);
            }
            foreach (XElement merged in xml.Root?.Element(MainNs + "mergeCells")?.Elements(MainNs + "mergeCell") ?? [])
            {
                (int top, int left, int bottom, int right) = ParseRange((string?)merged.Attribute("ref") ?? "");
                CellRange range = new(top, left, bottom, right);
                if ((top == bottom && left == right) || sheet.Merges.Any(m => m.Intersects(range))) throw new InvalidDataException("結合セルの情報が無効です。");
                sheet.Merges.Add(range);
            }
            (int imported, int skipped, int skippedFilter) = ImportTableParts(zip, target, xml, sheet);
            importedTables += imported; skippedTables += skipped; skippedFilters += skippedFilter;
            (int importedDrawing, int skippedDrawing) = ImportDrawingParts(zip, target, xml, sheet);
            importedDrawings += importedDrawing; skippedDrawings += skippedDrawing;
            loaded.Add(sheet);
        }
        int active = (int?)workbook.Root?.Element(MainNs + "bookViews")?.Element(MainNs + "workbookView")?.Attribute("activeTab") ?? 0;
        sheets.Clear(); sheets.AddRange(loaded); activeSheet = Math.Clamp(active, 0, loaded.Count - 1);
        importedSourcePath = sourcePath; documentPath = null;
        return (importedTables, skippedTables, skippedFilters, importedDrawings, skippedDrawings);
    }

    static string ImportRichText(XElement? parent) => parent is null ? "" : string.Concat(parent.Elements().Where(e => e.Name == MainNs + "t" || e.Name == MainNs + "r").Select(e => e.Name == MainNs + "t" ? e.Value : (string?)e.Element(MainNs + "t") ?? ""));
}
