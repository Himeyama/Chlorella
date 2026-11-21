using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

namespace Chlorella;

public sealed partial class MainWindow
{
    void RibbonToggle_Click(object sender, RoutedEventArgs e)
    {
        ribbonCollapsed = !ribbonCollapsed;
        UpdateRibbon();
        RibbonToggleIcon.Glyph = ribbonCollapsed ? ChevronDown : ChevronUp;
        string label = ribbonCollapsed ? "リボンを展開" : "リボンを折りたたむ";
        AutomationProperties.SetName(RibbonToggle, label); ToolTipService.SetToolTip(RibbonToggle, label);
    }
    void RibbonSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e) => UpdateRibbon();
    void UpdateRibbon()
    {
        if (FileRibbon is null || HomeRibbon is null || InsertRibbon is null || ViewRibbon is null || DataRibbon is null ||
            ShapeRibbon is null || RibbonSelector is null) return;
        string? tab = RibbonSelector.SelectedItem?.Tag as string;
        FileRibbon.Visibility = !ribbonCollapsed && tab == "File" ? Visibility.Visible : Visibility.Collapsed;
        HomeRibbon.Visibility = !ribbonCollapsed && tab == "Home" ? Visibility.Visible : Visibility.Collapsed;
        InsertRibbon.Visibility = !ribbonCollapsed && tab == "Insert" ? Visibility.Visible : Visibility.Collapsed;
        ViewRibbon.Visibility = !ribbonCollapsed && tab == "View" ? Visibility.Visible : Visibility.Collapsed;
        DataRibbon.Visibility = !ribbonCollapsed && tab == "Data" ? Visibility.Visible : Visibility.Collapsed;
        ShapeRibbon.Visibility = !ribbonCollapsed && tab == "Shape" ? Visibility.Visible : Visibility.Collapsed;
    }
    async void FlashFill_Click(object sender, RoutedEventArgs e) => await FlashFillAsync();
    Task FlashFillAsync() => RunAsync(async () =>
    {
        await CommitInputAsync();
        JsonElement result = await ExecuteAsync("edit.flashFill", new { });
        dirty = true; await RefreshAsync(); FocusCell(selectionLead);
        SetStatus($"{result.GetProperty("filled").GetInt32()} セルを入力しました。");
    });
    async void Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true;
        if (sender is FrameworkElement { Tag: string command })
            await RunAsync(async () =>
            {
                await CommitInputAsync(); await ExecuteAsync(command,
                new { }); dirty = true; await RefreshAsync(); FocusCell(selectionLead);
            });
    }
    async void NumberFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingNumberFormat || sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string format } }) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync(); await ExecuteAsync("home.numberFormat",
            new { format }); dirty = true; await RefreshAsync(); FocusCell(selectionLead);
        });
    }
    async void FontName_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingFont && FontNameInput.SelectedItem is string name) await ApplyFontNameAsync(name);
    }
    async void FontName_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs e) => await ApplyFontNameAsync(e.Text);
    Task ApplyFontNameAsync(string name) => RunAsync(async () =>
    {
        name = name.Trim();
        if (name.Length == 0) throw new FormatException("書体を入力してください。");
        await CommitInputAsync(); await ExecuteAsync("home.fontName",
            new { name }); dirty = true; await RefreshAsync(); FocusCell(selectionLead);
    });
    async void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingFont && FontSizeInput.SelectedItem is string size) await ApplyFontSizeAsync(size);
    }
    async void FontSize_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs e) => await ApplyFontSizeAsync(e.Text);
    Task ApplyFontSizeAsync(string text) => RunAsync(async () =>
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture,
            out double size) || !double.IsFinite(size) || size < 1 || size > 409)
            throw new FormatException("フォントサイズには 1 から 409 までの数値を入力してください。");
        await CommitInputAsync(); await ExecuteAsync("home.fontSize",
            new { size }); dirty = true; await RefreshAsync(refreshSheets: false, updateFontStyles: true); FocusCell(selectionLead);
    });
    async void IncreaseFont_Click(object sender, RoutedEventArgs e) => await StepFontSizeAsync(true);
    async void DecreaseFont_Click(object sender, RoutedEventArgs e) => await StepFontSizeAsync(false);
    Task StepFontSizeAsync(bool increase)
    {
        if (!double.TryParse(FontSizeInput.Text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture, out double current))
            return RunAsync(() => throw new FormatException("フォントサイズを確認してください。"));
        double[] sizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 36, 48, 72];
        double target = increase ? sizes.FirstOrDefault(size => size > current, Math.Min(409, current + 1))
                                 : sizes.Reverse().FirstOrDefault(size => size < current, Math.Max(1, current - 1));
        return ApplyFontSizeAsync(target.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }
    async void Merge_Click(object sender, RoutedEventArgs e)
    {
        MergeButton.IsChecked = activeCellMerged;
        await RunAsync(async () =>
            {
                await CommitInputAsync(); await ExecuteAsync(activeCellMerged ? "home.unmergeCells" : "home.mergeCells",
                new { }); dirty = true; await RefreshAsync(); FocusCell(selectionLead);
            });
    }
    async void ColorApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string target })
            await ApplyColorAsync(target, target == "fill" ? currentFillColor : currentFontColor, null);
    }
    void ColorPalette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string target } anchor) ShowColorPalette(anchor, target);
    }
    void ShowColorPalette(FrameworkElement anchor, string target)
    {
        Flyout flyout = new();
        StackPanel panel = new() { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock { Text = "テーマの色" });
        Grid themeGrid = new() { ColumnSpacing = 2, RowSpacing = 2 };
        for (int col = 0; col < 10; col++) themeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double[] tints = [0, 0.8, 0.6, 0.4, -0.25, -0.5];
        for (int row = 0; row < tints.Length; row++)
        {
            themeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int col = 0; col < 10; col++)
            {
                int index = col;
                double tint = tints[row];
                if (paletteTheme.ValueKind != JsonValueKind.Array || index >= paletteTheme.GetArrayLength()) continue;
                JsonElement encoded = JsonSerializer.SerializeToElement(new { Theme = new[] { index, (int)(tint * 1000) } });
                if (ColorBrush(encoded, paletteTheme) is not Microsoft.UI.Xaml.Media.SolidColorBrush brush) continue;
                AddColorSwatch(themeGrid, row, col, brush.Color, $"テーマの色 {index + 1}、色調 {tint:P0}", new
                {
                    theme = index,
                    tint
                }, target, flyout);
            }
        }
        panel.Children.Add(themeGrid);
        panel.Children.Add(new TextBlock { Text = "標準の色", Margin = new Thickness(0, 6, 0, 0) });
        Grid standardGrid = new() { ColumnSpacing = 2 };
        string[] colors = ["#C00000", "#FF0000", "#F59E0B", "#FFFF00", "#84CC16", "#00A86B", "#00A6D6", "#0078C8", "#163A70", "#8040A0"];
        for (int col = 0; col < colors.Length; col++)
        {
            standardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string hex = colors[col];
            uint rgb = Convert.ToUInt32(hex[1..], 16);
            Windows.UI.Color color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            AddColorSwatch(standardGrid, 0, col, color, $"標準の色 {hex}", hex, target, flyout);
        }
        panel.Children.Add(standardGrid);
        Button clear = new()
        {
            Content =
            target.StartsWith("tab:") ? "色を解除"
            : target is "fill" or "shapeFill" ? "塗りつぶしなし"
            : target == "shapeLine" ? "輪郭なし" : "文字色を自動にする",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        clear.Click += async (_, _) => { flyout.Hide(); await ApplyColorAsync(target, target == "font" ? "auto" : "none", null); };
        panel.Children.Add(clear);
        Button custom = new() { Content = "その他の色…", HorizontalAlignment = HorizontalAlignment.Stretch };
        custom.Click += async (_, _) => { flyout.Hide(); await ShowCustomColorAsync(target); };
        panel.Children.Add(custom);
        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }
    void AddColorSwatch(Grid grid, int row, int column, Windows.UI.Color color, string label, object value, string target, Flyout flyout)
    {
        Button button = new()
        {
            Width = 26,
            Height = 26,
            Style = (Style)RootGrid.Resources["ColorSwatchButtonStyle"],
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color)
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += async (_, _) => { flyout.Hide(); await ApplyColorAsync(target, value, color); };
        Grid.SetRow(button, row); Grid.SetColumn(button, column); grid.Children.Add(button);
    }
    async Task ShowCustomColorAsync(string target)
    {
        ColorPicker picker = new() { IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false };
        string colorLabel = target.StartsWith(
            "tab:") ? "シート見出しの色"
            : target == "fill" ? "セルの塗りつぶし"
            : target == "shapeFill" ? "図形の塗りつぶし"
            : target == "shapeLine" ? "図形の輪郭" : "文字の色";
        AutomationProperties.SetName(picker, colorLabel);
        ContentDialog dialog = new()
        {
            Title = colorLabel,
            Content = picker,
            PrimaryButtonText = "適用",
            CloseButtonText = "キャンセル",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        Windows.UI.Color color = picker.Color;
        await ApplyColorAsync(target, $"#{color.R:X2}{color.G:X2}{color.B:X2}", color);
    }
    Task ApplyColorAsync(string target, object value, Windows.UI.Color? color) => RunAsync(async () =>
    {
        await CommitInputAsync();
        if (target.StartsWith("tab:")) await ExecuteAsync("sheet.tabColor", new { sheet = target[4..], color = value });
        else if (target is "shapeFill" or "shapeLine")
        {
            if (selectedShape is not uint id) throw new InvalidOperationException("図形を選択してください。");
            await ExecuteAsync(target == "shapeFill" ? "chart.shapeFill" : "chart.shapeOutline", new { kind = "shape", id, color = value });
        }
        else await ExecuteAsync(target == "fill" ? "home.fillColor" : "home.fontColor", new { color = value });
        if (target is "fill" or "font" && color is Windows.UI.Color selected)
        {
            if (target == "fill")
            {
                currentFillColor = value; FillColorSwatch.Fill =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(selected);
            }
            else { currentFontColor = value; FontColorSwatch.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(selected); }
        }
        dirty = true; await RefreshAsync(); if (selectedShape is uint selectedId) FocusShape(selectedId); else FocusCell(selectionLead);
    });
    async void ChangeShape_Click(object sender, RoutedEventArgs e)
    {
        if (selectedShape is not uint id || sender is not FrameworkElement { Tag: string type }) return;
        await RunAsync(async () =>
        {
            await ExecuteAsync("shape.change", new
            {
                id,
                type
            }); dirty = true; await RefreshAsync(); FocusShape(id);
        });
    }
    void ShapeGallery_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string mode } anchor) return;
        Flyout flyout = new();
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "図形を選択" });
        Grid grid = new() { ColumnSpacing = 4, RowSpacing = 4 };
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        (string Kind, string Label)[] shapes = [
            ("rectangle", "四角形"), ("roundedRectangle", "角丸四角形"), ("ellipse", "楕円"),
            ("triangle", "三角形"), ("diamond", "ひし形"), ("pentagon", "五角形"),
            ("hexagon", "六角形"), ("line", "線"), ("arrow", "矢印") ];
        for (int i = 0; i < shapes.Length; i++)
        {
            (string Kind, string Label) item = shapes[i];
            Button choice = new()
            {
                Width = 42,
                Height = 42,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = ShapeGalleryIcon.Create(item.Kind)
            };
            AutomationProperties.SetName(choice, item.Label); ToolTipService.SetToolTip(choice, item.Label);
            choice.Click += async (_, _) =>
            {
                flyout.Hide();
                await RunAsync(async () =>
                {
                    await CommitInputAsync();
                    if (mode == "change")
                    {
                        if (selectedShape is not uint id) throw new InvalidOperationException("図形を選択してください。");
                        await ExecuteAsync("shape.change", new { id, type = item.Kind });
                    }
                    else await ExecuteAsync("insert.shape", new { kind = item.Kind, at = activeCell });
                    dirty = true; await RefreshAsync();
                    if (mode == "change" && selectedShape is uint shapeId) FocusShape(shapeId); else FocusCell(activeCell);
                });
            };
            Grid.SetRow(choice, i / 5); Grid.SetColumn(choice, i % 5); grid.Children.Add(choice);
        }
        for (int i = 0; i < (shapes.Length + 4) / 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(grid); flyout.Content = panel; flyout.ShowAt(anchor);
    }
    void FormulaInput_TextChanged(object sender, TextChangedEventArgs e) { if (!updatingInput) { dirty = true; inputPending = true; } }
    async void FormulaInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool alt = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.Enter && !alt)
        {
            e.Handled = true;
            await RunAsync(async () =>
            {
                await CommitInputAsync(); (int row,
                int col) = ParseCell(activeCell); row = await MoveVisibleRowAsync(row, 1, 1); EnsureVisible(row,
                    col); string next = CellAddress(row, col); await SelectRangeAsync(next, next);
            });
        }
    }
    void FormulaToggle_Click(object sender, RoutedEventArgs e)
    {
        formulaExpanded = !formulaExpanded;
        FormulaInput.Height = formulaExpanded ? 110 : double.NaN;
        FormulaInput.MaxHeight = formulaExpanded ? 140 : 40;
        FormulaToggleIcon.Glyph = formulaExpanded ? ChevronUp : ChevronDown;
        string label = formulaExpanded ? "数式バーを折りたたむ" : "数式バーを展開";
        AutomationProperties.SetName(FormulaToggle, label); ToolTipService.SetToolTip(FormulaToggle, label);
    }
    async void AddressInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await RunAsync(async () =>
        {
            (int row, int col) = ParseCell(AddressInput.Text); string address = CellAddress(row,
            col); EnsureVisible(row, col); await SelectRangeAsync(address, address);
        });
    }
    void AddressInput_LostFocus(object sender, RoutedEventArgs e) { AddressInput.Text = activeCell; }
    async Task CommitInputAsync()
    {
        if (!inputPending) return;
        await ExecuteAsync("cell.set", new { cell = activeCell, input = FormulaInput.Text });
        inputPending = false;
        viewportDirty = true;
    }
    Task ApplyAsync() => RunAsync(async () => { await CommitInputAsync(); await RefreshAsync(); });
    async void Undo_Click(object sender,
        RoutedEventArgs e) => await RunAsync(async () =>
        {
            await CommitInputAsync(); await ExecuteAsync("edit.undo",
            new { }); dirty = true; await RefreshAsync();
        });
    async void Redo_Click(object sender,
        RoutedEventArgs e) => await RunAsync(async () =>
        {
            await CommitInputAsync(); await ExecuteAsync("edit.redo",
            new { }); dirty = true; await RefreshAsync();
        });
    async void Previous_Click(object sender,
        RoutedEventArgs e) => await RunAsync(async () =>
        {
            await CommitInputAsync(); firstRow = Math.Max(1,
            firstRow - PageRows); await RefreshAsync();
        });
    async void Next_Click(object sender,
        RoutedEventArgs e) => await RunAsync(async () =>
        {
            await CommitInputAsync(); firstRow = Math.Min(1_048_576,
            firstRow + PageRows); await RefreshAsync();
        });
}
