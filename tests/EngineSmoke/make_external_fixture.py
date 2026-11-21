"""Regenerate external-xlsxwriter.xlsx using the independently maintained XlsxWriter library."""

from pathlib import Path
import xlsxwriter

here = Path(__file__).resolve().parent
icon = here.parents[1] / "Assets" / "AppIcon-16.png"
with xlsxwriter.Workbook(here / "external-xlsxwriter.xlsx") as workbook:
    sheet = workbook.add_worksheet("Data")
    sheet.set_column("A:A", 20)
    sheet.set_row(0, 30)
    sheet.add_table("A1:B3", {
        "data": [["alpha", 3], ["beta", 4]],
        "columns": [{"header": "Name"}, {"header": "Count"}],
    })
    sheet.insert_image("D4", icon, {
        "x_offset": 4, "y_offset": 3, "x_scale": 2, "y_scale": 2,
    })
