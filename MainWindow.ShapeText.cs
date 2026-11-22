using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Chlorella;

public sealed partial class MainWindow
{
    async void ShapeText_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (selectedShape is not uint id) throw new InvalidOperationException("図形を選択してください。");
        JsonElement shape = await SendAsync("sheet.shape", new { id });
        if (shape.GetProperty("kind").GetString() == "Line") throw new InvalidOperationException("線にはテキストを設定できません。");
        await EditShapeTextAsync(id, shape);
    });

    async Task EditShapeTextAsync(uint id, JsonElement shape)
    {
        JsonElement format = shape.GetProperty("format");
        TextBox text = new() { Header = "テキスト", Text = shape.GetProperty("text").GetString() ?? "",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 300, MinHeight = 100 };
        CheckBox bold = new() { Content = "太字", IsChecked = format.GetProperty("bold").GetBoolean() };
        CheckBox italic = new() { Content = "斜体", IsChecked = format.GetProperty("italic").GetBoolean() };
        CheckBox underline = new() { Content = "下線", IsChecked = format.GetProperty("underline").GetBoolean() };
        StackPanel styles = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        styles.Children.Add(bold); styles.Children.Add(italic); styles.Children.Add(underline);
        NumberBox size = new() { Header = "文字サイズ", Minimum = 4, Maximum = 400,
            Value = format.GetProperty("size").GetDouble(), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        uint rgb = format.GetProperty("text_color").GetProperty("Rgb").GetUInt32();
        TextBox color = new() { Header = "文字色（#RRGGBB）", Text = $"#{rgb:X6}" };
        TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(text); panel.Children.Add(styles); panel.Children.Add(size); panel.Children.Add(color); panel.Children.Add(error);
        ScrollViewer scroll = new() { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ContentDialog dialog = new() { XamlRoot = RootGrid.XamlRoot, Title = "図形のテキスト", Content = scroll,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string value = color.Text.Trim();
            if (double.IsFinite(size.Value) && value.Length == 7 && value[0] == '#' &&
                uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint _)) return;
            error.Text = "文字サイズと文字色（#RRGGBB）を確認してください。";
            args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await CommitInputAsync();
        await ExecuteAsync("shape.setText", new { id, text = text.Text, bold = bold.IsChecked == true,
            italic = italic.IsChecked == true, underline = underline.IsChecked == true, size = size.Value, color = color.Text.Trim() });
        dirty = true;
        await RefreshAsync();
        FocusShape(id);
    }
}
