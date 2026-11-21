using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;

namespace Chlorella;

public sealed partial class MainWindow : Window
{
    const int PageRows = 80;
    const int PageColumns = 48;
    const string ChevronUp = "\uE70E";
    const string ChevronDown = "\uE70D";
    sealed class IncompatibleDesktopHostException() : Exception("データ提供側から必要な表示情報を取得できません。");

    readonly IWorksheetDataSource? dataSource;
    int firstRow = 1;
    int firstColumn;
    double columnScrollFraction;
    uint? selectedShape;
    uint? selectedImage;
    (string Kind, uint Id, string Sheet, bool Cut)? objectClipboard;
    string? objectClipboardToken;
    string currentSheet = "";
    Border? selectionCorner;
    (int Top, int Left, int Bottom, int Right) selectionBounds;
    (int Top, int Left, int Bottom, int Right, string Sheet)? copiedCells;
    int scrollRowLimit = 100;
    int scrollColumnLimit = 26;
    int viewRows = 40;
    int viewColumns = 24;
    int renderedRow;
    int renderedColumn = -1;
    int renderedRows;
    int renderedColumns;
    double renderedColumnScrollFraction;
    bool viewportDirty;
    bool activeTableFilter;
    string activeCell = "A1";
    string currentSelection = "A1";
    bool dirty;
    bool confirmedClose;
    bool updatingInput;
    bool inputPending;
    bool activeCellMerged;
    object currentFillColor = "#FFFF00";
    object currentFontColor = "#FF0000";
    JsonElement paletteTheme;
    bool dragging;
    string? lastCellClick;
    long lastCellClickTime;
    string? cutClipboardText;
    bool cutPasted;
    string dragStart = "A1";
    string dragEnd = "A1";
    string selectionAnchor = "A1";
    string selectionLead = "A1";
    bool updatingSheets;
    string? sheetTabsState;
    bool ribbonCollapsed;
    bool formulaExpanded;
    string? draggedSheet;
    int draggedSheetIndex;
    bool updatingFont = true;
    bool updatingNumberFormat;
    TextBox? cellEditor;
    bool cellInputWaiting;
    readonly double[] columnWidths = new double[PageColumns];
    readonly double[] rowHeights = new double[PageRows];
    bool updatingScroll = true;
    bool selectingHeader;
    bool selectingHeaderRow;
    int headerSelectionStart;
    int headerSelectionEnd;
    bool resizingHeader;
    string resizeTag = "";
    double resizeOrigin;
    double resizeStart;
    string? lastHeaderEdge;
    long lastHeaderEdgeTime;
    Windows.Foundation.Point lastHeaderEdgePoint;
    readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    bool scrollBusy;

    public MainWindow() : this(new WorksheetEngine())
    {
    }

    public MainWindow(IWorksheetDataSource? dataSource)
    {
        this.dataSource = dataSource;
        InitializeComponent();
        foreach (string font in InstalledFonts()) FontNameInput.Items.Add(font);
        foreach (string size in new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "24", "28", "36", "48",
            "72" }) FontSizeInput.Items.Add(size);
        SetFontInputs("Yu Gothic", "11");
        updatingFont = false;
        updatingScroll = false;
        scrollTimer.Tick += ScrollTimer_Tick;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        SetInitialWindowSize();
        AppWindow.Closing += AppWindow_Closing;
        FormulaInput.EnterPressed += FormulaInput_KeyDown;
        CellGrid.PointerMoved += CellGrid_PointerMoved;
        CellGrid.PointerReleased += CellGrid_PointerReleased;
        CellGrid.PointerCaptureLost += CellGrid_PointerCaptureLost;
        Closed += Window_Closed;
        if (dataSource is null)
        {
            SetStatus("プレビュー: データ提供側が接続されていません。編集・保存はできません。");
            foreach (UIElement element in RootGrid.Children)
                if (element is FrameworkElement child && Grid.GetRow(child) is >= 1 and <= 4) DisablePreviewControls(child);
        }
        else _ = StartAsync();
    }
    static void DisablePreviewControls(DependencyObject node)
    {
        if (node is Control control) control.IsEnabled = false;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            DisablePreviewControls(VisualTreeHelper.GetChild(node, i));
    }

    static IEnumerable<string> InstalledFonts()
    {
        SortedSet<string> names = new(StringComparer.CurrentCultureIgnoreCase) { "Yu Gothic", "Meiryo", "Calibri", "Arial", "Segoe UI" };
        foreach (Microsoft.Win32.RegistryKey root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                if (key is null) continue;
                foreach (string value in key.GetValueNames())
                {
                    string display = value.Replace(" (TrueType)", "", StringComparison.OrdinalIgnoreCase)
                        .Replace(" (OpenType)", "", StringComparison.OrdinalIgnoreCase);
                    foreach (string name in display.Split('&',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) names.Add(name);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return names;
    }

    async Task StartAsync()
    {
        try
        {
            await RefreshAsync();
            FocusCell(activeCell);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                object? focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
                if (cellInputWaiting && cellEditor is not null && (focused is null || ReferenceEquals(focused, AppTitleBar)))
                    cellEditor.Focus(FocusState.Programmatic);
            });
        }
        catch (Exception ex)
        {
            SetStatus($"表示の初期化に失敗しました: {ex.Message}");
        }
    }

    Task<JsonElement> SendAsync(string method, object parameters) =>
        dataSource?.SendAsync(method, parameters) ?? Task.FromException<JsonElement>(
            new InvalidOperationException("データ提供側が接続されていません。"));

    async Task<JsonElement> ExecuteAsync(string command, object parameters)
    {
        JsonElement result = await SendAsync("engine.execute", new { command, @params = parameters });
        UpdateHistoryButtons();
        return result;
    }
}
