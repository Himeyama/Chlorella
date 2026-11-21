using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PackageNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    int SaveWorkbook(string path)
    {
        path = Path.GetFullPath(path);
        if (importedSourcePath is not null && string.Equals(path, importedSourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("読み込み元の Excel ブックは上書きできません。別のファイル名で保存してください。");
        foreach (Sheet sheet in sheets) ValidateTableLayout(sheet);
        string temp = Path.Combine(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("保存先が無効です。"), $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                List<FontStyle> fonts = [DefaultFont];
                foreach (FontStyle font in sheets.SelectMany(s => s.Fonts.Values).Distinct()) if (!fonts.Contains(font)) fonts.Add(font);
                XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
                WriteXml(zip, "[Content_Types].xml", new XDocument(new XElement(content + "Types",
                    new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(content + "Default", new XAttribute("Extension", "txt"), new XAttribute("ContentType", "text/plain")),
                    new XElement(content + "Default", new XAttribute("Extension", "json"), new XAttribute("ContentType", "application/json")),
                    new XElement(content + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                    new XElement(content + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
                    new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(content + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                    sheets.Select((s, i) => new XElement(content + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))),
                    sheets.Select((s, i) => (sheet: s, index: i)).Where(p => p.sheet.Objects.Count > 0).Select(p => new XElement(content + "Override", new XAttribute("PartName", $"/xl/drawings/drawing{p.index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml"))),
                    Enumerable.Range(1, sheets.Sum(s => s.Tables.Count)).Select(id => new XElement(content + "Override", new XAttribute("PartName", $"/xl/tables/table{id}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"))))));
                WriteXml(zip, "_rels/.rels", new XDocument(new XElement(PackageNs + "Relationships", new XElement(PackageNs + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", RelNs.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
                WriteXml(zip, "xl/workbook.xml", new XDocument(new XElement(MainNs + "workbook", new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(MainNs + "bookViews", new XElement(MainNs + "workbookView", new XAttribute("activeTab", activeSheet))),
                    new XElement(MainNs + "sheets", sheets.Select((s, i) => new XElement(MainNs + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(RelNs + "id", $"rId{i + 1}")))),
                    new XElement(MainNs + "calcPr", new XAttribute("fullCalcOnLoad", "1")))));
                WriteXml(zip, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(PackageNs + "Relationships", sheets.Select((s, i) => new XElement(PackageNs + "Relationship", new XAttribute("Id", $"rId{i + 1}"), new XAttribute("Type", RelNs.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{i + 1}.xml"))), new XElement(PackageNs + "Relationship", new XAttribute("Id", $"rId{sheets.Count + 1}"), new XAttribute("Type", RelNs.NamespaceName + "/styles"), new XAttribute("Target", "styles.xml")))));
                WriteXml(zip, "xl/styles.xml", MakeStyles(fonts));
                for (int i = 0; i < sheets.Count; i++)
                {
                    Sheet sheet = sheets[i];
                    HashSet<int> hiddenRows = FilteredRows(sheet);
                    int firstTableId = 1 + sheets.Take(i).Sum(s => s.Tables.Count);
                    IEnumerable<XElement> rows = sheet.Cells.Keys.Union(sheet.Fonts.Keys).Union(sheet.RowHeights.Keys.Select(row => (Row: row, Col: -1))).Union(hiddenRows.Select(row => (Row: row, Col: -1))).OrderBy(p => p.Row).ThenBy(p => p.Col).GroupBy(p => p.Row).Select(group =>
                        new XElement(MainNs + "row", new XAttribute("r", group.Key + 1), hiddenRows.Contains(group.Key) ? new XAttribute("hidden", 1) : null, sheet.RowHeights.TryGetValue(group.Key, out double height) ? new object[] { new XAttribute("ht", height * 0.75), new XAttribute("customHeight", 1) } : null, group.Where(cell => cell.Col >= 0).Select(cell => CreateCell(cell.Row, cell.Col, sheet.Cells.GetValueOrDefault(cell), fonts.IndexOf(sheet.Fonts.GetValueOrDefault(cell, DefaultFont))))));
                    WriteXml(zip, $"xl/worksheets/sheet{i + 1}.xml", new XDocument(new XElement(MainNs + "worksheet", new XAttribute(XNamespace.Xmlns + "r", RelNs),
                        sheet.TabColor is uint tabColor ? new XElement(MainNs + "sheetPr", new XElement(MainNs + "tabColor", new XAttribute("rgb", $"FF{tabColor:X6}"))) : null,
                        sheet.ColumnWidths.Count == 0 ? null : new XElement(MainNs + "cols", sheet.ColumnWidths.OrderBy(p => p.Key).Select(p => new XElement(MainNs + "col", new XAttribute("min", p.Key + 1), new XAttribute("max", p.Key + 1), new XAttribute("width", p.Value / 7), new XAttribute("customWidth", 1)))),
                        new XElement(MainNs + "sheetData", rows),
                        sheet.ProtectionHash is ushort protection ? new XElement(MainNs + "sheetProtection", new XAttribute("sheet", 1), new XAttribute("objects", 1), new XAttribute("scenarios", 1), new XAttribute("password", protection.ToString("X4", CultureInfo.InvariantCulture))) : null,
                        sheet.Merges.Count == 0 ? null : new XElement(MainNs + "mergeCells", new XAttribute("count", sheet.Merges.Count), sheet.Merges.Select(m => new XElement(MainNs + "mergeCell", new XAttribute("ref", $"{Address(m.Top, m.Left)}:{Address(m.Bottom, m.Right)}")))),
                        sheet.Objects.Count == 0 ? null : new XElement(MainNs + "drawing", new XAttribute(RelNs + "id", "rId1")),
                        sheet.Tables.Count == 0 ? null : new XElement(MainNs + "tableParts", new XAttribute("count", sheet.Tables.Count), sheet.Tables.Select((table, tableIndex) => new XElement(MainNs + "tablePart", new XAttribute(RelNs + "id", $"rId{tableIndex + 2}")))))));
                    WriteWorksheetObjectRelationships(zip, i, sheet, firstTableId);
                    WriteNativeDrawing(zip, i, sheet);
                    WriteNativeTables(zip, sheet, firstTableId);
                    WriteDrawingMetadata(zip, i, sheet);
                }
            }
            using (FileStream stream = new(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Update))
            {
                string digest = WorkbookDigest(zip);
                using StreamWriter marker = new(zip.CreateEntry("chlorella-format.txt").Open(), new UTF8Encoding(false));
                marker.Write(digest);
            }
            File.Move(temp, path, true);
            documentPath = path;
            return sheets.Sum(sheet => sheet.Tables.Count(table => table.Header && NativeTableHeaders(sheet, table) is null));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static XElement CreateCell(int row, int col, string? value, int style)
    {
        XElement cell = new(MainNs + "c", new XAttribute("r", Address(row, col)));
        if (style != 0) cell.Add(new XAttribute("s", style));
        if (value is null) return cell;
        if (value.StartsWith('=')) cell.Add(new XElement(MainNs + "f", value[1..]));
        else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) && number.ToString("R", CultureInfo.InvariantCulture) == value) cell.Add(new XElement(MainNs + "v", value));
        else cell.Add(new XAttribute("t", "inlineStr"), new XElement(MainNs + "is", new XElement(MainNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
        return cell;
    }

    static void WriteXml(ZipArchive zip, string name, XDocument document)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        document.Save(stream);
    }

    object OpenWorkbook(string path)
    {
        path = Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(path);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        // Refuse unknown workbooks rather than silently discard unsupported Excel features.
        ZipArchiveEntry? marker = zip.GetEntry("chlorella-format.txt");
        if (marker is null) return ImportExternalWorkbook(zip, path, false);
        using (StreamReader reader = new(marker.Open()))
            if (reader.ReadToEnd() != WorkbookDigest(zip)) return ImportExternalWorkbook(zip, path, true);
        XDocument workbook = ReadXml(zip, "xl/workbook.xml");
        List<FontStyle> fonts = ReadStyles(zip);
        XElement[] sheetElements = workbook.Root?.Element(MainNs + "sheets")?.Elements(MainNs + "sheet").ToArray() ?? [];
        if (sheetElements.Length == 0 || sheetElements.Length > 1000) throw new InvalidDataException("シート情報が無効です。");
        List<Sheet> loaded = [];
        for (int i = 0; i < sheetElements.Length; i++)
        {
            string name = (string?)sheetElements[i].Attribute("name") ?? throw new InvalidDataException("シート名がありません。");
            Sheet sheet = new(name);
            XDocument data = ReadXml(zip, $"xl/worksheets/sheet{i + 1}.xml");
            XElement? protection = data.Root?.Element(MainNs + "sheetProtection");
            if (protection is not null && (string?)protection.Attribute("sheet") is not ("0" or "false"))
            {
                string hash = (string?)protection.Attribute("password") ?? "0000";
                if (!ushort.TryParse(hash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort parsed)) throw new InvalidDataException("シート保護の情報が無効です。");
                sheet.ProtectionHash = parsed;
            }
            sheet.TabColor = ReadRgb(data.Root?.Element(MainNs + "sheetPr")?.Element(MainNs + "tabColor"));
            foreach (XElement column in data.Root?.Element(MainNs + "cols")?.Elements(MainNs + "col") ?? [])
            {
                int min = (int?)column.Attribute("min") ?? 0, max = (int?)column.Attribute("max") ?? 0;
                double width = ((double?)column.Attribute("width") ?? 0) * 7;
                if (min < 1 || max < min || max > 16384 || !double.IsFinite(width) || width <= 0 || width > 10000) throw new InvalidDataException("列幅が無効です。");
                for (int col = min - 1; col < max; col++) sheet.ColumnWidths[col] = width;
            }
            foreach (XElement row in data.Root?.Element(MainNs + "sheetData")?.Elements(MainNs + "row") ?? [])
            {
                if (row.Attribute("ht") is null) continue;
                int rowIndex = (int?)row.Attribute("r") ?? 0; double height = (double)row.Attribute("ht")! / 0.75;
                if (rowIndex < 1 || rowIndex > 1048576 || !double.IsFinite(height) || height <= 0 || height > 10000) throw new InvalidDataException("行の高さが無効です。");
                sheet.RowHeights[rowIndex - 1] = height;
            }
            foreach (XElement cell in data.Descendants(MainNs + "c"))
            {
                (int row, int col) = ParseCell((string?)cell.Attribute("r") ?? "");
                int style = (int?)cell.Attribute("s") ?? 0;
                if (style < 0 || style >= fonts.Count) throw new InvalidDataException("セルの書式が無効です。");
                if (fonts[style] != DefaultFont) sheet.Fonts.Add((row, col), fonts[style]);
                string? formula = (string?)cell.Element(MainNs + "f");
                string value = formula is not null ? "=" + formula : (string?)cell.Element(MainNs + "is")?.Element(MainNs + "t") ?? (string?)cell.Element(MainNs + "v") ?? "";
                if (value.Length > 0) sheet.Cells.Add((row, col), value);
            }
            foreach (XElement merged in data.Root?.Element(MainNs + "mergeCells")?.Elements(MainNs + "mergeCell") ?? [])
            {
                (int top, int left, int bottom, int right) = ParseRange((string?)merged.Attribute("ref") ?? "");
                CellRange range = new(top, left, bottom, right);
                if ((top == bottom && left == right) || sheet.Merges.Any(m => m.Intersects(range))) throw new InvalidDataException("結合セルの情報が無効です。");
                sheet.Merges.Add(range);
            }
            ReadDrawingMetadata(zip, i, sheet);
            loaded.Add(sheet);
        }
        int index = (int?)workbook.Root?.Element(MainNs + "bookViews")?.Element(MainNs + "workbookView")?.Attribute("activeTab") ?? 0;
        sheets.Clear(); sheets.AddRange(loaded); activeSheet = Math.Clamp(index, 0, sheets.Count - 1); documentPath = path; importedSourcePath = null;
        return new { imported = false };
    }

    object ImportExternalWorkbook(ZipArchive zip, string path, bool modified)
    {
        (int importedTables, int skippedTables, int skippedFilters, int importedDrawings, int skippedDrawings) = ImportWorkbook(zip, path);
        return new { imported = true, modified, importedTables, skippedTables, skippedFilters, importedDrawings, skippedDrawings };
    }

    static XDocument ReadXml(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidDataException("ブックの構造が無効です。");
        if (entry.Length > 64 * 1024 * 1024) throw new InvalidDataException("ブックのデータが大きすぎます。");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    static string WorkbookDigest(ZipArchive zip)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName != "chlorella-format.txt").OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (entry.Length > 64 * 1024 * 1024) throw new InvalidDataException("ブックのデータが大きすぎます。");
            hash.AppendData(Encoding.UTF8.GetBytes(entry.FullName + "\0"));
            using Stream input = entry.Open();
            byte[] buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    object RenameFile(JsonElement args)
    {
        string oldPath = documentPath ?? throw new InvalidOperationException("先に保存してください。");
        string name = args.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(".xlsx のファイル名を指定してください。");
        string newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? "", name);
        File.Move(oldPath, newPath); documentPath = newPath; return new { };
    }

    void ExportText(string path, bool html)
    {
        int maxRow = Current.Cells.Count == 0 ? 0 : Current.Cells.Keys.Max(p => p.Row);
        int maxCol = Current.Cells.Count == 0 ? 0 : Current.Cells.Keys.Max(p => p.Col);
        if ((long)(maxRow + 1) * (maxCol + 1) > 1_000_000) throw new InvalidOperationException("出力範囲が大きすぎます。");
        using StreamWriter writer = new(path, false, new UTF8Encoding(true));
        if (html) writer.WriteLine("<!doctype html><html><head><meta charset=\"utf-8\"></head><body><table>");
        for (int row = 0; row <= maxRow; row++)
        {
            if (FilteredRows().Contains(row)) continue;
            if (html) writer.Write("<tr>");
            for (int col = 0; col <= maxCol; col++)
            {
                string value = FormatCell(Current, (row, col), Current.Fonts.GetValueOrDefault((row, col), DefaultFont)).Text;
                if (html) writer.Write("<td>" + WebUtility.HtmlEncode(value) + "</td>");
                else { if (col > 0) writer.Write(','); writer.Write('"' + value.Replace("\"", "\"\"") + '"'); }
            }
            writer.WriteLine(html ? "</tr>" : "");
        }
        if (html) writer.WriteLine("</table></body></html>");
    }
}
