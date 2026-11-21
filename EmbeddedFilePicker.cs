using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Chlorella;

sealed record PickerFileType(string Name, string[] Extensions)
{
    public override string ToString() => $"{Name} ({string.Join(", ", Extensions.Select(ext => "*" + ext))})";
}

/// <summary>An in-window file browser shared by open, save, export, and picture insertion.</summary>
sealed class EmbeddedFilePicker
{
    sealed record Entry(string Path, string Name, bool IsDirectory);
    static string? lastDirectory;
    readonly bool save;
    readonly string primaryAction;
    readonly TextBox address = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox search = new() { PlaceholderText = "検索" };
    readonly TextBox name = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox fileType = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ListView items = new()
    {
        IsItemClickEnabled = false,
        SelectionMode = ListViewSelectionMode.Single,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button back = IconButton("\uE72B", "戻る");
    readonly Button forward = IconButton("\uE72A", "進む");
    readonly Button up = IconButton("\uE74A", "上の階層");
    readonly StackPanel shortcuts = new() { Spacing = 3, Width = 160 };
    readonly StackPanel newFolderPanel = new() { Orientation = Orientation.Horizontal, Spacing = 6, Visibility = Visibility.Collapsed };
    readonly TextBox newFolderName = new() { PlaceholderText = "フォルダー名", Width = 230 };
    readonly List<string> history = [];
    List<Entry> entries = [];
    int historyIndex = -1;
    int loadVersion;
    string directory = "";
    string? confirmedOverwrite;
    string? chosen;
    readonly Grid layout;
    Action? closePage;
    const string Computer = "::computer";

    EmbeddedFilePicker(XamlRoot root, bool save, IReadOnlyList<PickerFileType> types, string suggestedName, string primaryAction)
    {
        this.save = save;
        this.primaryAction = primaryAction;
        AutomationProperties.SetName(address, "フォルダーのパス");
        AutomationProperties.SetName(search, "現在のフォルダーを検索");
        AutomationProperties.SetName(name, "ファイル名");
        AutomationProperties.SetName(fileType, "ファイルの種類");
        AutomationProperties.SetName(items, "フォルダーとファイル");
        AutomationProperties.SetName(back, "戻る");
        AutomationProperties.SetName(forward, "進む");
        AutomationProperties.SetName(up, "上のフォルダー");
        AutomationProperties.SetName(newFolderName, "新しいフォルダー名");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        foreach (PickerFileType type in types) fileType.Items.Add(type);
        fileType.SelectedIndex = 0;
        name.Text = suggestedName;
        layout = new()
        {
            Width = Math.Max(420, Math.Min(1000, root.Size.Width - 80)),
            Height = Math.Max(300, Math.Min(620,
            root.Size.Height - 170)),
            RowSpacing = 8,
            ColumnSpacing = 8
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid toolbar = new() { ColumnSpacing = 5 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        toolbar.Children.Add(back);
        Grid.SetColumn(forward, 1);
        toolbar.Children.Add(forward);
        Grid.SetColumn(up, 2);
        toolbar.Children.Add(up);
        Grid.SetColumn(address, 3);
        toolbar.Children.Add(address);
        Grid.SetColumn(search, 4);
        toolbar.Children.Add(search);
        layout.Children.Add(toolbar);
        Button newFolder = IconButton("\uE8F4", "新しいフォルダー");
        Button refresh = IconButton("\uE72C", "再読み込み");
        refresh.Click += async (_, _) => await NavigateAsync(directory, false);
        Button createFolder = new() { Content = "作成" };
        newFolderPanel.Children.Add(newFolderName);
        newFolderPanel.Children.Add(createFolder);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(newFolder);
        actions.Children.Add(refresh);
        actions.Children.Add(newFolderPanel);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        Grid browser = new() { ColumnSpacing = 8 };
        browser.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ScrollViewer places = new() { Content = shortcuts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        browser.Children.Add(places);
        Grid.SetColumn(items, 1);
        browser.Children.Add(items);
        Grid.SetRow(browser, 2);
        layout.Children.Add(browser);
        Grid.SetRow(notice, 3);
        layout.Children.Add(notice);
        Grid fields = new() { RowSpacing = 6, ColumnSpacing = 8 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.Children.Add(new TextBlock { Text = "ファイル名", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(name, 1);
        fields.Children.Add(name);
        TextBlock typeLabel = new() { Text = "ファイルの種類", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(typeLabel, 1);
        fields.Children.Add(typeLabel);
        Grid.SetRow(fileType, 1);
        Grid.SetColumn(fileType, 1);
        fields.Children.Add(fileType);
        Grid.SetRow(fields, 4);
        layout.Children.Add(fields);
        AddShortcut("ホーム", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "\uE80F");
        AddShortcut("デスクトップ", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE7F4");
        AddShortcut("PC", Computer, "\uE770");
        AddShortcut("ドキュメント", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5");
        AddShortcut("ピクチャ", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uEB9F");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) AddShortcut("ダウンロード", Path.Combine(profile, "Downloads"), "\uE896");
        foreach (DriveInfo drive in DriveInfo.GetDrives()) AddShortcut(drive.Name, drive.Name, "\uEDA2");
        back.Click += async (_, _) =>
        {
            if (historyIndex > 0) await NavigateAsync(history[--historyIndex], false);
        }
;
        forward.Click += async (_, _) =>
        {
            if (historyIndex + 1 < history.Count) await NavigateAsync(history[++historyIndex], false);
        }
;
        up.Click += async (_, _) =>
        {
            string? parent = ParentLocation(directory);
            if (parent is not null) await NavigateAsync(parent, true);
        }
;
        address.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateAsync(address.Text, true);
            }

        }
;
        search.TextChanged += (_, _) => ShowEntries();
        fileType.SelectionChanged += (_, _) =>
        {
            confirmedOverwrite = null;
            ShowEntries();
        }
;
        name.TextChanged += (_, _) =>
        {
            confirmedOverwrite = null;
            notice.Text = "";
        }
;
        name.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            if (TryChoose()) closePage?.Invoke();
        }), true);
        items.SelectionChanged += (_, _) =>
        {
            if (items.SelectedItem is ListViewItem { Tag: Entry entry } && !entry.IsDirectory) name.Text = entry.Name;
        }
;
        items.DoubleTapped += async (_, _) => await ActivateSelectionAsync();
        items.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await ActivateSelectionAsync();
            }

        }
;
        newFolder.Click += (_, _) =>
        {
            newFolderPanel.Visibility = Visibility.Visible;
            newFolderName.Focus(FocusState.Programmatic);
        }
;
        createFolder.Click += async (_, _) => await CreateFolderAsync();
        newFolderName.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await CreateFolderAsync();
            }

        }
;
    }
    static Button IconButton(string glyph, string name)
    {
        Button button = new()
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 16 },
            MinWidth = 36,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }
    static string? ParentLocation(string path)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (path == Computer) return desktop;
        if (string.Equals(path.TrimEnd('\\'), desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
        return Directory.GetParent(path)?.FullName ?? Computer;
    }

    public static async Task<string?> PickPageAsync(Grid root, string title, IReadOnlyList<PickerFileType> types,
        bool save = false, string suggestedName = "", string? actionLabel = null)
    {
        string primaryAction = actionLabel ?? (save ? "保存" : "開く");
        EmbeddedFilePicker picker = new(root.XamlRoot, save, types, suggestedName, primaryAction);
        picker.layout.ClearValue(FrameworkElement.WidthProperty);
        picker.layout.ClearValue(FrameworkElement.HeightProperty);
        Grid page = new()
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(24),
            RowSpacing = 12
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(new TextBlock { Text = title, FontSize = 24 });
        Grid.SetRow(picker.layout, 1);
        page.Children.Add(picker.layout);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button open = new() { Content = primaryAction, Style = (Style)root.Resources["PickerPrimaryButtonStyle"] },
            cancel = new() { Content = "キャンセル" };
        actions.Children.Add(open);
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 2);
        page.Children.Add(actions);
        TaskCompletionSource<string?> completion = new();
        Control? previousFocus = FocusManager.GetFocusedElement(root.XamlRoot) as Control;
        (FrameworkElement Child, Visibility Visibility)[] hidden = root.Children.OfType<FrameworkElement>()
            .Select(child => (Child: child, Visibility: child.Visibility)).ToArray();
        foreach ((FrameworkElement child, Visibility _) in hidden)
        {
            child.Visibility = Visibility.Collapsed;
        }

        bool closed = false;
        void Close()
        {
            if (closed) return;
            closed = true;
            root.Children.Remove(page);
            foreach ((FrameworkElement child, Visibility visibility) in hidden)
            {
                child.Visibility = visibility;
            }
            previousFocus?.Focus(FocusState.Programmatic);
            completion.TrySetResult(picker.chosen);
        }
        picker.closePage = Close;
        open.Click += (_, _) =>
        {
            if (picker.TryChoose()) Close();
        }
;
        cancel.Click += (_, _) => Close();
        page.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Close();
            }

        }
;
        Grid.SetRowSpan(page, root.RowDefinitions.Count);
        Canvas.SetZIndex(page, 100);
        root.Children.Add(page);
        string start = lastDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(start)) start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await picker.NavigateAsync(start, true);
        picker.items.Focus(FocusState.Programmatic);
        return await completion.Task;
    }

    void AddShortcut(string label, string path, string glyph)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = label });
        Button button = new()
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        button.Click += async (_, _) => await NavigateAsync(path, true);
        shortcuts.Children.Add(button);
    }

    async Task NavigateAsync(string path, bool addHistory)
    {
        int version = ++loadVersion;
        try
        {
            string full = path == Computer ? Computer : Path.GetFullPath(path);
            Entry[] found = full == Computer ? DriveInfo.GetDrives().Select(d => new Entry(d.Name, d.Name,
                true)).ToArray() : await Task.Run(() =>
            {
                EnumerationOptions options = new()
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                };
                return new DirectoryInfo(full).EnumerateFileSystemInfos("*", options)
                    .Select(info => new Entry(info.FullName, info.Name, (info.Attributes & FileAttributes.Directory) != 0))
                    .OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name,
                        StringComparer.CurrentCultureIgnoreCase).ToArray();
            });
            if (version != loadVersion) return;
            directory = full;
            address.Text = full == Computer ? "PC" : full;
            if (full != Computer) lastDirectory = full;
            entries = [.. found];
            confirmedOverwrite = null;
            notice.Text = "";
            if (addHistory && (historyIndex < 0 || !string.Equals(history[historyIndex], full, StringComparison.OrdinalIgnoreCase)))
            {
                if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(full);
                historyIndex = history.Count - 1;
            }
            back.IsEnabled = historyIndex > 0;
            forward.IsEnabled = historyIndex + 1 < history.Count;
            up.IsEnabled = ParentLocation(full) is not null;
            ShowEntries();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (version == loadVersion)
            {
                notice.Text = $"フォルダーを開けません: {ex.Message}";
                address.Text = directory;
            }
        }
    }

    void ShowEntries()
    {
        if (fileType.SelectedItem is not PickerFileType selected) return;
        string query = search.Text.Trim();
        items.Items.Clear();
        foreach (Entry entry in entries)
        {
            if (query.Length > 0 && !entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            if (!entry.IsDirectory && !selected.Extensions.Contains(Path.GetExtension(entry.Name),
                StringComparer.OrdinalIgnoreCase)) continue;
            FontIcon icon = new() { Glyph = entry.IsDirectory ? "\uE8B7" : "\uE8A5", FontSize = 16 };
            StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = entry.Name });
            ListViewItem item = new() { Content = content, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, $"{entry.Name}、{(entry.IsDirectory ? "フォルダー" : "ファイル")}");
            items.Items.Add(item);
        }
    }

    async Task ActivateSelectionAsync()
    {
        if (items.SelectedItem is not ListViewItem { Tag: Entry entry }) return;
        if (entry.IsDirectory) await NavigateAsync(entry.Path, true);
        else
        {
            name.Text = entry.Name;
            if (!save)
            {
                chosen = entry.Path;
                closePage?.Invoke();
            }

        }
    }

    async Task CreateFolderAsync()
    {
        try
        {
            string value = newFolderName.Text.Trim();
            if (string.IsNullOrWhiteSpace(value) || Path.GetFileName(value) != value ||
                value is "." or "..") throw new ArgumentException("フォルダー名を入力してください。");
            string path = Path.Combine(directory, value);
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("同じ名前のファイルまたはフォルダーがあります。");
            Directory.CreateDirectory(path);
            newFolderName.Text = "";
            newFolderPanel.Visibility = Visibility.Collapsed;
            await NavigateAsync(directory, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            notice.Text = $"作成できません: {ex.Message}";
        }
    }

    bool TryChoose()
    {
        try
        {
            string value = name.Text.Trim();
            if (directory == Computer)
            {
                notice.Text = "ドライブを選択してください。";
                return false;
            }
            if (value.Length == 0)
            {
                notice.Text = "ファイル名を入力してください。";
                name.Focus(FocusState.Programmatic);
                return false;
            }
            string path = Path.GetFullPath(Path.Combine(directory, value));
            if (Directory.Exists(path))
            {
                _ = NavigateAsync(path, true);
                return false;
            }
            if (fileType.SelectedItem is not PickerFileType selected) return false;
            if (save && Path.GetExtension(path).Length == 0) path += selected.Extensions[0];
            if (!selected.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                notice.Text = "選択したファイルの種類に合う拡張子を指定してください。";
                return false;
            }
            if (!Directory.Exists(Path.GetDirectoryName(path)))
            {
                notice.Text = "保存先のフォルダーがありません。";
                return false;
            }
            if (!save && !File.Exists(path))
            {
                notice.Text = "ファイルが見つかりません。";
                return false;
            }
            if (save && File.Exists(path) && !string.Equals(confirmedOverwrite, path, StringComparison.OrdinalIgnoreCase))
            {
                confirmedOverwrite = path;
                notice.Text = $"同じ名前のファイルがあります。上書きする場合は、もう一度「{primaryAction}」を押してください。";
                return false;
            }
            chosen = path;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            notice.Text = $"パスを使用できません: {ex.Message}";
            return false;
        }
    }
}
