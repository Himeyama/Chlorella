using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    async Task RefreshSheetsAsync()
    {
        JsonElement info = await ExecuteAsync("document.inspect", new { });
        RenameFileButton.IsEnabled = info.GetProperty("path").ValueKind == JsonValueKind.String;
        CopyPathButton.IsEnabled = RenameFileButton.IsEnabled;
        int active = info.GetProperty("activeSheet").GetInt32();
        bool canDeleteSheet = info.GetProperty("sheets").GetArrayLength() > 1;
        string state = JsonSerializer.Serialize(new
        {
            active,
            sheets = info.GetProperty("sheets").EnumerateArray()
            .Select(sheet => new
            {
                name = sheet.GetProperty("name").GetString(),
                visibility = sheet.GetProperty("visibility").GetString(),
                color = sheet.GetProperty("tabColor").GetString(),
                isProtected = sheet.GetProperty("protected").GetBoolean()
            }).ToArray()
        });
        if (state == sheetTabsState) return;
        updatingSheets = true;
        SheetTabs.Items.Clear();
        int index = 0;
        foreach (JsonElement sheet in info.GetProperty("sheets").EnumerateArray())
        {
            int sheetIndex = index++;
            if (sheet.GetProperty("visibility").GetString() != "Visible") continue;
            string name = sheet.GetProperty("name").GetString() ?? "";
            SelectorBarItem tab = new()
            {
                Text = name,
                Tag = sheetIndex,
                CanDrag = true,
                AllowDrop = true,
                Height = 32,
                MinHeight = 0,
                Padding = new Thickness(10, 2, 10, 3)
            };
            string? tabColor = sheet.GetProperty("tabColor").GetString();
            if (tabColor is not null && tabColor.Length == 7 && uint.TryParse(tabColor[1..],
                System.Globalization.NumberStyles.HexNumber, null, out uint tabRgb))
            {
                tab.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,
                    (byte)(tabRgb >> 16), (byte)(tabRgb >> 8), (byte)tabRgb));
                tab.BorderThickness = new Thickness(0, 0, 0, 3);
            }
            bool protectedSheet = sheet.GetProperty("protected").GetBoolean();
            AutomationProperties.SetName(tab, $"{name}{(sheetIndex == active ? "、表示中のシート" : "")}{(protectedSheet ? "、保護中" : "")}");
            MenuFlyout menu = new();
            MenuFlyoutItem duplicate = new() { Text = "複製", Icon = new FontIcon { Glyph = "\uE8C8" } };
            duplicate.Click += async (_, _) => await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ExecuteAsync("sheet.move", new { sheet = name, to = sheetIndex + 1, copy = true });
                dirty = true;
                await RefreshAsync();
                SetStatus("シートを複製しました。テーブルや一部のファイル固有の要素は複製されません。");
            });
            MenuFlyoutItem rename = new() { Text = "名前の変更", Icon = new FontIcon { Glyph = "\uE8AC" } };
            rename.Click += async (_, _) => await RenameSheetAsync(name);
            MenuFlyoutItem delete = new() { Text = "削除", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += async (_, _) => await DeleteSheetAsync(name);
            MenuFlyoutItem protection = new() { Text = protectedSheet ? "シートの保護を解除" : "シートを保護", Icon = new FontIcon { Glyph = "\uE72E" } };
            protection.Click += async (_, _) => await ChangeSheetProtectionAsync(name, protectedSheet);
            MenuFlyoutItem color = new() { Text = "シート見出しの色…", Icon = new FontIcon { Glyph = "\uE790" } };
            color.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowColorPalette(tab, $"tab:{name}"));
            menu.Items.Add(duplicate);
            menu.Items.Add(rename);
            if (canDeleteSheet) menu.Items.Add(delete);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(protection);
            menu.Items.Add(color);
            tab.ContextFlyout = menu;
            tab.DragStarting += Sheet_DragStarting;
            tab.DragOver += Sheet_DragOver;
            tab.Drop += Sheet_Drop;
            tab.DropCompleted += Sheet_DropCompleted;
            SheetTabs.Items.Add(tab);
            if (sheetIndex == active) SheetTabs.SelectedItem = tab;
        }
        updatingSheets = false;
        sheetTabsState = state;
    }
    async void SheetTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        if (!updatingSheets && draggedSheet is null && SheetTabs.SelectedItem is SelectorBarItem { Tag: int index })
            await RunAsync(async () =>
            {
                await CommitInputAsync();
                await ExecuteAsync("sheet.activate", new { sheet = index });
                firstRow = 1;
                sheetTabsState = null;
                foreach (SelectorBarItem tab in SheetTabs.Items.OfType<SelectorBarItem>())
                {
                    string label = AutomationProperties.GetName(tab).Replace("、表示中のシート", "");
                    if (tab.Tag is int tabIndex && tabIndex == index) label += "、表示中のシート";
                    AutomationProperties.SetName(tab, label);
                }
                await RefreshAsync(refreshSheets: false);
                selectionAnchor = activeCell;
                selectionLead = activeCell;
                FocusCell(activeCell);
            });
    }
    void Sheet_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not SelectorBarItem { Tag: int index } item) return;
        draggedSheet = item.Text;
        draggedSheetIndex = index;
        args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        args.Data.SetText(item.Text);
    }
    void Sheet_DragOver(object sender, DragEventArgs e)
    {
        if (draggedSheet is not null) e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }
    async void Sheet_Drop(object sender, DragEventArgs e)
    {
        if (draggedSheet is not string name || sender is not SelectorBarItem { Tag: int index } targetItem) return;
        int insertion = index + (e.GetPosition(targetItem).X >= targetItem.ActualWidth / 2 ? 1 : 0);
        int target = draggedSheetIndex < insertion ? insertion - 1 : insertion;
        draggedSheet = null;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            if (target != draggedSheetIndex)
            {
                await ExecuteAsync("sheet.move", new { sheet = name, to = target });
                dirty = true;
            }
            await RefreshAsync();
        });
    }
    void Sheet_DropCompleted(UIElement sender, DropCompletedEventArgs e)
    {
        draggedSheet = null;
    }
    async Task ChangeSheetProtectionAsync(string name, bool isProtected)
    {
        PasswordBox password = new() { PlaceholderText = "パスワード（任意）" };
        AutomationProperties.SetName(password, "パスワード（任意）");
        ContentDialog dialog = new()
        {
            Title = isProtected ? "シートの保護を解除" : "シートを保護",
            Content = password,
            PrimaryButtonText = isProtected ? "解除" : "保護",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync(isProtected ? "review.unprotectSheet" : "review.protectSheet", new
            {
                sheet = name,
                password = password.Password
            });
            dirty = true;
            await RefreshAsync();
        });
    }
    async Task RenameSheetAsync(string name)
    {
        TextBox input = new() { Text = name };
        ContentDialog dialog = new()
        {
            Title = "シート名の変更",
            Content = input,
            PrimaryButtonText = "変更",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("sheet.rename", new { sheet = name, name = input.Text });
            dirty = true;
            await RefreshAsync();
        });
    }
    async Task DeleteSheetAsync(string name)
    {
        ContentDialog dialog = new()
        {
            Title = "シートを削除しますか？",
            Content = $"「{name}」を削除します。",
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("home.deleteSheet", new { sheet = name });
            dirty = true;
            await RefreshAsync();
        });
    }
    async void AddSheet_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("home.insertSheet", new { });
            dirty = true;
            firstRow = 1;
            await RefreshAsync();
            selectionAnchor = activeCell;
            selectionLead = activeCell;
            FocusCell(activeCell);
        });
}
