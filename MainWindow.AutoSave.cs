using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Chlorella;

public sealed partial class MainWindow
{
    readonly DispatcherTimer autoSaveTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    string? savedPath;
    string displayFileName = "ブック.xlsx";
    long changeVersion;
    bool autoSaveReady;
    bool autoSaveBusy;
    bool filePageActive;

    void InitializeAutoSave()
    {
        autoSaveTimer.Tick += AutoSaveTimer_Tick;
        autoSaveReady = true;
        UpdateFileName();
    }

    void UpdateFileName()
    {
        if (FileNameText is null || UnsavedMark is null || FileNameButton is null) return;
        FileNameText.Text = displayFileName;
        bool unsaved = isDirty;
        UnsavedMark.Visibility = unsaved ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FileNameButton,
            $"{displayFileName}、{(unsaved ? "未保存の変更あり" : "未保存の変更なし")}、ファイル名を変更");
    }

    void ScheduleAutoSave()
    {
        if (!autoSaveReady || AutoSaveToggle is null || !AutoSaveToggle.IsOn || savedPath is null) return;
        if (!autoSaveTimer.IsEnabled) autoSaveTimer.Start();
    }

    async void AutoSaveTimer_Tick(object? sender, object e)
    {
        autoSaveTimer.Stop();
        if (!autoSaveReady || !AutoSaveToggle.IsOn || !dirty || savedPath is null || inputPending || filePageActive) return;
        if (autoSaveBusy)
        {
            ScheduleAutoSave();
            return;
        }
        autoSaveBusy = true;
        long version = changeVersion;
        try
        {
            JsonElement saved = await ExecuteAsync("file.save", new { });
            if (changeVersion == version) dirty = false;
            else ScheduleAutoSave();
            if (saved.TryGetProperty("headerlessTables", out JsonElement count) && count.GetInt32() > 0)
                SetStatus("自動保存しました。一部のテーブルは他のアプリで見出しなしとして表示されます。");
            else SetStatus("自動保存しました。");
        }
        catch (Exception ex)
        {
            AutoSaveToggle.IsOn = false;
            SetStatus($"自動保存できませんでした: {ex.Message}");
        }
        finally { autoSaveBusy = false; }
    }

    async void AutoSaveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!autoSaveReady) return;
        if (!AutoSaveToggle.IsOn)
        {
            autoSaveTimer.Stop();
            return;
        }
        try
        {
            if (savedPath is null) await SaveAsAsync();
            if (savedPath is null) AutoSaveToggle.IsOn = false;
            else if (dirty) ScheduleAutoSave();
        }
        catch (Exception ex)
        {
            AutoSaveToggle.IsOn = false;
            SetStatus(ex.Message);
        }
    }

    async void FileName_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (savedPath is null) await SaveAsAsync();
        else await RenameFileAsync();
    });

    async void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!filePageActive) await RunAsync(SaveAsync);
    }

    async void OpenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!filePageActive) await RunAsync(OpenAsync);
    }
}
