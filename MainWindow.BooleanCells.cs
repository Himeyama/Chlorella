namespace Chlorella;

public sealed partial class MainWindow
{
    static bool IsBooleanCell(string value) => value.Equals("TRUE",
        StringComparison.OrdinalIgnoreCase) || value.Equals("FALSE", StringComparison.OrdinalIgnoreCase);
    const string CheckedGlyph = "\uE73A";
    const string UncheckedGlyph = "\uE739";
    static string BooleanGlyph(string value) => value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? CheckedGlyph : UncheckedGlyph;

    static Microsoft.UI.Xaml.Controls.FontIcon CreateBooleanIcon(string value) => new()
    {
        Glyph = BooleanGlyph(value),
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
        FontSize = 16,
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    async Task ToggleBooleanCellAsync(string address, bool isChecked)
    {
        await CommitInputAsync();
        await ExecuteAsync("cell.set", new { cell = address, input = isChecked ? "FALSE" : "TRUE" });
        dirty = true;
        await RefreshAsync(refreshSheets: false, updateCellValues: true);
        await SelectRangeAsync(address, address);
    }
}
