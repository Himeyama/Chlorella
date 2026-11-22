using System.IO.Compression;
using System.Text.Json;
using Chlorella;

WorksheetEngine historyEngine = new();
if (historyEngine.CanUndo || historyEngine.CanRedo) throw new Exception("Initial history availability");
await historyEngine.SendAsync("engine.execute", new { command = "cell.set", @params = new { cell = "A1", input = "test" } });
if (!historyEngine.CanUndo || historyEngine.CanRedo) throw new Exception("History after edit");
await historyEngine.SendAsync("engine.execute", new { command = "edit.undo", @params = new { } });
if (historyEngine.CanUndo || !historyEngine.CanRedo) throw new Exception("History after undo");
await historyEngine.SendAsync("engine.execute", new { command = "edit.redo", @params = new { } });
if (!historyEngine.CanUndo || historyEngine.CanRedo) throw new Exception("History after redo");
WorksheetEngine engine = new();
WorksheetEngine moveEngine = new();
async Task<JsonElement> MoveRun(string command, object args) => await moveEngine.SendAsync("engine.execute", new { command, @params = args });
await MoveRun("cell.set", new { cell = "A1", input = "left" });
await MoveRun("cell.set", new { cell = "B1", input = "right" });
await MoveRun("selection.set", new { range = "A1" });
await MoveRun("edit.moveRange", new { rowOffset = 0, columnOffset = 1, swap = true });
JsonElement movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B1" } });
if (movedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() != "right" || movedView.GetProperty("regions")[0].GetProperty("cells")[0][1].GetProperty("text").GetString() != "left") throw new Exception("Swap range");
await MoveRun("edit.undo", new { });
movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B1" } });
if (movedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() != "left") throw new Exception("Undo range swap");
await MoveRun("cell.set", new { cell = "C1", input = "third" });
await MoveRun("selection.set", new { range = "A1" });
await MoveRun("edit.moveRange", new { rowOffset = 0, columnOffset = 2, swap = true, vertical = false, insertIndex = 3 });
movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C1" } });
JsonElement movedCells = movedView.GetProperty("regions")[0].GetProperty("cells")[0];
if (movedCells[0].GetProperty("text").GetString() != "right" || movedCells[1].GetProperty("text").GetString() != "third" || movedCells[2].GetProperty("text").GetString() != "left") throw new Exception("Insert moved cell after intervening cells");
await MoveRun("edit.undo", new { });
await MoveRun("selection.set", new { range = "B1:C1" });
await MoveRun("edit.moveRange", new { rowOffset = 0, columnOffset = -1, swap = true, vertical = false, insertIndex = 0 });
movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C1" } });
movedCells = movedView.GetProperty("regions")[0].GetProperty("cells")[0];
if (movedCells[0].GetProperty("text").GetString() != "right" || movedCells[1].GetProperty("text").GetString() != "third" || movedCells[2].GetProperty("text").GetString() != "left") throw new Exception("Insert multiple cells before intervening cell");
await MoveRun("cell.set", new { cell = "A2", input = "second row" });
await MoveRun("cell.set", new { cell = "A3", input = "third row" });
await MoveRun("selection.set", new { range = "A1:XFD1", active = "A1" });
await MoveRun("edit.moveRange", new { rowOffset = 2, columnOffset = 0, swap = true, vertical = true, insertIndex = 3 });
movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:A3" } });
movedCells = movedView.GetProperty("regions")[0].GetProperty("cells");
if (movedCells[0][0].GetProperty("text").GetString() != "second row" || movedCells[1][0].GetProperty("text").GetString() != "third row" || movedCells[2][0].GetProperty("text").GetString() != "right") throw new Exception("Insert moved row");
await MoveRun("edit.undo", new { });
movedView = await moveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1" } });
if (movedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() != "right") throw new Exception("Undo inserted row");
WorksheetEngine mergedMoveEngine = new();
async Task<JsonElement> MergedRun(string command, object args) => await mergedMoveEngine.SendAsync("engine.execute", new { command, @params = args });
await MergedRun("cell.set", new { cell = "A1", input = "merged" });
await MergedRun("selection.set", new { range = "A1:B2" });
await MergedRun("home.mergeCells", new { });
await MergedRun("edit.moveRange", new { rowOffset = 1, columnOffset = 1, swap = false });
JsonElement mergedView = await mergedMoveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "B2:C3" } });
if (mergedView.GetProperty("merges").GetArrayLength() != 1 || mergedView.GetProperty("input").GetString() != "merged") throw new Exception("Move merged range");
await MergedRun("edit.undo", new { });
await MergedRun("edit.moveRange", new { rowOffset = 0, columnOffset = 2, swap = true, vertical = false, insertIndex = 4 });
mergedView = await mergedMoveEngine.SendAsync("sheet.viewport", new { ranges = new[] { "C1:D2" } });
if (mergedView.GetProperty("merges").GetArrayLength() != 1 || mergedView.GetProperty("input").GetString() != "merged") throw new Exception("Insert moved merged range");
WorksheetEngine mergeShiftEngine = new();
async Task<JsonElement> MergeShiftRun(string command, object args) => await mergeShiftEngine.SendAsync("engine.execute", new { command, @params = args });
await MergeShiftRun("cell.set", new { cell = "A3", input = "merged" });
await MergeShiftRun("selection.set", new { range = "A3:B4" });
await MergeShiftRun("home.mergeCells", new { });
await MergeShiftRun("edit.shiftCells", new { range = "A1:B2", insert = false, vertical = true });
JsonElement shiftedMerge = await mergeShiftEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B2" } });
if (shiftedMerge.GetProperty("merges").GetArrayLength() != 1 || shiftedMerge.GetProperty("merges")[0].GetProperty("text").GetString() != "merged") throw new Exception("Shift merged cells");
await MergeShiftRun("edit.undo", new { });
bool rejectedSplit = false;
try { await MergeShiftRun("edit.shiftCells", new { range = "A1:A2", insert = false, vertical = true }); }
catch (InvalidOperationException) { rejectedSplit = true; }
if (!rejectedSplit) throw new Exception("Reject split merged cells");
await MergeShiftRun("edit.shiftCells", new { range = "A3:B4", insert = false, vertical = true });
shiftedMerge = await mergeShiftEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B4" } });
if (shiftedMerge.GetProperty("merges").GetArrayLength() != 0) throw new Exception("Delete merged cells");
WorksheetEngine shiftEngine = new();
async Task<JsonElement> ShiftRun(string command, object args) => await shiftEngine.SendAsync("engine.execute", new { command, @params = args });
await ShiftRun("cell.set", new { cell = "A1", input = "one" });
await ShiftRun("cell.set", new { cell = "A2", input = "two" });
await ShiftRun("edit.shiftCells", new { range = "A1", insert = true, vertical = true });
JsonElement shiftView = await shiftEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:A3" } });
if (shiftView.GetProperty("regions")[0].GetProperty("cells")[1][0].GetProperty("text").GetString() != "one") throw new Exception("Insert shifted cells");
await ShiftRun("edit.shiftCells", new { range = "A1", insert = false, vertical = true });
shiftView = await shiftEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:A2" } });
if (shiftView.GetProperty("regions")[0].GetProperty("cells")[1][0].GetProperty("text").GetString() != "two") throw new Exception("Delete shifted cells");
async Task<JsonElement> Run(string command, object args) => await engine.SendAsync("engine.execute", new { command, @params = args });
async Task<string> Input(string cell)
{
    await Run("selection.set", new { range = cell });
    JsonElement view = await engine.SendAsync("sheet.viewport", new { ranges = new[] { cell } });
    return view.GetProperty("input").GetString() ?? "";
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task<JsonElement> Font(string cell)
{
    JsonElement view = await engine.SendAsync("sheet.viewport", new { ranges = new[] { cell } });
    string style = view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText();
    return view.GetProperty("styles").GetProperty(style).GetProperty("font");
}
async Task<string> Display(string cell)
{
    JsonElement view = await engine.SendAsync("sheet.viewport", new { ranges = new[] { cell } });
    return view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() ?? "";
}

await Run("cell.set", new { cell = "F1", input = "=SUM(1,AVERAGE(2,4" });
Check(await Input("F1") == "=SUM(1,AVERAGE(2,4))" && await Display("F1") == "4", "Complete nested formula parentheses");
await Run("cell.set", new { cell = "F2", input = "=SUM(1))(" });
Check(await Input("F2") == "=SUM(1))(", "Preserve invalid extra closing parenthesis");
await Run("cell.set", new { cell = "F3", input = "=SUM(\"(\")" });
Check(await Input("F3") == "=SUM(\"(\")", "Ignore parentheses in quoted strings");

await Run("cell.set", new { cell = "G1", input = "１２３" });
Check(await Input("G1") == "123", "Full-width digits normalized");
await Run("cell.set", new { cell = "G2", input = "品番１２" });
Check(await Input("G2") == "品番１２", "Mixed text preserved");
await Run("selection.set", new { range = "G3" });
await Run("edit.paste", new { text = "４５\tA６" });
Check(await Input("G3") == "45" && await Input("H3") == "A６", "Pasted full-width digits");
await Run("cell.set", new { cell = "H1", input = "2" });
await Run("cell.set", new { cell = "H2", input = "4" });
await Run("cell.set", new { cell = "I1", input = "=SUM(H1:H2)*2" });
Check(await Display("I1") == "12", "Formula aggregate and arithmetic");
await Run("cell.set", new { cell = "K1", input = "=COUNT(G2,H1:H2)" });
Check(await Display("K1") == "2", "COUNT ignores referenced text");
await Run("cell.set", new { cell = "H2", input = "6" });
Check(await Display("I1") == "16", "Formula recalculates on edit");
await Run("cell.set", new { cell = "H2", input = "4" });
await Run("selection.set", new { range = "H2" });
await Run("edit.clearContents", new { });
Check(await Display("H2") == "" && await Display("I1") == "4", "Delete updates referenced formula");
await Run("edit.undo", new { });
Check(await Display("H2") == "4" && await Display("I1") == "12", "Undo restores deleted value and formula result");
await Run("cell.set", new { cell = "I2", input = "=1/0" });
Check(await Display("I2") == "#DIV/0!", "Formula divide by zero");
await Run("cell.set", new { cell = "I3", input = "=I3" });
Check(await Display("I3") == "#REF!", "Circular formula");
await Run("edit.autoFill", new { source = "H1:H2", target = "H1:H4" });
Check(await Input("H4") == "8", "Autofill series");
await Run("cell.set", new { cell = "J1", input = "=H1+$H$2" });
await Run("edit.autoFill", new { source = "J1", target = "J1:J2" });
Check(await Input("J2") == "=H2+$H$2" && await Display("J2") == "8", "Autofill formula references");
await Run("edit.undo", new { });
Check(await Input("J2") == "", "Undo autofill");
await Run("cell.set", new { cell = "L3", input = "10" });
await Run("cell.set", new { cell = "M3", input = "15" });
await Run("selection.set", new { range = "L3:M3" });
await Run("home.bold", new { });
await Run("edit.autoFill", new { source = "L3:M3", target = "K3:N3" });
Check(await Input("K3") == "5" && await Input("N3") == "20", "Autofill horizontal and reverse series");
Check((await Font("N3")).GetProperty("bold").GetBoolean(), "Autofill copies formatting");

await Run("cell.set", new { cell = "A1", input = "001" });
await Run("cell.set", new { cell = "B1", input = "hello\tworld" });
await Run("selection.set", new { range = "A1:B1" });
string copied = (await Run("edit.copy", new { })).GetProperty("text").GetString() ?? "";
await Run("selection.set", new { range = "A2" });
await Run("edit.paste", new { text = copied });
Check(await Input("B2") == "hello\tworld", "Clipboard quoting");
await Run("edit.undo", new { });
Check(await Input("A2") == "", "Undo paste");
await Run("edit.redo", new { });
Check(await Input("A2") == "001", "Redo paste");
await Run("selection.set", new { range = "A1:B2", active = "A1" });
await Run("home.fontSize", new { size = 24 });
await Run("home.fontName", new { name = "Arial" });
await Run("home.bold", new { });
await Run("home.italic", new { });
await Run("home.underline", new { });
JsonElement font = await Font("B2");
Check(font.GetProperty("size").GetDouble() == 24 && font.GetProperty("name").GetString() == "Arial" && font.GetProperty("bold").GetBoolean() && font.GetProperty("italic").GetBoolean() && font.GetProperty("underline").GetString() == "Single", "Range formatting");
await Run("edit.undo", new { });
Check((await Font("A1")).GetProperty("underline").GetString() == "None", "Undo formatting");
await Run("edit.redo", new { });
await Run("selection.set", new { range = "D4" });
await Run("home.fontSize", new { size = 18 });
await Run("home.insertSheet", new { });
await Run("cell.set", new { cell = "C3", input = "=A1+1" });
string path = Path.Combine(Path.GetTempPath(), $"chlorella-test-{Guid.NewGuid():N}.xlsx");
try
{
    await Run("file.saveAs", new { path });
    await Run("cell.set", new { cell = "C3", input = "changed" });
    await Run("file.open", new { path });
    Check(await Input("C3") == "=A1+1", "Workbook formula roundtrip");
    await Run("sheet.activate", new { sheet = 0 });
    Check(await Input("A1") == "001", "Workbook preserves textual number");
    Check((await Font("A1")).GetProperty("bold").GetBoolean() && (await Font("A1")).GetProperty("size").GetDouble() == 24, "Font roundtrip");
    Check((await Font("D4")).GetProperty("size").GetDouble() == 18, "Empty cell font roundtrip");
    using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(stream, ZipArchiveMode.Update))
    using (StreamWriter writer = new(zip.CreateEntry("external-change.txt").Open())) writer.Write("changed");
    JsonElement modified = await Run("file.open", new { path });
    Check(modified.GetProperty("imported").GetBoolean() && modified.GetProperty("modified").GetBoolean(), "Externally modified workbook is imported separately");
    await Run("sheet.activate", new { sheet = 0 });
    Check(await Input("A1") == "001", "Modified workbook retains cell values");
    bool sourceProtected = false;
    try { await Run("file.saveAs", new { path }); } catch (InvalidOperationException) { sourceProtected = true; }
    Check(sourceProtected, "Modified source workbook cannot be overwritten");
}
finally { File.Delete(path); }

