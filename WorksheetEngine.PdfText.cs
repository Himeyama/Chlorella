using System.Globalization;
using System.Text;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static readonly int[] HelveticaWidths = [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584];

    static double PdfAdvance(Rune rune,
        double size) => rune.Value is >= 32 and <= 126 ? size * HelveticaWidths[rune.Value - 32] / 1000d : size;

    static void PdfDrawText(StringBuilder content, string text, double x, double baseline, double size, bool bold, bool italic)
    {
        StringBuilder run = new();
        bool? latin = null;
        double cursor = x, runWidth = 0;
        void Flush()
        {
            if (latin is null || run.Length == 0) return;
            byte[] bytes = latin.Value ? Encoding.ASCII.GetBytes(run.ToString()) : Encoding.BigEndianUnicode.GetBytes(run.ToString());
            content.AppendFormat(CultureInfo.InvariantCulture,
                "BT /{0} {1:F3} Tf {2} Tr {3:F3} w 1 0 {4:F3} 1 {5:F3} {6:F3} Tm <{7}> Tj ET\n", latin.Value ? "F2" : "F1",
                    size, bold ? 2 : 0, Math.Max(0.15, size / 40), italic ? 0.2 : 0, cursor, baseline, Convert.ToHexString(bytes));
            cursor += runWidth;
            run.Clear();
            runWidth = 0;
        }
        foreach (Rune source in text.EnumerateRunes())
        {
            Rune rune = source.Value == '\t' ? new Rune(' ') : source;
            bool isLatin = rune.Value is >= 32 and <= 126;
            if (latin.HasValue && latin.Value != isLatin) Flush();
            latin = isLatin;
            run.Append(rune.ToString());
            runWidth += PdfAdvance(rune, size);
        }
        Flush();
    }

    static void PdfCellText(StringBuilder content, string value, double x, double top, double width, double height,
        FontStyle style, bool numeric)
    {
        double size = Math.Clamp(style.Size, 1, 409), padding = 3 + style.Indent * 9;
        double available = Math.Max(1, width - padding - 3), lineHeight = size * 1.25;
        List<(string Text, double Width)> lines = [];
        StringBuilder line = new();
        double lineWidth = 0;
        void AddLine()
        {
            lines.Add((line.ToString(), lineWidth));
            line.Clear();
            lineWidth = 0;
        }
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n')
            {
                AddLine();
                continue;
            }
            double advance = PdfAdvance(rune.Value == '\t' ? new Rune(' ') : rune, size);
            if (style.Wrap && lineWidth > 0 && lineWidth + advance > available) AddLine();
            line.Append(rune.ToString());
            lineWidth += advance;
        }
        AddLine();
        double blockHeight = lines.Count * lineHeight;
        double firstBaseline = style.Vertical switch
        {
            "Top" => top - 2 - size,
            "Bottom" => top - height + 2 + blockHeight - lineHeight * 0.8,
            _ => top - Math.Max(2, (height - blockHeight) / 2) - size,
        };
        content.AppendFormat(CultureInfo.InvariantCulture, "q {0:F3} {1:F3} {2:F3} {3:F3} re W n\n", x + 2, top - height + 1,
            Math.Max(0, width - 4), Math.Max(0, height - 2));
        uint color = style.Color ?? 0;
        SetColor(content, color, false);
        if (style.Bold) SetColor(content, color, true);
        for (int index = 0; index < lines.Count; index++)
        {
            (string text, double textWidth) = lines[index];
            double baseline = firstBaseline - index * lineHeight;
            if (baseline > top || baseline + size < top - height) continue;
            string horizontal = style.Horizontal == "General" ? (numeric ? "Right" : "Left") : style.Horizontal;
            double left = horizontal switch
            {
                "Right" => x + width - 3 - textWidth,
                "Center" => x + (width - textWidth) / 2,
                _ => x + padding,
            };
            PdfDrawText(content, text, left, baseline, size, style.Bold, style.Italic);
            if (style.Underline)
            {
                SetColor(content, color, true);
                content.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} w {1:F3} {2:F3} m {3:F3} {2:F3} l S\n",
                    Math.Max(0.4, size / 18), left, baseline - 1, left + textWidth);
            }
        }
        content.Append("Q\n");
    }
}
