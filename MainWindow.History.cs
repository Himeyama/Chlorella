using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Chlorella;

public sealed partial class MainWindow
{
    async Task<bool> HandleObjectShortcutAsync(KeyRoutedEventArgs e, string kind, uint id)
    {
        bool control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (!control || e.Key is not (VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.Z or VirtualKey.Y)) return false;
        e.Handled = true;
        if (e.Key is VirtualKey.C or VirtualKey.X) CopyObject(kind, id, e.Key == VirtualKey.X);
        else if (e.Key == VirtualKey.V) await RunAsync(() => ClipboardCommandAsync("edit.paste"));
        else
        {
            bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            await RunAsync(() => ApplyHistoryAsync(e.Key == VirtualKey.Y || shift));
        }
        return true;
    }

    void UpdateHistoryButtons()
    {
        if (UndoButton is null || RedoButton is null) return;
        if (dataSource is WorksheetEngine engine)
        {
            UndoButton.IsEnabled = engine.CanUseHistory && (inputPending || engine.CanUndo);
            RedoButton.IsEnabled = !inputPending && engine.CanRedo;
        }
        else
        {
            UndoButton.IsEnabled = dataSource is not null;
            RedoButton.IsEnabled = dataSource is not null;
        }
    }

    async Task ApplyHistoryAsync(bool redo)
    {
        if (dataSource is WorksheetEngine engine &&
            (redo ? inputPending || !engine.CanRedo : !engine.CanUseHistory || (!inputPending && !engine.CanUndo))) return;
        await CommitInputAsync();
        await ExecuteAsync(redo ? "edit.redo" : "edit.undo", new { });
        dirty = true;
        await RefreshAsync();
        selectionAnchor = activeCell;
        selectionLead = activeCell;
        FocusCell(activeCell);
    }
}
