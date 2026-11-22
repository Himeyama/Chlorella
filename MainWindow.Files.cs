using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace Chlorella;

public sealed partial class MainWindow
{
    async void InsertTable_Click(object sender, RoutedEventArgs e)
    {
        TextBox range = new() { Text = currentSelection };
        CheckBox header = new() { Content = "先頭行を見出しとして使う", IsChecked = true };
        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "テーブルの範囲" });
        content.Children.Add(range);
        content.Children.Add(header);
        ContentDialog dialog = new()
        {
            Title = "テーブルを挿入",
            Content = content,
            PrimaryButtonText = "挿入",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("insert.table", new { range = range.Text, header = header.IsChecked == true });
            dirty = true;
            await RefreshAsync();
            FocusCell(activeCell);
            SetStatus($"テーブル {range.Text} を作成しました。");
        });
    }
    async void InsertPicture_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        string? path = await EmbeddedFilePicker.PickPageAsync(RootGrid, "画像を挿入", [new("画像", [".png", ".jpg", ".jpeg"])]);
        if (path is null) return;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidOperationException("表示できる画像は 16 MiB 以下です。");
        await CommitInputAsync();
        await ExecuteAsync("insert.picture", new { path, at = activeCell });
        dirty = true;
        await RefreshAsync();
        FocusCell(activeCell);
    });
    async void InsertShape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string kind }) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("insert.shape", new { kind, at = activeCell });
            dirty = true;
            await RefreshAsync();
            FocusCell(activeCell);
        });
    }
    async void New_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        string path = Environment.ProcessPath ?? throw new InvalidOperationException("新しいウィンドウを開けません。");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    });
    async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(SaveAsync);
    async Task SaveAsync()
    {
        await CommitInputAsync();
        JsonElement info = await ExecuteAsync("document.inspect", new { });
        if (info.GetProperty("path").ValueKind != JsonValueKind.String)
        {
            await SaveAsAsync();
            return;
        }
        JsonElement saved = await ExecuteAsync("file.save", new { });
        dirty = false;
        await ShowSaveResultAsync(saved);
    }
    async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        string? path = await EmbeddedFilePicker.PickPageAsync(RootGrid, "エクスポート",
            [new("CSV", [".csv"]), new("HTML", [".html"]), new("PDF", [".pdf"])], save: true, suggestedName: "エクスポート",
                actionLabel: "エクスポート");
        if (path is null) return;
        await CommitInputAsync();
        string command = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => "file.exportCsv",
            ".html" => "file.exportHtml",
            ".pdf" => "file.exportPdf",
            _ => throw new InvalidOperationException("エクスポート形式を選択してください。")
        };
        await ExecuteAsync(command, new { path });
        SetStatus("エクスポートしました。");
    });
    async void RenameFile_Click(object sender, RoutedEventArgs e) => await RunAsync(RenameFileAsync);
    async Task RenameFileAsync()
    {
        await CommitInputAsync();
        JsonElement info = await ExecuteAsync("document.inspect", new { });
        if (info.GetProperty("path").GetString() is not string path) throw new InvalidOperationException("名前を変更するには先にファイルを保存してください。");
        TextBox input = new() { Text = Path.GetFileName(path) };
        AutomationProperties.SetName(input, "ファイル名");
        ContentDialog dialog = new()
        {
            Title = "ファイル名の変更",
            Content = input,
            PrimaryButtonText = "変更",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await SendAsync("file.rename", new { name = input.Text.Trim() });
        savedPath = Path.Combine(Path.GetDirectoryName(path) ?? "", input.Text.Trim());
        displayFileName = Path.GetFileName(savedPath);
        UpdateFileName();
        SetStatus("ファイル名を変更しました。");
    }
    async void CopyPath_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        JsonElement info = await ExecuteAsync("document.inspect", new { });
        if (info.GetProperty("path").GetString() is not string path) throw new InvalidOperationException("パスをコピーするには先にファイルを保存してください。");
        DataPackage package = new();
        package.SetText(path);
        Clipboard.SetContent(package);
        SetStatus("パスをコピーしました。");
    });
    async void Open_Click(object sender, RoutedEventArgs e) => await RunAsync(OpenAsync);
    async Task OpenAsync()
    {
            if (dirty && !await ConfirmDiscardAsync()) return;
            if (filePageActive) return;
            filePageActive = true;
            string? path;
            try
            {
                path = await EmbeddedFilePicker.PickPageAsync(RootGrid, "開く", [new("Excel ブック", [".xlsx"])]);
            }
            finally
            {
                filePageActive = false;
                if (dirty) ScheduleAutoSave();
            }
            if (path is null) return;
            JsonElement result = await ExecuteAsync("file.open", new { path });
            bool external = result.TryGetProperty("imported", out JsonElement imported) && imported.ValueKind == JsonValueKind.True;
            savedPath = external ? null : path;
            displayFileName = Path.GetFileName(path);
            if (external && AutoSaveToggle.IsOn) AutoSaveToggle.IsOn = false;
            objectClipboard = null;
            objectClipboardToken = null;
            copiedCells = null;
            selectedShape = null;
            selectedImage = null;
            ShapeTab.Visibility = Visibility.Collapsed;
            if (RibbonSelector.SelectedItem == ShapeTab) RibbonSelector.SelectedItem = RibbonSelector.Items[1];
            dirty = false;
            firstRow = 1;
            await RefreshAsync();
            if (external)
            {
                int tables = result.TryGetProperty("importedTables", out JsonElement importedTables) ? importedTables.GetInt32() : 0;
                int skipped = result.TryGetProperty("skippedTables", out JsonElement skippedTables) ? skippedTables.GetInt32() : 0;
                int drawings = result.TryGetProperty("importedDrawings",
                    out JsonElement importedDrawings) ? importedDrawings.GetInt32() : 0;
                int skippedObjects = result.TryGetProperty("skippedDrawings",
                    out JsonElement skippedDrawings) ? skippedDrawings.GetInt32() : 0;
                int skippedConditions = result.TryGetProperty("skippedFilters",
                    out JsonElement skippedFilters) ? skippedFilters.GetInt32() : 0;
                bool modified = result.TryGetProperty("modified",
                    out JsonElement modifiedValue) && modifiedValue.ValueKind == JsonValueKind.True;
                SetStatus("Excel ブックを読み込みました。保存するときは別の名前を指定してください。");
                if (!modified && skipped == 0 && skippedConditions == 0 && skippedObjects == 0) return;
                string origin = modified ? "このブックは別のアプリで変更されています。Chlorella 専用の追加データは使用せず、標準形式から読み込みます。" : "";
                ContentDialog notice = new()
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "Excel ブックの読み込みについて",
                    Content = $"{origin}セルの値、数式、結合、テーブル {tables} 件、図形・画像 {drawings} 件を読み込みました。" +
                        $"読み込めないテーブルは {skipped} 件、フィルター条件は {skippedConditions} 件、図形・画像などは {skippedObjects} 件です。" +
                        $"セルの書式など一部の情報は読み込まれていません。元のファイルは変更せず、別の名前で保存してください。",
                    CloseButtonText = "閉じる"
                };
                await notice.ShowAsync();
            }
    }
    async void SaveAs_Click(object sender, RoutedEventArgs e) => await RunAsync(SaveAsAsync);
    async Task SaveAsAsync()
    {
        if (filePageActive) return;
        filePageActive = true;
        string? path;
        try
        {
            path = await EmbeddedFilePicker.PickPageAsync(RootGrid, "名前を付けて保存", [new("Excel ブック", [".xlsx"])],
                save: true, suggestedName: Path.GetFileNameWithoutExtension(displayFileName));
        }
        finally
        {
            filePageActive = false;
            if (dirty) ScheduleAutoSave();
        }
        if (path is null) return;
        await CommitInputAsync();
        JsonElement saved = await ExecuteAsync("file.saveAs", new { path });
        savedPath = path;
        displayFileName = Path.GetFileName(path);
        RenameFileButton.IsEnabled = true;
        CopyPathButton.IsEnabled = true;
        dirty = false;
        await ShowSaveResultAsync(saved);
    }
    async Task ShowSaveResultAsync(JsonElement saved)
    {
        SetStatus("保存しました。");
        if (!saved.TryGetProperty("headerlessTables", out JsonElement count) || count.GetInt32() == 0) return;
        ContentDialog notice = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "テーブルの見出しについて",
            Content = "空欄・重複・数値・数式の見出しがあるテーブルは、セルの値を維持するため、他の表計算アプリでは見出しなしとして表示されます。" +
                "フィルターを設定した場合、他のアプリでは非表示行を手動で再表示する必要があります。" +
                "Chlorella では元の見出しとフィルター設定を保持します。",
            CloseButtonText = "閉じる"
        };
        await notice.ShowAsync();
    }
}
