using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Chlorella;

public sealed partial class MainWindow
{
    bool movingRange, moveRows, moveColumns, moveVertical;
    int moveStartRow, moveStartColumn, moveRowOffset, moveColumnOffset;
    int moveInsertion;
    readonly List<Border> movePreview = [];

    static bool ShiftDown() => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;

    void BeginRangeMove(PointerRoutedEventArgs e, int row, int column, bool rows, bool columns)
    {
        moveStartRow = row;
        moveStartColumn = column;
        moveRowOffset = 0;
        moveColumnOffset = 0;
        moveRows = rows;
        moveColumns = columns;
        movingRange = CellGrid.CapturePointer(e.Pointer);
        if (movingRange) ShowMoveStatus();
        e.Handled = movingRange;
    }

    void UpdateRangeMove(PointerRoutedEventArgs e)
    {
        Windows.Foundation.Point point = e.GetCurrentPoint(CellGrid).Position;
        int row = firstRow + IndexAt(point.Y - 25, rowHeights, viewRows) - 1;
        int column = firstColumn + IndexAt(point.X - 48, columnWidths, viewColumns);
        moveRowOffset = moveColumns ? 0 : row - moveStartRow;
        moveColumnOffset = moveRows ? 0 : column - moveStartColumn;
        moveVertical = moveRows || (!moveColumns && Math.Abs(moveRowOffset) >= Math.Abs(moveColumnOffset));
        int index = moveVertical ? IndexAt(point.Y - 25, rowHeights, viewRows) : IndexAt(point.X - 48, columnWidths, viewColumns);
        double[] sizes = moveVertical ? rowHeights : columnWidths;
        double coordinate = (moveVertical ? point.Y - 25 : point.X - 48);
        for (int i = 0; i < index; i++) coordinate -= sizes[i];
        moveInsertion = (moveVertical ? firstRow - 1 : firstColumn) + index + (coordinate >= sizes[index] / 2 ? 1 : 0);
        UpdateMovePreview();
        ShowMoveStatus();
        e.Handled = true;
    }

    void ShowMoveStatus()
    {
        statusTimer.Stop();
        StatusText.Text = ShiftDown()
            ? "挿入: 線の位置に移動し、間のセルを詰めます"
            : "移動: 枠で示した場所へ移動します（Shift キーで入れ替え）";
    }

