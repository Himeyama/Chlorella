using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static void ValidateTableLayout(Sheet sheet)
    {
        for (int i = 0; i < sheet.Tables.Count; i++)
        {
            WorksheetTable table = sheet.Tables[i];
            CellRange range = new(table.Top, table.Left, table.Bottom, table.Right);
            bool merged = sheet.Merges.Any(item => item.Intersects(range));
            bool overlaps = sheet.Tables.Take(i).Any(other => new CellRange(other.Top, other.Left, other.Bottom,
                other.Right).Intersects(range));
            if (merged || overlaps) throw new InvalidOperationException(
                $"シート「{sheet.Name}」のテーブル {Address(table.Top, table.Left)}:{Address(table.Bottom, table.Right)}" +
                    $" は結合セルまたは別のテーブルと重なっています。保存前に範囲を変更してください。");
        }
    }

    static void WriteWorksheetObjectRelationships(ZipArchive zip, int sheetIndex, Sheet sheet, int firstTableId)
    {
        if (sheet.Objects.Count == 0 && sheet.Tables.Count == 0) return;
        XElement relationships = new(PackageNs + "Relationships");
        if (sheet.Objects.Count > 0) relationships.Add(new XElement(PackageNs + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", RelNs.NamespaceName + "/drawing"), new XAttribute("Target",
                $"../drawings/drawing{sheetIndex + 1}.xml")));
        for (int index = 0; index < sheet.Tables.Count; index++)
            relationships.Add(new XElement(PackageNs + "Relationship", new XAttribute("Id", $"rId{index + 2}"),
                new XAttribute("Type", RelNs.NamespaceName + "/table"), new XAttribute("Target",
                    $"../tables/table{firstTableId + index}.xml")));
        WriteXml(zip, $"xl/worksheets/_rels/sheet{sheetIndex + 1}.xml.rels", new XDocument(relationships));
    }

    static void WriteNativeTables(ZipArchive zip, Sheet sheet, int firstTableId, HashSet<string> tableNames)
    {
        for (int index = 0; index < sheet.Tables.Count; index++)
        {
            WorksheetTable table = sheet.Tables[index];
            int id = firstTableId + index;
            string range = $"{Address(table.Top, table.Left)}:{Address(table.Bottom, table.Right)}";
            string[]? headers = NativeTableHeaders(sheet, table);
            bool header = headers is not null;
            headers ??= Enumerable.Range(1, table.Right - table.Left + 1).Select(column => $"Column{column}").ToArray();
            string tableName = table.Name ?? $"Table{id}";
            if (table.Name is null)
            {
                int suffix = id;
                while (!tableNames.Add(tableName)) tableName = $"Table{++suffix}";
            }
            XElement definition = new(MainNs + "table", new XAttribute("id", id), new XAttribute("name", tableName),
                new XAttribute("displayName", tableName), new XAttribute("ref", range), new XAttribute("headerRowCount",
                    header ? 1 : 0), new XAttribute("totalsRowShown", table.Totals ? 1 : 0),
                table.Totals ? new XAttribute("totalsRowCount", 1) : null,
                header ? new XElement(MainNs + "autoFilter", new XAttribute("ref", $"{Address(table.Top, table.Left)}:{Address(table.Bottom - (table.Totals ? 1 : 0), table.Right)}"), (table.Filters ?? []).Select(filter =>
                    new XElement(MainNs + "filterColumn", new XAttribute("colId", filter.Column - table.Left),
                        new XElement(MainNs + "filters", filter.Values.Any(string.IsNullOrEmpty) ? new XAttribute("blank", 1) : null,
                            filter.Values.Where(value => value.Length > 0).Select(value => new XElement(MainNs + "filter",
                                new XAttribute("val", value))))))) : null,
                new XElement(MainNs + "tableColumns", new XAttribute("count", headers.Length), headers.Select((name,
                    column) => MakeTableColumn(sheet, table, name, column))),
                new XElement(MainNs + "tableStyleInfo", new XAttribute("name", "TableStyleMedium2"),
                    new XAttribute("showFirstColumn", 0), new XAttribute("showLastColumn", 0),
                        new XAttribute("showRowStripes", 1), new XAttribute("showColumnStripes", 0)));
            WriteXml(zip, $"xl/tables/table{id}.xml", new XDocument(definition));
        }
    }

    static XElement MakeTableColumn(Sheet sheet, WorksheetTable table, string name, int column)
    {
        XElement result = new(MainNs + "tableColumn", new XAttribute("id", column + 1), new XAttribute("name", name));
        if (!table.Totals) return result;
        string value = sheet.Cells.GetValueOrDefault((table.Bottom, table.Left + column), "");
        if (value.StartsWith('='))
        {
            result.Add(new XAttribute("totalsRowFunction", "custom"));
            result.Add(new XElement(MainNs + "totalsRowFormula", value[1..]));
        }
        else if (value.Length > 0) result.Add(new XAttribute("totalsRowLabel", value));
        return result;
    }

    // Excel requires unique, nonempty text in header cells. Preserve nonconforming
    // cell values by exporting those ranges as headerless native tables.
    static string[]? NativeTableHeaders(Sheet sheet, WorksheetTable table)
    {
        if (!table.Header) return null;
        List<string> headers = [];
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        for (int col = table.Left; col <= table.Right; col++)
        {
            string value = sheet.Cells.GetValueOrDefault((table.Top, col), "");
            if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.StartsWith('=') || !unique.Add(value)) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double number) && double.IsFinite(number) && number.ToString("R", CultureInfo.InvariantCulture) == value) return null;
            headers.Add(value);
        }
        return [.. headers];
    }
}
