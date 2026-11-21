using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Chlorella;

sealed class SheetCellButton : Button
{
    public event KeyEventHandler? EnterPressed;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            EnterPressed?.Invoke(this, e);
            return;
        }
        base.OnKeyDown(e);
    }
}