    void UpdateMovePreview()
    {
        ClearMovePreview();
        if (ShiftDown())
        {
            UpdateInsertionPreview();
            return;
        }
        if (moveRowOffset == 0 && moveColumnOffset == 0) return;
        int top = selectionBounds.Top + moveRowOffset, bottom = selectionBounds.Bottom + moveRowOffset;
        int left = selectionBounds.Left + moveColumnOffset, right = selectionBounds.Right + moveColumnOffset;
        if (top < 0 || bottom >= 1048576 || left < 0 || right >= 16384) return;
        bool swap = false;
        bool vertical = Math.Abs(moveRowOffset) >= Math.Abs(moveColumnOffset);
        bool showTop = !swap || (vertical && moveRowOffset > 0);
        bool showBottom = !swap || (vertical && moveRowOffset < 0);
        bool showLeft = !swap || (!vertical && moveColumnOffset > 0);
        bool showRight = !swap || (!vertical && moveColumnOffset < 0);
        List<Border> preview = [];
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not Button { Tag: string tag } button || element is not (SheetCellButton or SheetHeaderButton) ||
                tag == "all") continue;
            Thickness edge;
            if (tag.StartsWith("row:"))
            {
                if (!moveRows || !int.TryParse(tag[4..], out int row) || row - 1 < top || row - 1 > bottom) continue;
                edge = new Thickness(0, showTop && row - 1 == top ? 3 : 0, 0, showBottom && row - 1 == bottom ? 3 : 0);
            }
            else if (tag.StartsWith("col:"))
            {
                if (!moveColumns || !int.TryParse(tag[4..], out int column) || column < left || column > right) continue;
                edge = new Thickness(showLeft && column == left ? 3 : 0, 0, showRight && column == right ? 3 : 0, 0);
            }
            else
            {
                (int row, int column) = ParseCell(tag);
                row--;
                if (row < top || row > bottom || column < left || column > right) continue;
                edge = new Thickness(showLeft && column == left ? 3 : 0, showTop && row == top ? 3 : 0,
                    showRight && column + Grid.GetColumnSpan(button) - 1 == right ? 3 : 0,
                    showBottom && row + Grid.GetRowSpan(button) - 1 == bottom ? 3 : 0);
            }
            if (edge == new Thickness(0)) continue;
            Border overlay = new()
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"],
                BorderThickness = edge,
                IsHitTestVisible = false
            };
            Grid.SetRow(overlay, Grid.GetRow(button));
            Grid.SetColumn(overlay, Grid.GetColumn(button));
            Grid.SetRowSpan(overlay, Grid.GetRowSpan(button));
            Grid.SetColumnSpan(overlay, Grid.GetColumnSpan(button));
            Canvas.SetZIndex(overlay, 6);
            preview.Add(overlay);
        }
        foreach (Border overlay in preview)
        {
            CellGrid.Children.Add(overlay);
            movePreview.Add(overlay);
        }
    }

    void UpdateInsertionPreview()
    {
        int start = moveVertical ? selectionBounds.Top : selectionBounds.Left;
        int end = moveVertical ? selectionBounds.Bottom : selectionBounds.Right;
        if (moveInsertion >= start && moveInsertion <= end + 1) return;
        int limit = moveVertical ? 1048576 : 16384;
        int visibleEnd = moveVertical ? firstRow - 1 + viewRows : firstColumn + viewColumns;
        bool trailing = moveInsertion == limit || moveInsertion == visibleEnd;
        int lineCell = trailing ? moveInsertion - 1 : moveInsertion;
        List<Border> preview = [];
        foreach (UIElement element in CellGrid.Children)
        {
            if (element is not Button { Tag: string tag } button || element is not (SheetCellButton or SheetHeaderButton) ||
                tag == "all") continue;
            bool target = false;
            if (tag.StartsWith("row:")) target = moveRows && moveVertical && int.TryParse(tag[4..], out int row) && row - 1 == lineCell;
            else if (tag.StartsWith("col:")) target = moveColumns && !moveVertical && int.TryParse(tag[4..],
                out int column) && column == lineCell;
            else
            {
                (int row, int column) = ParseCell(tag);
                row--;
                target = moveVertical ? row == lineCell && column >= selectionBounds.Left && column <= selectionBounds.Right
                    : column == lineCell && row >= selectionBounds.Top && row <= selectionBounds.Bottom;
            }
            if (!target) continue;
            Thickness edge = moveVertical ? new Thickness(0, trailing ? 0 : 3, 0, trailing ? 3 : 0)
                : new Thickness(trailing ? 0 : 3, 0, trailing ? 3 : 0, 0);
            Border overlay = new()
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)RootGrid.Resources["MergeAccentBrush"],
                BorderThickness = edge,
                IsHitTestVisible = false
            };
            Grid.SetRow(overlay, Grid.GetRow(button));
            Grid.SetColumn(overlay, Grid.GetColumn(button));
            Grid.SetRowSpan(overlay, Grid.GetRowSpan(button));
            Grid.SetColumnSpan(overlay, Grid.GetColumnSpan(button));
            Canvas.SetZIndex(overlay, 6);
            preview.Add(overlay);
        }
        foreach (Border overlay in preview)
        {
            CellGrid.Children.Add(overlay);
            movePreview.Add(overlay);
        }
    }

    void ClearMovePreview()
    {
        foreach (Border overlay in movePreview) CellGrid.Children.Remove(overlay);
        movePreview.Clear();
    }

    async Task FinishRangeMoveAsync(PointerRoutedEventArgs e)
    {
        UpdateRangeMove(e);
        ClearMovePreview();
        SetStatus("");
        movingRange = false;
        CellGrid.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        bool swap = ShiftDown();
        if (!swap && moveRowOffset == 0 && moveColumnOffset == 0) return;
        if (swap && moveInsertion >= (moveVertical ? selectionBounds.Top : selectionBounds.Left) &&
            moveInsertion <= (moveVertical ? selectionBounds.Bottom : selectionBounds.Right) + 1) return;
        await RunAsync(async () =>
        {
            await CommitInputAsync();
            await ExecuteAsync("edit.moveRange", new
            {
                rowOffset = moveRowOffset,
                columnOffset = moveColumnOffset,
                swap,
                vertical = moveVertical,
                insertIndex = moveInsertion
            });
            dirty = true;
            await RefreshAsync();
            selectionAnchor = activeCell;
            selectionLead = activeCell;
        });
    }
}
