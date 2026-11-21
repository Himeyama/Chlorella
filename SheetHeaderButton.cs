using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Chlorella;

sealed class SheetHeaderButton : Button
{
    static readonly InputCursor ColumnResizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    static readonly InputCursor RowResizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

    public void SetResizeCursor(bool row, bool onEdge) => ProtectedCursor = onEdge ? (row ? RowResizeCursor : ColumnResizeCursor) : null;
}

