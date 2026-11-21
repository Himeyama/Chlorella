using System.IO.Compression;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record PackageRelationship(string Type, string Target);

    static Dictionary<string, PackageRelationship> ReadPartRelationships(ZipArchive zip, string part)
    {
        int slash = part.LastIndexOf('/');
        string directory = slash < 0 ? "" : part[..(slash + 1)], filename = part[(slash + 1)..];
        string relPath = $"{directory}_rels/{filename}.rels";
        if (zip.GetEntry(relPath) is null) return [];
        XDocument relations = ReadXml(zip, relPath);
        Dictionary<string, PackageRelationship> result = new(StringComparer.Ordinal);
        foreach (XElement relation in relations.Root?.Elements(PackageNs + "Relationship") ?? [])
        {
            if ((string?)relation.Attribute("TargetMode") == "External") continue;
            string id = (string?)relation.Attribute("Id") ?? throw new InvalidDataException("ファイルの参照が無効です。");
            string target = (string?)relation.Attribute("Target") ?? throw new InvalidDataException("ファイルの参照が無効です。");
            string type = (string?)relation.Attribute("Type") ?? "";
            Uri resolved = new(new Uri("http://localhost/" + part), target);
            if (resolved.Host != "localhost" || resolved.Scheme != "http" || resolved.Query.Length != 0 ||
                resolved.Fragment.Length != 0) throw new InvalidDataException("ファイルの参照が無効です。");
            result.Add(id, new(type, Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'))));
        }
        return result;
    }

    static (int Imported, int Skipped, int SkippedFilters) ImportTableParts(ZipArchive zip, string worksheetPart,
        XDocument worksheet, Sheet sheet)
    {
        XElement[] tableParts = worksheet.Root?.Element(MainNs + "tableParts")?.Elements(MainNs + "tablePart").ToArray() ?? [];
        if (tableParts.Length == 0) return (0, 0, 0);
        if (tableParts.Length > 10000) throw new InvalidDataException("テーブルが多すぎます。");
        Dictionary<string, PackageRelationship> relations = ReadPartRelationships(zip, worksheetPart);
        int imported = 0, skipped = 0, skippedFilters = 0;
        foreach (XElement part in tableParts)
        {
            string id = (string?)part.Attribute(RelNs + "id") ?? "";
            if (!relations.TryGetValue(id,
                out PackageRelationship? relation) ||
                    relation.Type != RelNs.NamespaceName + "/table") throw new InvalidDataException("テーブルの参照が無効です。");
            XElement table = ReadXml(zip, relation.Target).Root ?? throw new InvalidDataException("テーブルのデータが無効です。");
            if (table.Name != MainNs + "table") throw new InvalidDataException("テーブルのデータが無効です。");
            (int top, int left, int bottom, int right) = ParseRange((string?)table.Attribute("ref") ?? "");
            int headers = (int?)table.Attribute("headerRowCount") ?? 1;
            int totals = (int?)table.Attribute("totalsRowCount") ?? 0;
            CellRange range = new(top, left, bottom, right);
            if (headers is < 0 or > 1 || totals is < 0 or > 1 || bottom - top + 1 < headers + totals ||
                (long)(bottom - top + 1) * (right - left + 1) > 100000 ||
                sheet.Merges.Any(merge => merge.Intersects(range)) || sheet.Tables.Any(other => new CellRange(other.Top,
                    other.Left, other.Bottom, other.Right).Intersects(range)))
            {
                skipped++;
                continue;
            }
            List<TableFilter> filters = [];
            foreach (XElement filterColumn in table.Element(MainNs + "autoFilter")?.Elements(MainNs + "filterColumn") ?? [])
            {
                int offset = (int?)filterColumn.Attribute("colId") ?? -1;
                XElement? values = filterColumn.Element(MainNs + "filters");
                if (headers != 1 || offset < 0 || offset > right - left || values is null ||
                    filterColumn.Elements().Any(child => child != values) ||
                        values.Elements().Any(child => child.Name != MainNs + "filter" || child.Attribute("val") is null) ||
                            filters.Any(filter => filter.Column == left + offset))
                {
                    skippedFilters++;
                    continue;
                }
                List<string> accepted = [];
                if ((string?)values.Attribute("blank") is "1" or "true") accepted.Add("");
                foreach (XElement filter in values.Elements(MainNs + "filter"))
                {
                    string? value = (string?)filter.Attribute("val");
                    if (value is not null) accepted.Add(value);
                }
                if (accepted.Count > 100000) throw new InvalidDataException("フィルターの値が多すぎます。");
                filters.Add(new(left + offset, accepted.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }
            sheet.Tables.Add(new(top, left, bottom, right, headers == 1, [.. filters],
                (string?)table.Attribute("displayName") ?? (string?)table.Attribute("name"), totals == 1));
            imported++;
        }
        return (imported, skipped, skippedFilters);
    }
}
