using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Chlorella;

public sealed partial class MainWindow
{
    void AttachTableMenu(SheetCellButton cell, JsonElement tables, int row, int col, string address)
    {
        foreach (JsonElement table in tables.EnumerateArray())
        {
            JsonElement range = table.GetProperty("range"), start = range.GetProperty("start"), end = range.GetProperty("end");
            int top = start.GetProperty("row").GetInt32(), left = start.GetProperty("col").GetInt32();
            int bottom = end.GetProperty("row").GetInt32(), right = end.GetProperty("col").GetInt32();
            if (row < top || row > bottom || col < left || col > right) continue;
            string initial = $"{CellAddress(top + 1, left)}:{CellAddress(bottom + 1, right)}";
            MenuFlyout menu = new();
            MenuFlyoutItem ascending = new() { Text = "この列を昇順に並べ替え" };
            ascending.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ExecuteAsync("table.sort", new { cell = address, direction = "ascending" });
                dirty = true;
                await RefreshAsync();
            });
            MenuFlyoutItem descending = new() { Text = "この列を降順に並べ替え" };
            descending.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ExecuteAsync("table.sort", new { cell = address, direction = "descending" });
                dirty = true;
                await RefreshAsync();
            });
            MenuFlyoutItem resize = new() { Text = "テーブルの範囲を変更…" };
            resize.Click += async (_, _) =>
            {
                TextBox input = new() { Text = initial };
                ContentDialog dialog = new()
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "テーブルの範囲",
                    Content = input,
                    PrimaryButtonText = "変更",
                    CloseButtonText = "キャンセル"
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                await RunAsync(async () =>
                {
                    await CommitInputAsync();
                    await ExecuteAsync("table.resize", new { cell = address, range = input.Text });
                    dirty = true;
                    await RefreshAsync();
                });
            }
;
            MenuFlyoutItem remove = new() { Text = "テーブルを解除" };
            remove.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ExecuteAsync("table.delete", new { cell = address });
                dirty = true;
                await RefreshAsync();
            });
            menu.Items.Add(ascending);
            menu.Items.Add(descending);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(resize);
            menu.Items.Add(remove);
            cell.ContextFlyout = menu;
            return;
        }
    }
}
