using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Chlorella;

public sealed partial class MainWindow
{
    Canvas? scrollPreview;
    readonly List<TranslateTransform> rowHeaderOffsets = [];

    void UpdateHorizontalOffset()
    {
        if (CellGridOffset is null) return;
        double offset = columnScrollFraction * columnWidths[0];
        CellGridOffset.X = -offset;
        foreach (TranslateTransform transform in rowHeaderOffsets) transform.X = offset;
        if (renderedColumn >= 0) renderedColumnScrollFraction = columnScrollFraction;
    }

    void ShowScrollPreview()
    {
        if (dataSource is null || renderedRow < 1 || renderedRows == 0 || renderedColumns == 0 || scrollPreview is not null) return;
        // Keep the last complete viewport visible, but prevent interacting with its old cell addresses.
        Canvas blocker = new()
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRowSpan(blocker, renderedRows + 1);
        Grid.SetColumnSpan(blocker, renderedColumns + 1);
        Canvas.SetZIndex(blocker, 100);
        CellGrid.Children.Add(blocker);
        scrollPreview = blocker;
    }

    void RemoveScrollPreview()
    {
        if (scrollPreview is null) return;
        CellGrid.Children.Remove(scrollPreview);
        scrollPreview = null;
    }
}