await Run("cell.set", new { cell = "P1", input = "1234.5" });
await Run("selection.set", new { range = "P1" });
await Run("home.fontColor", new { color = "#FF0000" });
await Run("home.fillColor", new { color = "#00FF00" });
await Run("home.numberFormat", new { format = "Number" });
await Run("home.alignRight", new { });
await Run("home.wrapText", new { });
Check(await Display("P1") == "1234.50", "Number display format");
JsonElement originalView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "P1" } });
JsonElement originalStyle = originalView.GetProperty("styles").GetProperty(originalView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText());
Check(originalStyle.GetProperty("font").GetProperty("color").GetProperty("Rgb").GetUInt32() == 0xFF0000 && originalStyle.GetProperty("fill").GetProperty("fg").GetProperty("Rgb").GetUInt32() == 0x00FF00 && originalStyle.GetProperty("align").GetProperty("h").GetString() == "Right" && originalStyle.GetProperty("align").GetProperty("wrap").GetBoolean(), "Cell color and alignment");
JsonElement copiedWithStyle = await Run("edit.copy", new { });
await Run("selection.set", new { range = "Q1" });
await Run("edit.paste", new { text = copiedWithStyle.GetProperty("text").GetString(), data = copiedWithStyle.GetProperty("data").GetString() });
JsonElement pastedView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "Q1" } });
JsonElement pastedStyle = pastedView.GetProperty("styles").GetProperty(pastedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText());
Check(await Display("Q1") == "1234.50" && pastedStyle.GetProperty("fill").GetProperty("fg").GetProperty("Rgb").GetUInt32() == 0x00FF00, "Paste keeps formatting");
await Run("selection.set", new { range = "R1:S1" });
await Run("home.mergeCells", new { });
JsonElement mergeView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "R1:S1" } });
Check(mergeView.GetProperty("merges").GetArrayLength() == 1, "Merge cells");
JsonElement copiedMerge = await Run("edit.copy", new { });
await Run("selection.set", new { range = "T1" });
await Run("edit.paste", new { text = copiedMerge.GetProperty("text").GetString(), data = copiedMerge.GetProperty("data").GetString() });
JsonElement copiedMergeView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "T1:U1" } });
Check(copiedMergeView.GetProperty("merges").GetArrayLength() == 1, "Merged range copies with formatting");
await Run("selection.set", new { range = "V1" });
await Run("cell.set", new { cell = "V1", input = "0.25" });
await Run("home.percent", new { });
Check(await Display("V1") == "25%", "Percentage display");
await Run("home.increaseDecimal", new { });
Check(await Display("V1") == "25.0%", "Increase displayed decimal places");
await Run("selection.set", new { range = "V2" });
await Run("cell.set", new { cell = "V2", input = "61" });
await Run("home.numberFormat", new { format = "Short Date" });
Check(await Display("V2") == "1900/3/1", "Excel date serial");
await Run("selection.set", new { range = "W1" });
await Run("cell.set", new { cell = "W1", input = "=P1+$P$1" });
JsonElement copiedFormula = await Run("edit.copy", new { });
await Run("selection.set", new { range = "W2" });
await Run("edit.paste", new { text = copiedFormula.GetProperty("text").GetString(), data = copiedFormula.GetProperty("data").GetString() });
Check(await Input("W2") == "=P2+$P$1", "Pasted formulas shift relative references");
await Run("selection.set", new { range = "Z1" });
await Run("home.fillColor", new { color = "#0000FF" });
await Run("edit.paste", new { text = copiedFormula.GetProperty("text").GetString(), data = copiedFormula.GetProperty("data").GetString(), mode = "values" });
Check(await Input("Z1") == "2469", "Values paste uses calculated result");
JsonElement valuesView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "Z1" } });
JsonElement valuesStyle = valuesView.GetProperty("styles").GetProperty(valuesView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText());
Check(valuesStyle.GetProperty("fill").GetProperty("fg").GetProperty("Rgb").GetUInt32() == 0x0000FF, "Values paste preserves destination formatting");
await Run("selection.set", new { range = "Z2" });
await Run("cell.set", new { cell = "Z2", input = "keep" });
await Run("edit.paste", new { text = copiedWithStyle.GetProperty("text").GetString(), data = copiedWithStyle.GetProperty("data").GetString(), mode = "formats" });
Check(await Input("Z2") == "keep", "Formats paste preserves destination value");
JsonElement formatsView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "Z2" } });
JsonElement formatsStyle = formatsView.GetProperty("styles").GetProperty(formatsView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText());
Check(formatsStyle.GetProperty("fill").GetProperty("fg").GetProperty("Rgb").GetUInt32() == 0x00FF00, "Formats paste copies formatting");
await Run("selection.set", new { range = "Z3" });
await Run("edit.paste", new { text = copiedFormula.GetProperty("text").GetString(), data = copiedFormula.GetProperty("data").GetString(), mode = "formulas" });
Check(await Input("Z3") == "=S3+$P$1", "Formulas paste shifts relative references");
await Run("selection.set", new { range = "X1" });
await Run("cell.set", new { cell = "X1", input = "keep" });
await Run("selection.set", new { range = "W1:X1" });
bool rejectedMerge = false;
try { await Run("home.mergeCells", new { }); } catch (InvalidOperationException) { rejectedMerge = true; }
Check(rejectedMerge && await Input("X1") == "keep", "Merge protects other cell contents");
await Run("selection.set", new { range = "Y1" });
await Run("cell.set", new { cell = "Y1", input = "This is a long line that should wrap across several lines." });
await Run("home.wrapText", new { });
JsonElement wrapView = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "Y1" } });
Check(wrapView.GetProperty("regions")[0].GetProperty("rowHeights")[0].GetDouble() > 24, "Wrapped cell increases row height");
string formatPath = Path.Combine(Path.GetTempPath(), $"chlorella-format-{Guid.NewGuid():N}.xlsx");
try
{
    await Run("file.saveAs", new { path = formatPath });
    await Run("file.open", new { path = formatPath });
    Check(await Display("P1") == "1234.50", "Number format persists");
    JsonElement reopened = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "P1:S1" } });
    JsonElement savedStyle = reopened.GetProperty("styles").GetProperty(reopened.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText());
    Check(savedStyle.GetProperty("font").GetProperty("color").GetProperty("Rgb").GetUInt32() == 0xFF0000 && savedStyle.GetProperty("fill").GetProperty("fg").GetProperty("Rgb").GetUInt32() == 0x00FF00 && savedStyle.GetProperty("align").GetProperty("wrap").GetBoolean(), "Cell formatting persists");
    Check(reopened.GetProperty("merges").GetArrayLength() == 1, "Merge persists");
}
finally { File.Delete(formatPath); }
await Run("home.rowHeight", new { rows = "2:2", height = 45.0 });
await Run("home.columnWidth", new { cols = "B:B", width = 140.0 });
await Run("sheet.tabColor", new { sheet = "Sheet1", color = "#123456" });
JsonElement dimensions = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "B2" } });
Check(dimensions.GetProperty("regions")[0].GetProperty("rowHeights")[0].GetDouble() == 45 && dimensions.GetProperty("regions")[0].GetProperty("columnWidths")[0].GetDouble() == 140, "Dimensions applied");
await Run("edit.undo", new { });
Check((await Run("document.inspect", new { })).GetProperty("sheets")[0].GetProperty("tabColor").ValueKind == JsonValueKind.Null, "Undo tab color");
await Run("edit.redo", new { });
string dimensionsPath = Path.Combine(Path.GetTempPath(), $"chlorella-dimensions-{Guid.NewGuid():N}.xlsx");
try
{
    await Run("file.saveAs", new { path = dimensionsPath });
    await Run("file.open", new { path = dimensionsPath });
    dimensions = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "B2" } });
    Check(dimensions.GetProperty("regions")[0].GetProperty("rowHeights")[0].GetDouble() == 45 && dimensions.GetProperty("regions")[0].GetProperty("columnWidths")[0].GetDouble() == 140, "Dimensions persist");
    Check((await Run("document.inspect", new { })).GetProperty("sheets")[0].GetProperty("tabColor").GetString() == "#123456", "Tab color persists");
    await Run("home.autofitColumnWidth", new { cols = "B:B" });
    await Run("home.autofitRowHeight", new { rows = "2:2" });
    dimensions = await engine.SendAsync("sheet.viewport", new { ranges = new[] { "B2" } });
    Check(dimensions.GetProperty("regions")[0].GetProperty("rowHeights")[0].GetDouble() >= 24 && dimensions.GetProperty("regions")[0].GetProperty("columnWidths")[0].GetDouble() >= 20, "Autofit dimensions");
}
finally { File.Delete(dimensionsPath); }
WorksheetEngine inserted = new();
async Task<JsonElement> InsertRun(string command, object args) => await inserted.SendAsync("engine.execute", new { command, @params = args });
await InsertRun("cell.set", new { cell = "A2", input = "10" });
await InsertRun("cell.set", new { cell = "B1", input = "=$A$2+A2" });
await InsertRun("home.rowHeight", new { rows = "2:2", height = 40 });
await InsertRun("home.insertRows", new { rows = "2:2" });
JsonElement insertedView = await inserted.SendAsync("sheet.viewport", new { ranges = new[] { "A3:B3" } });
Check(insertedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "10" && insertedView.GetProperty("regions")[0].GetProperty("rowHeights")[0].GetDouble() == 40, "Insert row moves cell and height");
await InsertRun("selection.set", new { range = "B1" });
Check((await inserted.SendAsync("sheet.viewport", new { ranges = new[] { "B1" } })).GetProperty("input").GetString() == "=$A$3+A3", "Insert row updates absolute and relative references");
await InsertRun("home.insertColumns", new { cols = "A:A" });
await InsertRun("selection.set", new { range = "C1" });
Check((await inserted.SendAsync("sheet.viewport", new { ranges = new[] { "C1" } })).GetProperty("input").GetString() == "=$B$3+B3", "Insert column moves formula and references");
await InsertRun("edit.undo", new { });
await InsertRun("selection.set", new { range = "B1" });
Check((await inserted.SendAsync("sheet.viewport", new { ranges = new[] { "B1" } })).GetProperty("input").GetString() == "=$A$3+A3", "Undo insert column");
WorksheetEngine protectedEngine = new();
async Task<JsonElement> ProtectedRun(string command, object args) => await protectedEngine.SendAsync("engine.execute", new { command, @params = args });
await ProtectedRun("cell.set", new { cell = "A1", input = "kept" });
await ProtectedRun("review.protectSheet", new { sheet = "Sheet1", password = "test" });
bool editBlocked = false;
try { await ProtectedRun("cell.set", new { cell = "A1", input = "lost" }); } catch (InvalidOperationException) { editBlocked = true; }
Check(editBlocked, "Protected sheet blocks edits");
string protectedPath = Path.Combine(Path.GetTempPath(), $"chlorella-protected-{Guid.NewGuid():N}.xlsx");
try
{
    await ProtectedRun("file.saveAs", new { path = protectedPath });
    await ProtectedRun("file.open", new { path = protectedPath });
    Check((await ProtectedRun("document.inspect", new { })).GetProperty("sheets")[0].GetProperty("protected").GetBoolean(), "Sheet protection persists");
    bool wrongPassword = false;
    try { await ProtectedRun("review.unprotectSheet", new { sheet = "Sheet1", password = "wrong" }); } catch (InvalidOperationException) { wrongPassword = true; }
    Check(wrongPassword, "Wrong password rejected");
    await ProtectedRun("review.unprotectSheet", new { sheet = "Sheet1", password = "test" });
    await ProtectedRun("cell.set", new { cell = "A1", input = "changed" });
    Check((await protectedEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1" } })).GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "changed", "Unprotected sheet accepts edits");
}
finally { File.Delete(protectedPath); }
string externalPath = Path.Combine(Path.GetTempPath(), $"chlorella-external-{Guid.NewGuid():N}.xlsx");
try
{
    using (FileStream stream = File.Create(externalPath))
    using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
    {
        void Entry(string name, string value) { using StreamWriter writer = new(zip.CreateEntry(name).Open()); writer.Write(value); }
        Entry("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Data' sheetId='4' r:id='rId9'/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId9' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/custom.xml'/></Relationships>");
        Entry("xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><r><t>日本</t></r><r><t>語</t></r></si></sst>");
        Entry("xl/worksheets/custom.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1'><v>2</v></c><c r='C1'><f>B1*2</f><v>4</v></c></row></sheetData><mergeCells><mergeCell ref='A2:B2'/></mergeCells></worksheet>");
    }
    WorksheetEngine importedEngine = new();
    async Task<JsonElement> ImportRun(string command, object args) => await importedEngine.SendAsync("engine.execute", new { command, @params = args });
    Check((await ImportRun("file.open", new { path = externalPath })).GetProperty("imported").GetBoolean(), "External workbook imported");
    JsonElement view = await importedEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C2" } });
    Check(view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "日本語" && view.GetProperty("regions")[0].GetProperty("cells")[0][2].GetProperty("text").GetString() == "4" && view.GetProperty("merges").GetArrayLength() == 1, "External cells and merges");
    bool sourceProtected = false;
    try { await ImportRun("file.saveAs", new { path = externalPath }); } catch (InvalidOperationException) { sourceProtected = true; }
    Check(sourceProtected, "Imported workbook source cannot be overwritten");
}
finally { File.Delete(externalPath); }
WorksheetEngine drawingEngine = new();
async Task<JsonElement> DrawingRun(string command, object args) => await drawingEngine.SendAsync("engine.execute", new { command, @params = args });
WorksheetEngine lineEngine = new();
async Task<JsonElement> LineRun(string command, object args) => await lineEngine.SendAsync("engine.execute", new { command, @params = args });
Check(DrawingGeometry.SnapRotation(22, true) == 15 && DrawingGeometry.SnapRotation(23, true) == 30 &&
    DrawingGeometry.SnapRotation(-8, true) == -15 && DrawingGeometry.SnapRotation(23, false) == 23 &&
    DrawingGeometry.StepRotation(17, 1, true) == 30 && DrawingGeometry.StepRotation(17, -1, true) == 15 &&
    DrawingGeometry.StepRotation(30, -1, true) == 15 && DrawingGeometry.StepRotation(17, 1, false) == 18,
    "Shift rotation snaps to 15-degree steps in both directions");
(double rotatedX, double rotatedY) = DrawingGeometry.RotatePoint(10, 20, 100, 40, 0, 0, 90);
Check(Math.Abs(rotatedX - 80) < 1e-9 && Math.Abs(rotatedY + 10) < 1e-9, "Rotated line endpoint stays centered on its shape");
(double lineLength, double lineAngle) = DrawingGeometry.LineDirection(3, 4, true);
Check(lineLength == 5 && Math.Abs(lineAngle + 53.13010235415598) < 1e-9,
    "Line hit target follows the flipped line instead of its bounding box");
Check(DrawingGeometry.MovedEndpointIndex(101, 100, 0) == 1 && DrawingGeometry.MovedEndpointIndex(99, 100, 1) == 0 &&
    DrawingGeometry.MovedEndpointIndex(100, 100, 1) == 1, "Keyboard focus follows an endpoint across the fixed end");
foreach ((double width, double height) in new[] { (160d, 100d), (80d, .01d), (.01d, 90d) })
foreach (bool flip in new[] { false, true })
foreach (double angle in new[] { 0d, 37d, 90d, 180d, 270d })
{
    (double length, double direction) = DrawingGeometry.LineDirection(width, height, flip);
    double radians = (angle + direction) * Math.PI / 180;
    double centerX = 10 + width / 2, centerY = 20 + height / 2;
    (double startX, double startY) = DrawingGeometry.RotatePoint(10, 20, width, height, 0, flip ? height : 0, angle);
    (double endX, double endY) = DrawingGeometry.RotatePoint(10, 20, width, height, width, flip ? 0 : height, angle);
    Check(Math.Abs(startX - (centerX - length / 2 * Math.Cos(radians))) < 1e-8 &&
        Math.Abs(startY - (centerY - length / 2 * Math.Sin(radians))) < 1e-8 &&
        Math.Abs(endX - (centerX + length / 2 * Math.Cos(radians))) < 1e-8 &&
        Math.Abs(endY - (centerY + length / 2 * Math.Sin(radians))) < 1e-8,
        "Rotated line hit target and endpoint handles coincide");
}
foreach ((double x1, double y1, double x2, double y2, bool flip) in new[]
    { (10d, 20d, 90d, 80d, false), (90d, 80d, 10d, 20d, false),
      (10d, 80d, 90d, 20d, true), (90d, 20d, 10d, 80d, true) })
{
    (double left, double top, double width, double height, bool flipped) = DrawingGeometry.LineBounds(x1, y1, x2, y2);
    Check(left == 10 && top == 20 && width == 80 && height == 60 && flipped == flip,
        "Moving either endpoint across both axes preserves line geometry");
}
await LineRun("insert.shape", new { kind = "line", at = "B2" });
await LineRun("object.move", new { kind = "shape", id = 1, dx = 3, dy = 4, width = 80, height = .01, flipV = true, rotation = 0 });
JsonElement lineView = (await lineEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0];
Check(lineView.GetProperty("flipV").GetBoolean() && lineView.GetProperty("anchor").GetProperty("height").GetDouble() == .01,
    "Line endpoints allow near-horizontal lines and reverse slope");
await LineRun("edit.undo", new { });
Check(!(await lineEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0].GetProperty("flipV").GetBoolean(),
    "Line endpoint changes support undo");
await LineRun("edit.redo", new { });
string linePath = Path.Combine(Path.GetTempPath(), $"chlorella-line-{Guid.NewGuid():N}.xlsx");
try
{
    await LineRun("file.saveAs", new { path = linePath });
    await LineRun("file.open", new { path = linePath });
    Check((await lineEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0].GetProperty("flipV").GetBoolean(),
        "Near-horizontal line survives saved metadata");
    using (FileStream file = File.Open(linePath, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(file, ZipArchiveMode.Update))
    {
        foreach (ZipArchiveEntry entry in zip.Entries.Where(entry => entry.FullName.StartsWith("chlorella/")).ToArray()) entry.Delete();
        ZipArchiveEntry original = zip.GetEntry("xl/drawings/drawing1.xml") ?? throw new Exception("Missing line drawing");
        System.Xml.Linq.XDocument xml;
        using (Stream input = original.Open()) xml = System.Xml.Linq.XDocument.Load(input);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        System.Xml.Linq.XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        System.Xml.Linq.XElement transform = xml.Descendants(a + "xfrm").Single();
        transform.SetAttributeValue("flipV", null);
        transform.SetAttributeValue("flipH", "1");
        transform.Element(a + "ext")?.SetAttributeValue("cy", "0");
        xml.Descendants(xdr + "oneCellAnchor").Single().Element(xdr + "ext")?.SetAttributeValue("cy", "0");
        original.Delete();
        using Stream output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
        xml.Save(output);
    }
    WorksheetEngine nativeLineEngine = new();
    await nativeLineEngine.SendAsync("engine.execute", new { command = "file.open", @params = new { path = linePath } });
    JsonElement importedLine = (await nativeLineEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0];
    Check(importedLine.GetProperty("flipV").GetBoolean() && importedLine.GetProperty("anchor").GetProperty("height").GetDouble() == .01,
        "Native horizontal flipped line imports");
    await LineRun("shape.change", new { id = 1, type = "rectangle" });
    JsonElement converted = (await lineEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0];
    Check(!converted.GetProperty("flipV").GetBoolean() && converted.GetProperty("anchor").GetProperty("height").GetDouble() >= 4,
        "Changing a line to a shape restores valid bounds");
}
finally { File.Delete(linePath); }
WorksheetEngine roundedEngine = new();
await roundedEngine.SendAsync("engine.execute", new { command = "insert.shape", @params = new { kind = "roundedRectangle", at = "A1" } });
await roundedEngine.SendAsync("engine.execute", new { command = "shape.setText", @params = new { id = 1, text = " 一行目 \n二行目", bold = true } });
string multilinePath = Path.Combine(Path.GetTempPath(), $"chlorella-shape-text-{Guid.NewGuid():N}.xlsx");
try
{
    await roundedEngine.SendAsync("engine.execute", new { command = "file.saveAs", @params = new { path = multilinePath } });
    using (FileStream file = File.Open(multilinePath, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        foreach (ZipArchiveEntry entry in zip.Entries.Where(entry => entry.FullName.StartsWith("chlorella/")).ToArray()) entry.Delete();
    WorksheetEngine multilineEngine = new();
    await multilineEngine.SendAsync("engine.execute", new { command = "file.open", @params = new { path = multilinePath } });
    JsonElement shape = await multilineEngine.SendAsync("sheet.shape", new { id = 1 });
    Check(shape.GetProperty("text").GetString() == " 一行目 \n二行目" && shape.GetProperty("format").GetProperty("bold").GetBoolean(),
        "Native Excel drawing preserves multiline text, spaces and formatting");
}
finally { File.Delete(multilinePath); }
string roundedPdfPath = Path.Combine(Path.GetTempPath(), $"chlorella-rounded-{Guid.NewGuid():N}.pdf");
try
{
    await roundedEngine.SendAsync("engine.execute", new { command = "file.exportPdf", @params = new { path = roundedPdfPath } });
    string roundedPdf = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(roundedPdfPath));
    Check(roundedPdf.Contains(" c h\nB\n"), "Rounded rectangle uses curved corners in PDF");
}
finally { File.Delete(roundedPdfPath); }
await DrawingRun("insert.shape", new { kind = "ellipse", at = "B2" });
await DrawingRun("shape.setText", new { id = 1, text = "図形", bold = true, italic = true, underline = true,
    size = 20, color = "#12AB34" });
await DrawingRun("shape.setText", new { id = 1, text = "編集後" });
await DrawingRun("edit.undo", new { });
JsonElement shapeFormat = (await drawingEngine.SendAsync("sheet.shape", new { id = 1 })).GetProperty("format");
Check(shapeFormat.GetProperty("bold").GetBoolean() && shapeFormat.GetProperty("italic").GetBoolean() &&
    shapeFormat.GetProperty("underline").GetBoolean() && shapeFormat.GetProperty("size").GetDouble() == 20 &&
    shapeFormat.GetProperty("text_color").GetProperty("Rgb").GetUInt32() == 0x12ab34, "Shape text formatting survives undo");
await DrawingRun("chart.shapeFill", new { id = 1, color = "#123456" });
await DrawingRun("object.move", new { kind = "shape", id = 1, dx = 7, dy = 8, width = 120, height = 90 });
await DrawingRun("object.move", new { kind = "shape", id = 1, dx = 207, dy = 56, width = 120, height = 90 });
JsonElement movedShape = (await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F5" } })).GetProperty("shapes")[0].GetProperty("anchor");
Check(movedShape.GetProperty("cell").GetProperty("col").GetInt32() == 3 && movedShape.GetProperty("cell").GetProperty("row").GetInt32() == 3 && movedShape.GetProperty("dx").GetDouble() == 7 && movedShape.GetProperty("dy").GetDouble() == 8, "Moving a shape updates its anchor across cells");
await DrawingRun("edit.undo", new { });
JsonElement partialShape = (await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "C2:E5" } })).GetProperty("shapes");
Check(partialShape.GetArrayLength() == 1 && partialShape[0].GetProperty("anchor").GetProperty("viewportX").GetDouble() == -93, "Shape remains visible when its anchor is outside the viewport");
WorksheetEngine viewportEngine = new();
async Task<JsonElement> ViewportRun(string command, object args) => await viewportEngine.SendAsync("engine.execute", new { command, @params = args });
await ViewportRun("selection.set", new { range = "A1" });
await ViewportRun("home.wrapText", new { });
await ViewportRun("insert.shape", new { kind = "rectangle", at = "A2" });
double beforeWrap = (await viewportEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C20" } })).GetProperty("shapes")[0].GetProperty("anchor").GetProperty("viewportY").GetDouble();
await ViewportRun("cell.set", new { cell = "A1", input = "This text wraps onto several lines and changes the automatic height of the first row." });
double afterWrap = (await viewportEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C20" } })).GetProperty("shapes")[0].GetProperty("anchor").GetProperty("viewportY").GetDouble();
Check(afterWrap > beforeWrap, "Drawing coordinates follow recalculated row height");
await ViewportRun("edit.undo", new { });
Check((await viewportEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C20" } })).GetProperty("shapes")[0].GetProperty("anchor").GetProperty("viewportY").GetDouble() == beforeWrap, "Undo invalidates drawing layout cache");
await DrawingRun("cell.set", new { cell = "D1", input = "商品" });
await DrawingRun("cell.set", new { cell = "E1", input = "数量" });
await DrawingRun("cell.set", new { cell = "F1", input = "単価" });
await DrawingRun("insert.table", new { range = "D1:E3", header = true });
await DrawingRun("table.resize", new { cell = "D1", range = "D1:F3" });
await DrawingRun("insert.picture", new { at = "A4", path = Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/AppIcon-16.png") });
await DrawingRun("object.move", new { kind = "image", id = 2, dx = 10, dy = 20, width = 80, height = 60 });
await DrawingRun("object.move", new { kind = "image", id = 2, dx = 210, dy = 68, width = 120, height = 90 });
JsonElement movedImage = (await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("images")[0].GetProperty("anchor");
Check(movedImage.GetProperty("cell").GetProperty("col").GetInt32() == 2 && movedImage.GetProperty("cell").GetProperty("row").GetInt32() == 5 && movedImage.GetProperty("width").GetDouble() == 120, "Moving and resizing an image updates its anchor");
await DrawingRun("edit.undo", new { });
await DrawingRun("object.duplicate", new { kind = "shape", id = 1 });
await DrawingRun("edit.undo", new { });
JsonElement drawingView = await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F4" } });
Check(drawingView.GetProperty("shapes").GetArrayLength() == 1 && drawingView.GetProperty("tables").GetArrayLength() == 1 && drawingView.GetProperty("images").GetArrayLength() == 1, "Drawing and table undo");
await DrawingRun("object.transform", new { kind = "shape", id = 1, rotation = 45.0 });
await DrawingRun("edit.undo", new { });
Check((await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("shapes")[0].GetProperty("rotation").GetDouble() == 0,
    "Rotation supports undo");
await DrawingRun("edit.redo", new { });
await DrawingRun("object.transform", new { kind = "image", id = 2, rotation = 270.0 });
await DrawingRun("image.crop", new { kind = "image", id = 2, left = .1, top = .2, right = .15, bottom = .05 });
bool badCropRejected = false;
try { await DrawingRun("image.crop", new { kind = "image", id = 2, left = .6, top = 0, right = .4, bottom = 0 }); }
catch (InvalidOperationException) { badCropRejected = true; }
Check(badCropRejected && (await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } }))
    .GetProperty("images")[0].GetProperty("cropLeft").GetDouble() == .1, "Invalid crop does not alter the image");
await DrawingRun("edit.undo", new { });
Check((await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("images")[0].GetProperty("cropLeft").GetDouble() == 0, "Crop supports undo");
await DrawingRun("edit.redo", new { });
string? keptDrawing = Environment.GetEnvironmentVariable("CHLORELLA_TEST_XLSX");
string drawingPath = keptDrawing ?? Path.Combine(Path.GetTempPath(), $"chlorella-drawing-{Guid.NewGuid():N}.xlsx");
try
{
    await DrawingRun("file.saveAs", new { path = drawingPath });
    using (FileStream savedFile = File.OpenRead(drawingPath))
    using (ZipArchive workbookZip = new(savedFile, ZipArchiveMode.Read))
    {
        using StreamReader drawingXml = new(workbookZip.GetEntry("xl/drawings/drawing1.xml")?.Open() ?? throw new Exception("Native drawing missing"));
        string xml = drawingXml.ReadToEnd();
        Check(xml.Contains("oneCellAnchor") && xml.Contains("図形") && xml.Contains("blipFill") && xml.Contains("rot=\"2700000\"") && xml.Contains("l=\"10000\"") && workbookZip.GetEntry("xl/media/image1_1.png") is not null && workbookZip.GetEntry("xl/worksheets/_rels/sheet1.xml.rels") is not null, "Native drawing, rotation and crop parts");
        using StreamReader tableXml = new(workbookZip.GetEntry("xl/tables/table1.xml")?.Open() ?? throw new Exception("Native table missing"));
        string tableDefinition = tableXml.ReadToEnd();
        Check(tableDefinition.Contains("D1:F3") && tableDefinition.Contains("headerRowCount=\"1\"") && tableDefinition.Contains("商品"), "Native table preserves valid header");
    }
    await DrawingRun("file.open", new { path = drawingPath });
    drawingView = await drawingEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F4" } });
    JsonElement shape = drawingView.GetProperty("shapes")[0];
    Check(shape.GetProperty("text").GetString() == "図形" && shape.GetProperty("fill").GetProperty("Rgb").GetUInt32() == 0x123456 && shape.GetProperty("anchor").GetProperty("width").GetDouble() == 120 && drawingView.GetProperty("tables").GetArrayLength() == 1, "Drawing and table roundtrip");
    Check(drawingView.GetProperty("images")[0].GetProperty("anchor").GetProperty("width").GetDouble() == 80 && Convert.FromBase64String((await drawingEngine.SendAsync("sheet.picture", new { id = 2 })).GetProperty("base64").GetString() ?? "").Length > 8, "Picture data and bounds roundtrip");
    Check(shape.GetProperty("rotation").GetDouble() == 45 && drawingView.GetProperty("images")[0].GetProperty("rotation").GetDouble() == 270 && drawingView.GetProperty("images")[0].GetProperty("cropLeft").GetDouble() == .1, "Rotation and crop roundtrip");
    Check(shape.GetProperty("format").GetProperty("bold").GetBoolean() && shape.GetProperty("format").GetProperty("italic").GetBoolean() &&
        shape.GetProperty("format").GetProperty("underline").GetBoolean() && shape.GetProperty("format").GetProperty("size").GetDouble() == 20 &&
        shape.GetProperty("format").GetProperty("text_color").GetProperty("Rgb").GetUInt32() == 0x12ab34, "Shape text formatting roundtrips");
    string legacyPath = Path.Combine(Path.GetTempPath(), $"chlorella-legacy-text-{Guid.NewGuid():N}.xlsx");
    try
    {
        File.Copy(drawingPath, legacyPath);
        using (FileStream file = File.Open(legacyPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = zip.GetEntry("chlorella/sheet1.json") ?? throw new Exception("Missing drawing metadata");
            System.Text.Json.Nodes.JsonNode metadata;
            using (Stream input = entry.Open()) metadata = System.Text.Json.Nodes.JsonNode.Parse(input) ?? throw new Exception("Invalid drawing metadata");
            foreach (System.Text.Json.Nodes.JsonNode? item in metadata["Objects"]!.AsArray())
                foreach (string property in new[] { "TextBold", "TextItalic", "TextUnderline", "TextSize", "TextColor" }) item!.AsObject().Remove(property);
            entry.Delete();
            using (Stream output = zip.CreateEntry("chlorella/sheet1.json").Open())
                System.Text.Json.JsonSerializer.Serialize(output, metadata);
            ZipArchiveEntry marker = zip.GetEntry("chlorella-format.txt") ?? throw new Exception("Missing workbook marker");
            marker.Delete();
            using System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (ZipArchiveEntry part in zip.Entries.OrderBy(part => part.FullName, StringComparer.Ordinal))
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(part.FullName + "\0"));
                using Stream partStream = part.Open();
                using MemoryStream bytes = new();
                partStream.CopyTo(bytes);
                hash.AppendData(bytes.ToArray());
            }
            using StreamWriter writer = new(zip.CreateEntry("chlorella-format.txt").Open(), new System.Text.UTF8Encoding(false));
            writer.Write(Convert.ToHexString(hash.GetHashAndReset()));
        }
        WorksheetEngine legacyEngine = new();
        await legacyEngine.SendAsync("engine.execute", new { command = "file.open", @params = new { path = legacyPath } });
        JsonElement legacyFormat = (await legacyEngine.SendAsync("sheet.shape", new { id = 1 })).GetProperty("format");
        Check(!legacyFormat.GetProperty("bold").GetBoolean() && legacyFormat.GetProperty("size").GetDouble() == 14 &&
            legacyFormat.GetProperty("text_color").GetProperty("Rgb").GetUInt32() == 0xffffff, "Older shape metadata receives default text formatting");
    }
    finally { File.Delete(legacyPath); }
    string nativeOnlyPath = Path.Combine(Path.GetTempPath(), $"chlorella-native-{Guid.NewGuid():N}.xlsx");
    try
    {
        File.Copy(drawingPath, nativeOnlyPath);
        using (FileStream file = File.Open(nativeOnlyPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
            foreach (ZipArchiveEntry entry in zip.Entries.Where(entry => entry.FullName.StartsWith("chlorella/")).ToArray()) entry.Delete();
        WorksheetEngine nativeEngine = new();
        await nativeEngine.SendAsync("engine.execute", new { command = "file.open", @params = new { path = nativeOnlyPath } });
        JsonElement nativeView = await nativeEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } });
        Check(nativeView.GetProperty("shapes")[0].GetProperty("rotation").GetDouble() == 45 &&
            nativeView.GetProperty("images")[0].GetProperty("rotation").GetDouble() == 270 &&
            nativeView.GetProperty("images")[0].GetProperty("cropLeft").GetDouble() == .1, "Native Excel drawing import retains rotation and crop");
        JsonElement nativeFormat = nativeView.GetProperty("shapes")[0].GetProperty("format");
        Check(nativeFormat.GetProperty("bold").GetBoolean() && nativeFormat.GetProperty("italic").GetBoolean() &&
            nativeFormat.GetProperty("underline").GetBoolean() && nativeFormat.GetProperty("size").GetDouble() == 20 &&
            nativeFormat.GetProperty("text_color").GetProperty("Rgb").GetUInt32() == 0x12ab34, "Native Excel import retains shape text formatting");
    }
    finally { File.Delete(nativeOnlyPath); }
}
finally { if (keptDrawing is null) File.Delete(drawingPath); }
WorksheetEngine edgeCropEngine = new();
async Task<JsonElement> EdgeCropRun(string command, object args) => await edgeCropEngine.SendAsync("engine.execute", new { command, @params = args });
await EdgeCropRun("insert.picture", new { at = "A1", path = Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/AppIcon-16.png") });
await EdgeCropRun("image.crop", new { kind = "image", id = 1, left = .9999999, top = 0, right = 0, bottom = 0 });
string edgeCropPath = Path.Combine(Path.GetTempPath(), $"chlorella-crop-edge-{Guid.NewGuid():N}.xlsx");
try
{
    await EdgeCropRun("file.saveAs", new { path = edgeCropPath });
    using FileStream file = File.OpenRead(edgeCropPath);
    using ZipArchive zip = new(file, ZipArchiveMode.Read);
    using Stream input = zip.GetEntry("xl/drawings/drawing1.xml")?.Open() ?? throw new Exception("Missing cropped image");
    System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
    System.Xml.Linq.XElement crop = System.Xml.Linq.XDocument.Load(input).Descendants(a + "srcRect").Single();
    Check((int?)crop.Attribute("l") == 99999 && (int?)crop.Attribute("r") == 0, "Near-total crop remains valid in native Excel output");
}
finally { File.Delete(edgeCropPath); }
WorksheetEngine tableEngine = new();
async Task<JsonElement> TableRun(string command, object args) => await tableEngine.SendAsync("engine.execute", new { command, @params = args });
await TableRun("insert.table", new { range = "A1:B3", header = true });
await TableRun("insert.table", new { range = "C1:D3", header = true });
await TableRun("insert.table", new { range = "A4:B6", header = false });
bool tableOverlapReported = false;
try { await TableRun("insert.table", new { range = "B3:C4", header = true }); }
catch (InvalidOperationException ex) { tableOverlapReported = ex.Message.Contains("A1:B3"); }
Check(tableOverlapReported, "Table overlap identifies the existing range");
await TableRun("selection.set", new { range = "F1:G1" });
await TableRun("home.mergeCells", new { });
await TableRun("insert.table", new { range = "F2:G2", header = true });
await TableRun("selection.set", new { range = "A1:B1" });
bool mergeInsideTableBlocked = false;
try { await TableRun("home.mergeCells", new { }); } catch (InvalidOperationException) { mergeInsideTableBlocked = true; }
Check(mergeInsideTableBlocked, "Merging inside a table is blocked");
await TableRun("selection.set", new { range = "F1:G1" });
JsonElement mergeClipboard = await TableRun("edit.copy", new { });
await TableRun("cell.set", new { cell = "A2", input = "keep" });
await TableRun("selection.set", new { range = "A2" });
bool pasteMergeBlocked = false;
try { await TableRun("edit.paste", new { text = mergeClipboard.GetProperty("text").GetString(), data = mergeClipboard.GetProperty("data").GetString() }); }
catch (InvalidOperationException) { pasteMergeBlocked = true; }
JsonElement protectedTableCell = await tableEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A2" } });
Check(pasteMergeBlocked && protectedTableCell.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "keep", "Pasting a merge into a table is rejected without changing cells");
bool mergeOverlapReported = false;
try { await TableRun("insert.table", new { range = "F1:F1", header = true }); }
catch (InvalidOperationException ex) { mergeOverlapReported = ex.Message.Contains("F1:G1"); }
Check(mergeOverlapReported, "Merged-cell overlap identifies the existing range");
string? keptTables = Environment.GetEnvironmentVariable("CHLORELLA_TEST_TABLES");
string tablePath = keptTables ?? Path.Combine(Path.GetTempPath(), $"chlorella-tables-{Guid.NewGuid():N}.xlsx");
try
{
    JsonElement save = await TableRun("file.saveAs", new { path = tablePath });
    Check(save.GetProperty("headerlessTables").GetInt32() == 3, "Invalid native headers are reported");
    using (FileStream tableFile = File.OpenRead(tablePath))
    using (ZipArchive tableZip = new(tableFile, ZipArchiveMode.Read))
    {
        using StreamReader reader = new(tableZip.GetEntry("xl/tables/table1.xml")?.Open() ?? throw new Exception("Missing table"));
        Check(reader.ReadToEnd().Contains("headerRowCount=\"0\""), "Nonconforming headers export without changing values");
    }
    await TableRun("file.open", new { path = tablePath });
    JsonElement tableView = await tableEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:D4" } });
    Check(tableView.GetProperty("tables")[0].GetProperty("header").GetBoolean() && tableView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "", "App preserves original table header and empty cell");
    string legacyPath = Path.Combine(Path.GetTempPath(), $"chlorella-legacy-overlap-{Guid.NewGuid():N}.xlsx");
    string outputPath = Path.Combine(Path.GetTempPath(), $"chlorella-legacy-output-{Guid.NewGuid():N}.xlsx");
    try
    {
        File.Copy(tablePath, legacyPath);
        using (FileStream file = File.Open(legacyPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            ZipArchiveEntry oldSheet = zip.GetEntry("xl/worksheets/sheet1.xml") ?? throw new Exception("Missing worksheet");
            System.Xml.Linq.XDocument xml;
            using (Stream stream = oldSheet.Open()) xml = System.Xml.Linq.XDocument.Load(stream);
            System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            xml.Descendants(ns + "mergeCell").Single().SetAttributeValue("ref", "A1:B1");
            oldSheet.Delete();
            using Stream replacement = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(replacement);
        }
        using (FileStream file = File.Open(legacyPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            zip.GetEntry("chlorella-format.txt")?.Delete();
            using System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(entry.FullName + "\0"));
                using Stream input = entry.Open();
                byte[] buffer = new byte[8192]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, count);
            }
            using StreamWriter marker = new(zip.CreateEntry("chlorella-format.txt").Open(), new System.Text.UTF8Encoding(false));
            marker.Write(Convert.ToHexString(hash.GetHashAndReset()));
        }
        await TableRun("file.open", new { path = legacyPath });
        bool legacyBlocked = false;
        try { await TableRun("file.saveAs", new { path = outputPath }); } catch (InvalidOperationException) { legacyBlocked = true; }
        Check(legacyBlocked && !File.Exists(outputPath) && File.Exists(legacyPath), "Overlapping legacy table cannot be saved as an invalid workbook");
    }
    finally { File.Delete(legacyPath); File.Delete(outputPath); }
}
finally { if (keptTables is null) File.Delete(tablePath); }
WorksheetEngine multiEngine = new();
async Task<JsonElement> MultiRun(string command, object args) => await multiEngine.SendAsync("engine.execute", new { command, @params = args });
await MultiRun("cell.set", new { cell = "A1", input = "First" });
await MultiRun("cell.set", new { cell = "B1", input = "Second" });
await MultiRun("insert.table", new { range = "A1:B2", header = true });
await MultiRun("insert.picture", new { at = "D1", path = Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/AppIcon-16.png") });
await MultiRun("home.insertSheet", new { });
await MultiRun("cell.set", new { cell = "B2", input = "Third" });
await MultiRun("insert.table", new { range = "B2:B3", header = true });
await MultiRun("insert.shape", new { kind = "rectangle", at = "D1" });
string? keptMulti = Environment.GetEnvironmentVariable("CHLORELLA_TEST_MULTI");
string multiPath = keptMulti ?? Path.Combine(Path.GetTempPath(), $"chlorella-multi-{Guid.NewGuid():N}.xlsx");
try
{
    await MultiRun("file.saveAs", new { path = multiPath });
    using (FileStream file = File.OpenRead(multiPath))
    using (ZipArchive workbook = new(file, ZipArchiveMode.Read))
    {
        Check(workbook.GetEntry("xl/tables/table1.xml") is not null && workbook.GetEntry("xl/tables/table2.xml") is not null && workbook.GetEntry("xl/drawings/drawing1.xml") is not null && workbook.GetEntry("xl/drawings/drawing2.xml") is not null, "Multiple sheets have distinct native table and drawing parts");
        using StreamReader first = new(workbook.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")?.Open() ?? throw new Exception("Sheet relationships missing"));
        string relationships = first.ReadToEnd();
        Check(relationships.Contains("../drawings/drawing1.xml") && relationships.Contains("../tables/table1.xml"), "Drawing and table relationships coexist on one sheet");
    }
    await MultiRun("file.open", new { path = multiPath });
    JsonElement secondSheet = await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:E4" } });
    Check(secondSheet.GetProperty("tables").GetArrayLength() == 1 && secondSheet.GetProperty("shapes").GetArrayLength() == 1, "Second sheet drawing and table roundtrip");
    string externalTables = Path.Combine(Path.GetTempPath(), $"chlorella-import-tables-{Guid.NewGuid():N}.xlsx");
    try
    {
        File.Copy(multiPath, externalTables);
        using (FileStream file = File.Open(externalTables, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            zip.GetEntry("chlorella-format.txt")?.Delete();
            foreach (ZipArchiveEntry entry in zip.Entries.Where(item => item.FullName.StartsWith("chlorella/", StringComparison.Ordinal)).ToArray()) entry.Delete();
        }
        JsonElement importedTables = await MultiRun("file.open", new { path = externalTables });
        Check(importedTables.GetProperty("importedTables").GetInt32() == 2 && importedTables.GetProperty("skippedTables").GetInt32() == 0 && importedTables.GetProperty("importedDrawings").GetInt32() == 2 && importedTables.GetProperty("skippedDrawings").GetInt32() == 0, "External workbook imports native tables and drawings on multiple sheets");
        JsonElement importedView = await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:E4" } });
        Check(importedView.GetProperty("tables").GetArrayLength() == 1 && importedView.GetProperty("shapes").GetArrayLength() == 1 && importedView.GetProperty("regions")[0].GetProperty("cells")[1][1].GetProperty("text").GetString() == "Third", "Imported table, shape and values are available");
        await MultiRun("table.resize", new { cell = "B2", range = "B2:C3" });
        Check((await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:E4" } })).GetProperty("tables")[0].GetProperty("range").GetProperty("end").GetProperty("col").GetInt32() == 2, "Imported table can be resized");
        await MultiRun("shape.setText", new { id = 1, text = "changed" });
        await MultiRun("sheet.activate", new { sheet = 0 });
        JsonElement importedPicture = await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:H8" } });
        Check(importedPicture.GetProperty("images").GetArrayLength() == 1 && Convert.FromBase64String((await multiEngine.SendAsync("sheet.picture", new { id = 1 })).GetProperty("base64").GetString() ?? "").Length > 8, "External image data is imported");
        await MultiRun("object.move", new { kind = "image", id = 1, dx = 10, dy = 5, width = 100, height = 80 });
        string converted = Path.Combine(Path.GetTempPath(), $"chlorella-converted-{Guid.NewGuid():N}.xlsx");
        try
        {
            await MultiRun("file.saveAs", new { path = converted });
            await MultiRun("file.open", new { path = converted });
            Check((await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:H8" } })).GetProperty("images")[0].GetProperty("anchor").GetProperty("width").GetDouble() == 100, "Imported image edits survive save and reload");
            await MultiRun("sheet.activate", new { sheet = 1 });
            Check((await multiEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:H8" } })).GetProperty("shapes")[0].GetProperty("text").GetString() == "changed", "Imported shape edits survive save and reload");
        }
        finally { File.Delete(converted); }
    }
    finally { File.Delete(externalTables); }
}
finally { if (keptMulti is null) File.Delete(multiPath); }
WorksheetEngine sortEngine = new();
async Task<JsonElement> SortRun(string command, object args) => await sortEngine.SendAsync("engine.execute", new { command, @params = args });
async Task<string> SortInput(string address)
{
    await SortRun("selection.set", new { range = address });
    return (await sortEngine.SendAsync("sheet.viewport", new { ranges = new[] { address } })).GetProperty("input").GetString() ?? "";
}
await SortRun("cell.set", new { cell = "A1", input = "Key" });
await SortRun("cell.set", new { cell = "B1", input = "Name" });
await SortRun("cell.set", new { cell = "C1", input = "Formula" });
await SortRun("cell.set", new { cell = "A2", input = "3" });
await SortRun("cell.set", new { cell = "B2", input = "first" });
await SortRun("cell.set", new { cell = "C2", input = "=A2*10" });
await SortRun("cell.set", new { cell = "A3", input = "1" });
await SortRun("cell.set", new { cell = "B3", input = "second" });
await SortRun("cell.set", new { cell = "A5", input = "2" });
await SortRun("cell.set", new { cell = "D2", input = "outside" });
await SortRun("selection.set", new { range = "B2" });
await SortRun("home.bold", new { });
await SortRun("insert.table", new { range = "A1:C5", header = true });
await SortRun("table.sort", new { cell = "A1", direction = "ascending" });
Check(await SortInput("A2") == "1" && await SortInput("A3") == "2" && await SortInput("A4") == "3" && await SortInput("A5") == "", "Ascending table sort moves complete rows and leaves blanks last");
Check(await SortInput("B4") == "first" && await SortInput("C4") == "=A4*10" && await SortInput("D2") == "outside", "Table sort preserves row contents, adjusts relative formula and leaves outside cells intact");
JsonElement sortedView = await sortEngine.SendAsync("sheet.viewport", new { ranges = new[] { "B4" } });
string sortedStyle = sortedView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("style").GetRawText();
Check(sortedView.GetProperty("styles").GetProperty(sortedStyle).GetProperty("font").GetProperty("bold").GetBoolean(), "Table sort moves cell formatting");
await SortRun("table.sort", new { cell = "A1", direction = "descending" });
Check(await SortInput("A2") == "3" && await SortInput("A4") == "1" && await SortInput("A5") == "", "Descending table sort keeps blanks last");
await SortRun("edit.undo", new { });
Check(await SortInput("A2") == "1" && await SortInput("A4") == "3", "Undo table sort restores rows");
await SortRun("cell.set", new { cell = "A3", input = "z" });
await SortRun("cell.set", new { cell = "A4", input = "A" });
await SortRun("table.sort", new { cell = "A1", direction = "ascending" });
Check(await SortInput("A2") == "1" && await SortInput("A3") == "A" && await SortInput("A4") == "z" && await SortInput("A5") == "", "Mixed numeric and text values sort predictably");
await SortRun("table.sort", new { cell = "A1", direction = "descending" });
Check(await SortInput("A2") == "z" && await SortInput("A3") == "A" && await SortInput("A4") == "1" && await SortInput("A5") == "", "Mixed values reverse without moving blanks first");
WorksheetEngine filterEngine = new();
async Task<JsonElement> FilterRun(string command, object args) => await filterEngine.SendAsync("engine.execute", new { command, @params = args });
await FilterRun("cell.set", new { cell = "A1", input = "Category" });
await FilterRun("cell.set", new { cell = "B1", input = "Amount" });
await FilterRun("cell.set", new { cell = "A2", input = "red" });
await FilterRun("cell.set", new { cell = "A3", input = "blue" });
await FilterRun("cell.set", new { cell = "A4", input = "red" });
await FilterRun("cell.set", new { cell = "B2", input = "1" });
await FilterRun("cell.set", new { cell = "B3", input = "keep" });
await FilterRun("cell.set", new { cell = "B4", input = "2" });
await FilterRun("insert.shape", new { kind = "rectangle", at = "D3" });
await FilterRun("insert.shape", new { kind = "rectangle", at = "D4" });
await FilterRun("insert.table", new { range = "A1:B5", header = true });
JsonElement choices = await FilterRun("table.filterChoices", new { cell = "A1" });
Check(choices.GetProperty("values").GetArrayLength() == 3 && !choices.GetProperty("active").GetBoolean(), "Filter choices include distinct values and blanks");
await FilterRun("selection.set", new { range = "B3" });
await FilterRun("table.filter", new { cell = "A1", values = new[] { "red" } });
JsonElement filtered = await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } });
JsonElement filteredHeights = filtered.GetProperty("regions")[0].GetProperty("rowHeights");
Check(filteredHeights[0].GetDouble() > 0 && filteredHeights[1].GetDouble() > 0 && filteredHeights[2].GetDouble() == 0 && filteredHeights[3].GetDouble() > 0 && filteredHeights[4].GetDouble() == 0 && filtered.GetProperty("activeCell").GetString() == "A1", "Filter hides nonmatching rows and moves selection to header");
JsonElement filteredDrawings = await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F8" } });
Check(filteredDrawings.GetProperty("shapes").GetArrayLength() == 1 && filteredDrawings.GetProperty("shapes")[0].GetProperty("id").GetUInt32() == 2 && filteredDrawings.GetProperty("shapes")[0].GetProperty("anchor").GetProperty("viewportY").GetDouble() == 48, "Objects anchored to filtered rows are hidden and later objects shift upward");
Check((await filterEngine.SendAsync("sheet.nextVisibleRow", new { row = 2, direction = 1, count = 1 })).GetProperty("row").GetInt32() == 4, "Visible-row navigation skips filtered rows");
bool hiddenEditBlocked = false;
try { await FilterRun("cell.set", new { cell = "B3", input = "lost" }); } catch (InvalidOperationException) { hiddenEditBlocked = true; }
Check(hiddenEditBlocked, "Editing a hidden row is blocked");
await FilterRun("selection.set", new { range = "B2:B4" });
bool hiddenCutBlocked = false;
try { await FilterRun("edit.cut", new { }); } catch (InvalidOperationException) { hiddenCutBlocked = true; }
Check(hiddenCutBlocked, "Cutting across hidden rows is blocked");
await FilterRun("table.filter", new { cell = "B1", values = new[] { "1" } });
JsonElement combined = (await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights");
Check(combined[1].GetDouble() > 0 && combined[2].GetDouble() == 0 && combined[3].GetDouble() == 0, "Filters on different columns combine");
await FilterRun("table.filter", new { cell = "B1", values = (string[]?)null });
string? keptFilter = Environment.GetEnvironmentVariable("CHLORELLA_TEST_FILTER");
string filterPath = keptFilter ?? Path.Combine(Path.GetTempPath(), $"chlorella-filter-{Guid.NewGuid():N}.xlsx");
try
{
    await FilterRun("file.saveAs", new { path = filterPath });
    string? keptFilteredPdf = Environment.GetEnvironmentVariable("CHLORELLA_TEST_FILTER_PDF");
    string filteredPdf = keptFilteredPdf ?? Path.Combine(Path.GetTempPath(), $"chlorella-filter-{Guid.NewGuid():N}.pdf");
    string filteredCsv = Path.Combine(Path.GetTempPath(), $"chlorella-filter-{Guid.NewGuid():N}.csv");
    try
    {
        await FilterRun("file.exportPdf", new { path = filteredPdf });
        string pdf = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(filteredPdf));
        Check(pdf.Contains("726564") && !pdf.Contains("626C7565") && !pdf.Contains("6B656570"), "PDF excludes filtered rows");
        await FilterRun("file.exportCsv", new { path = filteredCsv });
        string csv = File.ReadAllText(filteredCsv);
        Check(csv.Contains("red") && !csv.Contains("blue") && !csv.Contains("keep"), "CSV excludes filtered rows");
    }
    finally { if (keptFilteredPdf is null) File.Delete(filteredPdf); File.Delete(filteredCsv); }
    using (FileStream file = File.OpenRead(filterPath))
    using (ZipArchive zip = new(file, ZipArchiveMode.Read))
    {
        using StreamReader tableXml = new(zip.GetEntry("xl/tables/table1.xml")?.Open() ?? throw new Exception("Missing filtered table"));
        string nativeTable = tableXml.ReadToEnd();
        Check(nativeTable.Contains("filterColumn") && nativeTable.Contains("red"), "Native XLSX stores filter criteria");
        using StreamReader sheetXml = new(zip.GetEntry("xl/worksheets/sheet1.xml")?.Open() ?? throw new Exception("Missing sheet"));
        Check(sheetXml.ReadToEnd().Contains("hidden=\"1\""), "Native XLSX marks filtered rows hidden");
    }
    string importedFilterPath = Path.Combine(Path.GetTempPath(), $"chlorella-filter-import-{Guid.NewGuid():N}.xlsx");
    try
    {
        File.Copy(filterPath, importedFilterPath);
        using (FileStream file = File.Open(importedFilterPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            zip.GetEntry("chlorella-format.txt")?.Delete();
            foreach (ZipArchiveEntry entry in zip.Entries.Where(item => item.FullName.StartsWith("chlorella/", StringComparison.Ordinal)).ToArray()) entry.Delete();
        }
        WorksheetEngine importedFilter = new();
        JsonElement import = await importedFilter.SendAsync("engine.execute", new { command = "file.open", @params = new { path = importedFilterPath } });
        Check(import.GetProperty("skippedFilters").GetInt32() == 0 && (await importedFilter.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() == 0, "Native equality filter survives external XLSX import");
        using (FileStream file = File.Open(importedFilterPath, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive zip = new(file, ZipArchiveMode.Update))
        {
            ZipArchiveEntry oldTable = zip.GetEntry("xl/tables/table1.xml") ?? throw new Exception("Missing native table");
            System.Xml.Linq.XDocument table;
            using (Stream input = oldTable.Open()) table = System.Xml.Linq.XDocument.Load(input);
            System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            table.Descendants(ns + "filters").Single().ReplaceWith(new System.Xml.Linq.XElement(ns + "customFilters", new System.Xml.Linq.XElement(ns + "customFilter", new System.Xml.Linq.XAttribute("operator", "greaterThan"), new System.Xml.Linq.XAttribute("val", "1"))));
            oldTable.Delete();
            using Stream output = zip.CreateEntry("xl/tables/table1.xml").Open(); table.Save(output);
        }
        JsonElement unsupported = await importedFilter.SendAsync("engine.execute", new { command = "file.open", @params = new { path = importedFilterPath } });
        Check(unsupported.GetProperty("skippedFilters").GetInt32() == 1 && (await importedFilter.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() > 0, "Unsupported external filter is reported instead of silently hiding rows");
    }
    finally { File.Delete(importedFilterPath); }
    await FilterRun("file.open", new { path = filterPath });
    Check((await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() == 0, "Filter survives save and reopen");
    await FilterRun("table.filter", new { cell = "A1", values = (string[]?)null });
    JsonElement unfiltered = await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } });
    Check(unfiltered.GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() > 0 && unfiltered.GetProperty("regions")[0].GetProperty("cells")[2][1].GetProperty("text").GetString() == "keep", "Clearing the filter restores rows without losing values");
    await FilterRun("edit.undo", new { });
    Check((await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() == 0, "Undo restores the filter");
    await FilterRun("selection.set", new { range = "A2" });
    await FilterRun("cell.set", new { cell = "A2", input = "blue" });
    JsonElement editedFilter = await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } });
    Check(editedFilter.GetProperty("regions")[0].GetProperty("rowHeights")[1].GetDouble() == 0 && editedFilter.GetProperty("activeCell").GetString() == "A4", "Editing a filtered value recalculates visibility and moves selection");
    await FilterRun("edit.undo", new { });
    await FilterRun("table.filter", new { cell = "A1", values = new[] { "" } });
    JsonElement blanks = (await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B5" } })).GetProperty("regions")[0].GetProperty("rowHeights");
    Check(blanks[1].GetDouble() == 0 && blanks[4].GetDouble() > 0, "Filtering for blanks keeps only blank entries visible");
    await FilterRun("edit.undo", new { });
    await FilterRun("home.insertColumns", new { cols = "A:A" });
    JsonElement shiftedFilter = await filterEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C5" } });
    Check(shiftedFilter.GetProperty("regions")[0].GetProperty("rowHeights")[2].GetDouble() == 0 && shiftedFilter.GetProperty("tables")[0].GetProperty("filteredColumns")[0].GetInt32() == 1, "Column insertion moves the filter with its table");
}
finally { if (keptFilter is null) File.Delete(filterPath); }
WorksheetEngine formulaFilter = new();
async Task<JsonElement> FormulaFilterRun(string command, object args) => await formulaFilter.SendAsync("engine.execute", new { command, @params = args });
await FormulaFilterRun("cell.set", new { cell = "A1", input = "Number" });
await FormulaFilterRun("cell.set", new { cell = "C1", input = "1" });
await FormulaFilterRun("cell.set", new { cell = "A2", input = "=C1" });
await FormulaFilterRun("cell.set", new { cell = "A3", input = "1" });
await FormulaFilterRun("insert.table", new { range = "A1:A3", header = true });
await FormulaFilterRun("table.filter", new { cell = "A1", values = new[] { "1" } });
await FormulaFilterRun("selection.set", new { range = "A2" });
await FormulaFilterRun("cell.set", new { cell = "C1", input = "2" });
JsonElement changedFilterFormula = await formulaFilter.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C3" } });
Check(changedFilterFormula.GetProperty("regions")[0].GetProperty("rowHeights")[1].GetDouble() == 0 && changedFilterFormula.GetProperty("activeCell").GetString() == "A3", "Changing a referenced value recalculates the filter and active row");
await FormulaFilterRun("edit.undo", new { });
Check((await formulaFilter.SendAsync("sheet.viewport", new { ranges = new[] { "A1:C3" } })).GetProperty("regions")[0].GetProperty("rowHeights")[1].GetDouble() > 0, "Undo recalculates formula-dependent filter visibility");
WorksheetEngine thirdPartyEngine = new();
async Task<JsonElement> ThirdPartyRun(string command, object args) => await thirdPartyEngine.SendAsync("engine.execute", new { command, @params = args });
string thirdPartyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../external-xlsxwriter.xlsx"));
JsonElement thirdPartyImport = await ThirdPartyRun("file.open", new { path = thirdPartyPath });
Check(thirdPartyImport.GetProperty("importedTables").GetInt32() == 1 && thirdPartyImport.GetProperty("importedDrawings").GetInt32() == 1 && thirdPartyImport.GetProperty("skippedDrawings").GetInt32() == 0, "Third-party XLSX table and image are imported");
JsonElement thirdPartyView = await thirdPartyEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:H10" } });
JsonElement externalAnchor = thirdPartyView.GetProperty("images")[0].GetProperty("anchor");
Check(externalAnchor.GetProperty("width").GetDouble() == 32 && externalAnchor.GetProperty("height").GetDouble() == 32 && externalAnchor.GetProperty("dx").GetDouble() == 4 && externalAnchor.GetProperty("dy").GetDouble() == 3, "Two-cell image anchor retains its actual size and offset");
Check(thirdPartyView.GetProperty("tables")[0].GetProperty("header").GetBoolean() && thirdPartyView.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "Name", "Third-party table header and cells are preserved");
string croppedPath = Path.Combine(Path.GetTempPath(), $"chlorella-cropped-{Guid.NewGuid():N}.xlsx");
try
{
    File.Copy(thirdPartyPath, croppedPath);
    using (FileStream file = File.Open(croppedPath, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(file, ZipArchiveMode.Update))
    {
        ZipArchiveEntry original = zip.GetEntry("xl/drawings/drawing1.xml") ?? throw new Exception("Missing external drawing");
        System.Xml.Linq.XDocument xml;
        using (Stream input = original.Open()) xml = System.Xml.Linq.XDocument.Load(input);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        xml.Descendants(a + "blip").Single().AddAfterSelf(new System.Xml.Linq.XElement(a + "srcRect", new System.Xml.Linq.XAttribute("l", "10000")));
        original.Delete();
        using Stream output = zip.CreateEntry("xl/drawings/drawing1.xml").Open();
        xml.Save(output);
    }
    JsonElement skipped = await ThirdPartyRun("file.open", new { path = croppedPath });
    Check(skipped.GetProperty("importedTables").GetInt32() == 1 && skipped.GetProperty("importedDrawings").GetInt32() == 1 && skipped.GetProperty("skippedDrawings").GetInt32() == 0 &&
        (await thirdPartyEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:F10" } })).GetProperty("images")[0].GetProperty("cropLeft").GetDouble() == .1, "External cropped image is imported with its crop");
    using (FileStream file = File.Open(croppedPath, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(file, ZipArchiveMode.Update)) zip.GetEntry("xl/worksheets/sheet1.xml")?.Delete();
    bool invalidRejected = false;
    try { await ThirdPartyRun("file.open", new { path = croppedPath }); } catch (InvalidDataException) { invalidRejected = true; }
    Check(invalidRejected && (await thirdPartyEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:B3" } })).GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString() == "Name", "Failed import leaves the previously open workbook intact");
}
finally { File.Delete(croppedPath); }
string? keptPdf = Environment.GetEnvironmentVariable("CHLORELLA_TEST_PDF");
string pdfPath = keptPdf ?? Path.Combine(Path.GetTempPath(), $"chlorella-pdf-{Guid.NewGuid():N}.pdf");
try
{
    await DrawingRun("cell.set", new { cell = "A1", input = "日本語" });
    await DrawingRun("selection.set", new { range = "A1:B1" });
    await DrawingRun("home.mergeCells", new { });
    string imageDirectory = Environment.GetEnvironmentVariable("CHLORELLA_TEST_IMAGES") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../pngsuite"));
    string[] imagePaths = Directory.GetFiles(imageDirectory).Where(path => Path.GetExtension(path) is ".png" or ".jpg").Order().ToArray();
    Check(imagePaths.Count(path => path.EndsWith(".png")) >= 4 && imagePaths.Any(path => path.EndsWith(".jpg")), "PNG and JPEG fixtures are present");
    int column = 1;
    foreach (string imagePath in imagePaths)
        await DrawingRun("insert.picture", new { path = imagePath, at = $"{(char)('A' + column++)}10" });
    await DrawingRun("file.exportPdf", new { path = pdfPath });
    byte[] pdf = File.ReadAllBytes(pdfPath);
    string pdfText = System.Text.Encoding.ASCII.GetString(pdf);
    Check(pdfText.StartsWith("%PDF-1.4") && pdfText.Contains("%%EOF") && pdfText.Contains("/SMask") && pdfText.Contains("/Im2 Do") && pdfText.Contains("56F35F62"), "PDF includes image, transparency, and shape text");
    Check(System.Text.RegularExpressions.Regex.IsMatch(pdfText, @"-?0\.000000 1\.000000 -1\.000000 -?0\.000000") &&
        pdfText.Contains("60.000 45.000 re W n 80.000 0 0 60.000"), "PDF applies the image rotation and crop clip");
}
finally { if (keptPdf is null) File.Delete(pdfPath); }
WorksheetEngine layoutEngine = new();
async Task<JsonElement> LayoutRun(string command, object args) => await layoutEngine.SendAsync("engine.execute", new { command, @params = args });
await LayoutRun("home.columnWidth", new { cols = "A:A", width = 200 });
await LayoutRun("home.rowHeight", new { rows = "1:1", height = 40 });
await LayoutRun("cell.set", new { cell = "A1", input = "wide" });
await LayoutRun("cell.set", new { cell = "A2", input = "one\ntwo" });
await LayoutRun("cell.set", new { cell = "B2", input = "売上A1" });
await LayoutRun("selection.set", new { range = "A2" });
await LayoutRun("home.wrapText", new { });
await LayoutRun("home.bold", new { });
await LayoutRun("home.rowHeight", new { rows = "2:2", height = 50 });
await LayoutRun("cell.set", new { cell = "J1", input = "end" });
await LayoutRun("insert.shape", new { kind = "rectangle", at = "E1" });
await LayoutRun("shape.setText", new { id = 1, text = "跨ぐ" });
await LayoutRun("object.move", new { kind = "shape", id = 1, dx = 0, dy = 0, width = 300, height = 50 });
string? keptLayout = Environment.GetEnvironmentVariable("CHLORELLA_TEST_LAYOUT");
string layoutPath = keptLayout ?? Path.Combine(Path.GetTempPath(), $"chlorella-layout-{Guid.NewGuid():N}.pdf");
try
{
    await LayoutRun("file.exportPdf", new { path = layoutPath });
    string data = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(layoutPath));
    Check(data.Contains("36 776 150 30 re S"), "PDF reflects custom column width and row height");
    Check(data.Split("/Type /Page /Parent").Length - 1 == 2, "PDF paginates at column boundaries");
    Check(data.Split("8DE83050").Length - 1 == 2, "PDF repeats a clipped shape across adjacent pages");
    Check(data.Contains("6F6E65") && data.Contains("74776F") && data.Contains("2 Tr"), "PDF preserves wrapped text and bold formatting");
    Check(data.Contains("58F24E0A") && data.Contains("4131"), "PDF uses separate Japanese and Latin text runs");
    await LayoutRun("home.columnWidth", new { cols = "A:A", width = 1400 });
    await LayoutRun("file.exportPdf", new { path = layoutPath });
    data = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(layoutPath));
    Check(data.Split("/Type /Page /Parent").Length - 1 >= 3 && data.Contains("656E64"), "Oversized columns paginate without dropping later cells");
}
finally { if (keptLayout is null) File.Delete(layoutPath); }
Console.WriteLine("Engine smoke tests passed.");

// Copying complete tables preserves their structure, while partial selections remain ordinary cells.
WorksheetEngine copyTableEngine = new();
async Task<JsonElement> CopyTableRun(string command, object args) => await copyTableEngine.SendAsync("engine.execute", new { command, @params = args });
async Task<JsonElement> CopyTableView() => await copyTableEngine.SendAsync("sheet.viewport", new { ranges = new[] { "A1:H6" } });
await CopyTableRun("cell.set", new { cell = "A1", input = "Header" });
await CopyTableRun("cell.set", new { cell = "A2", input = "=B2" });
await CopyTableRun("insert.table", new { range = "A1:B2", header = true });
await CopyTableRun("selection.set", new { range = "A1:B2" });
JsonElement tableCopy = await CopyTableRun("edit.copy", new { });
await CopyTableRun("selection.set", new { range = "D3" });
await CopyTableRun("edit.paste", new { text = tableCopy.GetProperty("text").GetString(), data = tableCopy.GetProperty("data").GetString() });
JsonElement copiedTables = (await CopyTableView()).GetProperty("tables");
Check(copiedTables.GetArrayLength() == 2 && copiedTables[1].GetProperty("range").GetProperty("start").GetProperty("row").GetInt32() == 2 && copiedTables[1].GetProperty("range").GetProperty("start").GetProperty("col").GetInt32() == 3 && copiedTables[1].GetProperty("header").GetBoolean(), "Copy table structure and position");
await CopyTableRun("selection.set", new { range = "D4" });
Check((await CopyTableView()).GetProperty("input").GetString() == "=E4", "Copy table formula references");
await CopyTableRun("selection.set", new { range = "B2" });
bool overlapRejected = false;
try { await CopyTableRun("edit.paste", new { text = tableCopy.GetProperty("text").GetString(), data = tableCopy.GetProperty("data").GetString() }); } catch (InvalidOperationException) { overlapRejected = true; }
Check(overlapRejected && (await CopyTableView()).GetProperty("tables").GetArrayLength() == 2, "Reject overlapping pasted table without mutation");
await CopyTableRun("selection.set", new { range = "A1" });
JsonElement partialTableCopy = await CopyTableRun("edit.copy", new { });
await CopyTableRun("selection.set", new { range = "H1" });
await CopyTableRun("edit.paste", new { text = partialTableCopy.GetProperty("text").GetString(), data = partialTableCopy.GetProperty("data").GetString() });
Check((await CopyTableView()).GetProperty("tables").GetArrayLength() == 2, "Partial table selection copies cells only");

WorksheetEngine structuredEngine = new();
async Task<JsonElement> StructuredRun(string command, object args) => await structuredEngine.SendAsync("engine.execute", new { command, @params = args });
async Task<string?> StructuredDisplay(string address)
{
    JsonElement view = await structuredEngine.SendAsync("sheet.viewport", new { ranges = new[] { address } });
    return view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString();
}
await StructuredRun("cell.set", new { cell = "A1", input = "Price" });
await StructuredRun("cell.set", new { cell = "B1", input = "Count" });
await StructuredRun("cell.set", new { cell = "C1", input = "Total" });
await StructuredRun("cell.set", new { cell = "A2", input = "12" });
await StructuredRun("cell.set", new { cell = "B2", input = "3" });
await StructuredRun("cell.set", new { cell = "C2", input = "=[@Price]*[[#This Row],[Count]]" });
await StructuredRun("insert.table", new { range = "A1:C2", header = true });
Check(await StructuredDisplay("C2") == "36", "Structured current-row references calculate");
await StructuredRun("cell.set", new { cell = "A2", input = "20" });
Check(await StructuredDisplay("C2") == "60", "Structured references recalculate after edit");

string? estimatePath = Environment.GetEnvironmentVariable("CHLORELLA_TEST_ESTIMATE");
if (!string.IsNullOrEmpty(estimatePath))
{
    WorksheetEngine estimate = new();
    async Task<JsonElement> EstimateRun(string command, object args) => await estimate.SendAsync("engine.execute", new { command, @params = args });
    async Task<string?> EstimateDisplay(string address)
    {
        JsonElement view = await estimate.SendAsync("sheet.viewport", new { ranges = new[] { address } });
        return view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString();
    }
    await EstimateRun("file.open", new { path = estimatePath });
    Check(await EstimateDisplay("E2") == "112800" && await EstimateDisplay("E12") == "344980", "Estimate structured formulas import");
    await EstimateRun("cell.set", new { cell = "C2", input = "2" });
    Check(await EstimateDisplay("E2") == "225600" && await EstimateDisplay("E12") == "457780", "Estimate recalculates after edit");
    string saved = Path.Combine(Path.GetTempPath(), $"chlorella-estimate-{Guid.NewGuid():N}.xlsx");
    try
    {
        await EstimateRun("file.saveAs", new { path = saved });
        await EstimateRun("file.open", new { path = saved });
        Check(await EstimateDisplay("E2") == "225600" && await EstimateDisplay("E12") == "457780", "Estimate structured formulas survive save and reopen");
    }
    finally { File.Delete(saved); }
}

string totalsFixture = Path.Combine(Path.GetTempPath(), $"chlorella-totals-{Guid.NewGuid():N}.xlsx");
try
{
    WorksheetEngine source = new();
    async Task<JsonElement> SourceRun(string command, object args) => await source.SendAsync("engine.execute", new { command, @params = args });
    await SourceRun("cell.set", new { cell = "A1", input = "Amount" });
    await SourceRun("cell.set", new { cell = "A2", input = "10" });
    await SourceRun("cell.set", new { cell = "A3", input = "20" });
    await SourceRun("cell.set", new { cell = "A4", input = "=SUM(Table1[Amount])" });
    await SourceRun("insert.table", new { range = "A1:A4", header = true });
    await SourceRun("file.saveAs", new { path = totalsFixture });
    using (FileStream file = File.Open(totalsFixture, FileMode.Open, FileAccess.ReadWrite))
    using (ZipArchive zip = new(file, ZipArchiveMode.Update))
    {
        zip.GetEntry("chlorella-format.txt")?.Delete();
        ZipArchiveEntry tableEntry = zip.GetEntry("xl/tables/table1.xml") ?? throw new Exception("Missing table");
        System.Xml.Linq.XDocument table;
        using (Stream input = tableEntry.Open()) table = System.Xml.Linq.XDocument.Load(input);
        table.Root?.SetAttributeValue("totalsRowCount", "1");
        table.Root?.SetAttributeValue("totalsRowShown", "1");
        tableEntry.Delete();
        using Stream output = zip.CreateEntry("xl/tables/table1.xml").Open();
        table.Save(output);
    }
    WorksheetEngine totals = new();
    async Task<JsonElement> TotalsRun(string command, object args) => await totals.SendAsync("engine.execute", new { command, @params = args });
    async Task<JsonElement> TotalsView() => await totals.SendAsync("sheet.viewport", new { ranges = new[] { "A1:A4" } });
    await TotalsRun("file.open", new { path = totalsFixture });
    Check((await TotalsView()).GetProperty("regions")[0].GetProperty("cells")[3][0].GetProperty("text").GetString() == "30", "Imported totals row is excluded from structured column sum");
    await TotalsRun("table.sort", new { cell = "A1", direction = "descending" });
    Check((await TotalsView()).GetProperty("regions")[0].GetProperty("cells")[3][0].GetProperty("text").GetString() == "30", "Sorting leaves totals row in place");
    await TotalsRun("table.filter", new { cell = "A1", values = new[] { "10" } });
    Check((await TotalsView()).GetProperty("regions")[0].GetProperty("rowHeights")[3].GetDouble() > 0, "Filtering leaves totals row visible");
}
finally { File.Delete(totalsFixture); }

string? settlementPath = Environment.GetEnvironmentVariable("CHLORELLA_TEST_SETTLEMENT");
if (!string.IsNullOrEmpty(settlementPath))
{
    WorksheetEngine settlement = new();
    async Task<JsonElement> SettlementRun(string command, object args) => await settlement.SendAsync("engine.execute", new { command, @params = args });
    async Task<string?> SettlementDisplay(string address)
    {
        JsonElement view = await settlement.SendAsync("sheet.viewport", new { ranges = new[] { address } });
        return view.GetProperty("regions")[0].GetProperty("cells")[0][0].GetProperty("text").GetString();
    }
    await SettlementRun("file.open", new { path = settlementPath });
    for (int sheet = 0; sheet < 2; sheet++)
    {
        await SettlementRun("sheet.activate", new { sheet });
        Check(await SettlementDisplay("B22") == "65547", "Table totals exclude the totals row");
    }
    string saved = Path.Combine(Path.GetTempPath(), $"chlorella-settlement-{Guid.NewGuid():N}.xlsx");
    try
    {
        await SettlementRun("file.saveAs", new { path = saved });
        await SettlementRun("file.open", new { path = saved });
        Check(await SettlementDisplay("B22") == "65547", "Table totals survive save and reopen");
        using FileStream file = File.OpenRead(saved);
        using ZipArchive zip = new(file, ZipArchiveMode.Read);
        using Stream stream = (zip.GetEntry("xl/tables/table1.xml") ?? throw new Exception("Missing table")).Open();
        System.Xml.Linq.XDocument xml = System.Xml.Linq.XDocument.Load(stream);
        Check((string?)xml.Root?.Attribute("totalsRowCount") == "1", "Native table retains totals row");
    }
    finally { File.Delete(saved); }
}
