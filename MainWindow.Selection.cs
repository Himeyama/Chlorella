using System.Text.Json;

namespace Chlorella;

public sealed partial class MainWindow
{
    bool TryUpdateSelection(JsonElement selection)
    {
        if (dataSource is not WorksheetEngine || renderedView.ValueKind != JsonValueKind.Object ||
            !selection.TryGetProperty("activeCell", out JsonElement active) ||
            renderedRow != firstRow || renderedColumn != firstColumn ||
            renderedRows != viewRows || renderedColumns != viewColumns) return false;

        string address = active.GetString() ?? "A1";
        (int row, int col) = ParseCell(address);
        if (row < firstRow || row >= firstRow + viewRows || col < firstColumn || col >= firstColumn + viewColumns) return false;

        activeCell = address;
        currentSelection = selection.GetProperty("selection").GetString() ?? address;
        AddressInput.Text = address;
        updatingInput = true;
        try { FormulaInput.Text = selection.GetProperty("input").GetString() ?? ""; }
        finally { updatingInput = false; }
        inputPending = false;
        UpdateHistoryButtons();
        JsonElement rows = renderedView.GetProperty("regions")[0].GetProperty("cells");
        JsonElement styles = renderedView.GetProperty("styles");
        JsonElement merges = renderedView.GetProperty("merges");
        UpdateMergeButton(merges);
        UpdateWrapButton(rows, styles, merges);
        UpdateActiveFont(rows, styles);
        (int top, int left, int bottom, int right) = ReadSelectionBounds(selection);
        UpdateSelectionVisuals(top, left, bottom, right);
        return true;
    }
}
