using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Chlorella;

public sealed partial class MainWindow
{
    void UpdateVisibleCellValues(JsonElement rows, JsonElement styles, bool updateFontStyles = false)
    {
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not SheetCellButton { Tag: string tag } button || tag.Contains(':') || tag == "all") continue;
            int row = Grid.GetRow(button) - 1, col = Grid.GetColumn(button) - 1;
            if (row < 0 || row >= viewRows || col < 0 || col >= viewColumns) continue;
            JsonElement cell = rows[row][col];
            string value = cell.GetProperty("text").GetString() ?? "";
            JsonElement fullStyle = styles.GetProperty(cell.GetProperty("style").GetRawText());
            JsonElement alignment = fullStyle.GetProperty("align");
            if (IsBooleanCell(value))
            {
                if (button.Content is FontIcon icon) icon.Glyph = BooleanGlyph(value);
                else button.Content = CreateBooleanIcon(value);
                button.Padding = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.VerticalContentAlignment = VerticalAlignment.Center;
                continue;
            }
            bool wasBoolean = button.Content is FontIcon;
            TextBlock text = button.Content as TextBlock ?? new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            if (wasBoolean)
            {
                button.Content = text;
                button.Padding = new Thickness(5 + alignment.GetProperty("indent").GetInt32() * 12, 0, 5, 0);
                button.VerticalContentAlignment = alignment.GetProperty("v").GetString() switch { "Top" => VerticalAlignment.Top, "Bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
                text.TextWrapping = alignment.GetProperty("wrap").GetBoolean() ? TextWrapping.Wrap : TextWrapping.NoWrap;
            }
            if (text.Text != value) text.Text = value;
            if (updateFontStyles || wasBoolean)
            {
                JsonElement font = fullStyle.GetProperty("font");
                string family = font.GetProperty("name").GetString() ?? "Segoe UI";
                if (text.FontFamily.Source != family) text.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(family);
                double size = font.GetProperty("size").GetDouble() * 96 / 72;
                if (text.FontSize != size) text.FontSize = size;
                Windows.UI.Text.FontWeight weight = font.GetProperty("bold").GetBoolean() ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                Windows.UI.Text.FontStyle fontStyle = font.GetProperty("italic").GetBoolean() ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
                Windows.UI.Text.TextDecorations decoration = font.GetProperty("underline").GetString() != "None" ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
                if (text.FontWeight.Weight != weight.Weight) text.FontWeight = weight;
                if (text.FontStyle != fontStyle) text.FontStyle = fontStyle;
                if (text.TextDecorations != decoration) text.TextDecorations = decoration;
            }
            HorizontalAlignment horizontal = alignment.GetProperty("h").GetString() switch
            {
                "Center" or "CenterAcross" => HorizontalAlignment.Center,
                "Right" => HorizontalAlignment.Right,
                "General" when cell.GetProperty("numeric").GetBoolean() => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            };
            if (text.HorizontalAlignment != horizontal) text.HorizontalAlignment = horizontal;
            if (button.HorizontalContentAlignment != horizontal) button.HorizontalContentAlignment = horizontal;
        }
    }

    void UpdateActiveFont(JsonElement rows, JsonElement styles)
    {
        (int row, int col) = ParseCell(activeCell);
        int rowIndex = row - firstRow, colIndex = col - firstColumn;
        if (rowIndex < 0 || rowIndex >= viewRows || colIndex < 0 || colIndex >= viewColumns) return;
        JsonElement font = styles.GetProperty(rows[rowIndex][colIndex].GetProperty("style").GetRawText()).GetProperty("font");
        updatingFont = true;
        try
        {
            FontNameInput.Text = font.GetProperty("name").GetString() ?? "";
            FontSizeInput.Text = font.GetProperty("size").GetDouble().ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally { updatingFont = false; }
    }
}
