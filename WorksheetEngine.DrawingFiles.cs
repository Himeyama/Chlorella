using System.IO.Compression;
using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record DrawingMetadata(DrawingObject[] Objects, WorksheetTable[] Tables);

    static void WriteDrawingMetadata(ZipArchive zip, int index, Sheet sheet)
    {
        if (sheet.Objects.Count == 0 && sheet.Tables.Count == 0) return;
        using Stream stream = zip.CreateEntry($"chlorella/sheet{index + 1}.json").Open();
        JsonSerializer.Serialize(stream, new DrawingMetadata([.. sheet.Objects], [.. sheet.Tables]));
    }

    static void ReadDrawingMetadata(ZipArchive zip, int index, Sheet sheet)
    {
        ZipArchiveEntry? entry = zip.GetEntry($"chlorella/sheet{index + 1}.json");
        if (entry is null) return;
        if (entry.Length > 64 * 1024 * 1024) throw new InvalidDataException("図形のデータが大きすぎます。");
        using Stream stream = entry.Open();
        DrawingMetadata metadata = JsonSerializer.Deserialize<DrawingMetadata>(stream) ?? throw new InvalidDataException("図形のデータが無効です。");
        if (metadata.Objects is null || metadata.Tables is null || metadata.Objects.Length > 10000 || metadata.Tables.Length > 10000 ||
            metadata.Objects.Any(item => item is null || item.Id == 0 || item.Kind is not ("shape" or "image") || item.Row is < 0 or >= 1048576 || item.Col is < 0 or >= 16384 || !double.IsFinite(item.Dx) || !double.IsFinite(item.Dy) || !double.IsFinite(item.Width) || !double.IsFinite(item.Height) || item.Width is < 4 or > 10000 || item.Height is < 4 or > 10000 || item.Text is null || (item.Kind == "image" && item.Data is null)) ||
            metadata.Objects.Select(item => item.Id).Distinct().Count() != metadata.Objects.Length ||
            metadata.Tables.Any(t => t is null || t.Top < 0 || t.Left < 0 || t.Bottom < t.Top || t.Right < t.Left || t.Bottom >= 1048576 || t.Right >= 16384 || (t.Filters is not null && (!t.Header || t.Filters.Length > t.Right - t.Left + 1 || t.Filters.Any(filter => filter is null || filter.Column < t.Left || filter.Column > t.Right || filter.Values is null || filter.Values.Length > 100000 || filter.Values.Any(value => value is null)) || t.Filters.Select(filter => filter.Column).Distinct().Count() != t.Filters.Length))))
            throw new InvalidDataException("図形またはテーブルのデータが無効です。");
        sheet.Objects = [.. metadata.Objects]; sheet.Tables = [.. metadata.Tables];
    }
}
