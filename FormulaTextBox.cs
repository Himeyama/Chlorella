using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Chlorella;

public sealed class FormulaTextBox : TextBox
{
    public event KeyEventHandler? EnterPressed;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        bool alt = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.Enter && !alt)
        {
            e.Handled = true;
            EnterPressed?.Invoke(this, e);
            return;
        }
        base.OnKeyDown(e);
    }
}
