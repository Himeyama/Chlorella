using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Chlorella;

public sealed partial class MainWindow
{
    void AttachLazyCellMenu(SheetCellButton cell, JsonElement tables, int row, int col, string address)
    {
        cell.ContextRequested += (_, args) =>
        {
            if (cell.ContextFlyout is not MenuFlyout)
            {
                AttachTableMenu(cell, tables, row, col, address);
                AttachCellEditMenu(cell, address);
            }
            if (cell.ContextFlyout is MenuFlyout menu)
            {
                args.Handled = true;
                if (args.TryGetPosition(cell, out Windows.Foundation.Point point))
                    menu.ShowAt(cell, point);
                else menu.ShowAt(cell);
            }
        };
    }

    void AttachCellEditMenu(SheetCellButton cell, string address)
    {
        MenuFlyout menu = cell.ContextFlyout as MenuFlyout ?? new MenuFlyout();
        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        AddCellShiftMenu(menu, address, "挿入", true);
        AddCellShiftMenu(menu, address, "削除", false);
        cell.ContextFlyout = menu;
    }

    void AddCellShiftMenu(MenuFlyout menu, string address, string title, bool insert)
    {
        MenuFlyoutSubItem submenu = new() { Text = title };
        foreach (bool vertical in new[] { true, false })
        {
            MenuFlyoutItem item = new()
            {
                Text = insert ? (vertical ? "セルを下にシフト" : "セルを右にシフト")
                    : (vertical ? "セルを上にシフト" : "セルを左にシフト")
            };
            item.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                (int row, int col) = ParseCell(address);
                bool selected = row - 1 >= selectionBounds.Top && row - 1 <= selectionBounds.Bottom &&
                    col >= selectionBounds.Left && col <= selectionBounds.Right;
                string range = selected ? currentSelection : address;
                await ExecuteAsync("edit.shiftCells", new { range, insert, vertical });
                dirty = true;
                await RefreshAsync();
                selectionAnchor = activeCell;
                selectionLead = activeCell;
                FocusCell(activeCell);
            });
            submenu.Items.Add(item);
        }
        menu.Items.Add(submenu);
    }
}
