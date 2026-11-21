using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Chlorella;

// The drawing interaction owns pointer capture. ButtonBase's pointer handlers
// otherwise release that capture before the drawing's PointerReleased handler.
sealed class DrawingObjectButton : Button
{
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
    }
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
    }
}
