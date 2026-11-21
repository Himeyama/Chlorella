using System.Globalization;
using System.Text;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static void PdfShape(StringBuilder content, DrawingObject item, double x, double top, double width, double height)
    {
        double y = top - height;
        content.Append("q 1 w\n");
        if (item.Fill is uint fill) SetColor(content, fill, false);
        if (item.Line is uint line) SetColor(content, line, true);
        void Point(double px, double py, string op) => content.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2}\n", x + width * px, y + height * py, op);
        switch (item.Type)
        {
            case "Ellipse":
                double k = 0.55228475;
                content.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} m {0:F3} {2:F3} {3:F3} {4:F3} {5:F3} {4:F3} c {6:F3} {4:F3} {7:F3} {2:F3} {7:F3} {1:F3} c {7:F3} {8:F3} {6:F3} {9:F3} {5:F3} {9:F3} c {3:F3} {9:F3} {0:F3} {8:F3} {0:F3} {1:F3} c h\n", x, y + height / 2, y + height * (1 + k) / 2, x + width * (1 - k) / 2, y + height, x + width / 2, x + width * (1 + k) / 2, x + width, y + height * (1 - k) / 2, y);
                break;
            case "Triangle": Point(.5, 1, "m"); Point(1, 0, "l"); Point(0, 0, "l"); content.Append("h\n"); break;
            case "Diamond": Point(.5, 1, "m"); Point(1, .5, "l"); Point(.5, 0, "l"); Point(0, .5, "l"); content.Append("h\n"); break;
            case "Pentagon": Point(.5, 1, "m"); Point(1, .62, "l"); Point(.8, 0, "l"); Point(.2, 0, "l"); Point(0, .62, "l"); content.Append("h\n"); break;
            case "Hexagon": Point(.25, 1, "m"); Point(.75, 1, "l"); Point(1, .5, "l"); Point(.75, 0, "l"); Point(.25, 0, "l"); Point(0, .5, "l"); content.Append("h\n"); break;
            case "Arrow": Point(0, .65, "m"); Point(.65, .65, "l"); Point(.65, 1, "l"); Point(1, .5, "l"); Point(.65, 0, "l"); Point(.65, .35, "l"); Point(0, .35, "l"); content.Append("h\n"); break;
            case "Line": Point(0, 1, "m"); Point(1, 0, "l"); break;
            default: content.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} {3:F3} re\n", x, y, width, height); break;
        }
        content.Append(item.Type == "Line" ? (item.Line.HasValue ? "S\n" : "n\n") : item.Fill.HasValue && item.Line.HasValue ? "B\n" : item.Fill.HasValue ? "f\n" : item.Line.HasValue ? "S\n" : "n\n");
        content.Append("Q\n");
        if (item.Text.Length > 0) PdfText(content, item.Text.Replace('\r', ' ').Replace('\n', ' '), x, top - Math.Max(0, height / 2 - 7), width, Math.Min(16, height), 0xffffff, 10);
    }
}
