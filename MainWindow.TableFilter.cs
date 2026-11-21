using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Chlorella;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class TableFilterChoice : INotifyPropertyChanged
{
    bool selected;
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            OnPropertyChanged();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed partial class MainWindow
{
    async Task<int> MoveVisibleRowAsync(int row, int direction, int count)
    {
        if (dataSource is not WorksheetEngine) return Math.Clamp(row + direction * count, 1, 1_048_576);
        JsonElement result = await SendAsync("sheet.nextVisibleRow", new { row, direction, count });
        return result.GetProperty("row").GetInt32();
    }

    async Task ShowTableFilterAsync(string address)
    {
        await CommitInputAsync();
        JsonElement result = await ExecuteAsync("table.filterChoices", new { cell = address });
        List<TableFilterChoice> values = result.GetProperty("values").EnumerateArray().Select(item =>
        {
            string value = item.GetProperty("value").GetString() ?? "", label = item.GetProperty("label").GetString() ?? "";
            int count = item.GetProperty("count").GetInt32();
            return new TableFilterChoice
            {
                Value = value,
                Label = $"{(label.Length == 0 ? "(空白)" : label)} ({count})",
                Selected = item.GetProperty("selected").GetBoolean()
            };
        }).ToList();
        DataTemplate template =
            (DataTemplate)XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                    " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                    "<CheckBox Content='{Binding Label}'" +
                    " IsChecked='{Binding Selected, Mode=TwoWay}'" +
                    " HorizontalAlignment='Stretch'/></DataTemplate>");
        ListView list = new()
        {
            ItemsSource = values,
            ItemTemplate = template,
            SelectionMode = ListViewSelectionMode.None,
            Height = 300,
            MinWidth = 280
        };
        Button selectAll = new() { Content = "すべて選択" }, clearAll = new() { Content = "すべて解除" };
        selectAll.Click += (_, _) =>
        {
            foreach (TableFilterChoice choice in values) choice.Selected = true;
        }
;
        clearAll.Click += (_, _) =>
        {
            foreach (TableFilterChoice choice in values) choice.Selected = false;
        }
;
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(selectAll);
        buttons.Children.Add(clearAll);
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = $"{address} のフィルター",
            Content = panel,
            PrimaryButtonText = "適用",
            SecondaryButtonText = "フィルター解除",
            CloseButtonText = "キャンセル"
        };
        ContentDialogResult decision = await dialog.ShowAsync();
        if (decision == ContentDialogResult.None) return;
        string[]? selected = decision == ContentDialogResult.Secondary ||
            values.All(choice => choice.Selected) ? null : values.Where(choice => choice.Selected).Select(choice => choice.Value).ToArray();
        await ExecuteAsync("table.filter", new { cell = address, values = selected });
        if (selected is not null && result.TryGetProperty("startRow",
            out JsonElement start) && result.TryGetProperty("endRow", out JsonElement end))
        {
            int header = start.GetInt32(), next = await MoveVisibleRowAsync(header, 1, 1);
            if (next <= end.GetInt32() && next >= firstRow + PageRows)
            {
                (_, int col) = ParseCell(address);
                string target = CellAddress(next, col);
                await ExecuteAsync("selection.set", new { range = target, active = target });
                firstRow = next;
            }
        }
        dirty = true;
        await RefreshAsync();
        selectionAnchor = activeCell;
        selectionLead = activeCell;
        FocusCell(activeCell);
    }
}
