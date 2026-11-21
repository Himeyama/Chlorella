using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    static readonly uint[] ThemeColors = [0xffffff, 0, 0xeeeeee, 0x44546a, 0x4472c4, 0xed7d31, 0xa5a5a5, 0xffc000, 0x5b9bd5,
        0x70ad47, 0x0563c1, 0x954f72];
    static readonly string[] FormatNames = ["General", "Number", "Currency", "Short Date", "Time", "Percentage", "Text", "Comma"];
    static bool ValidStyle(FontStyle style) => !string.IsNullOrWhiteSpace(style.Name) && style.Name.Length <= 255 &&
        double.IsFinite(style.Size) && style.Size is >= 1 and <= 409 && style.Decimals is >= 0 and <= 10 &&
        (style.Color is null or <= 0xffffff) && (style.Fill is null or <= 0xffffff) &&
        FormatNames.Contains(style.Format) && style.Horizontal is "General" or "Left" or "Center" or "Right" &&
        style.Vertical is "Top" or "Center" or "Bottom" && style.Indent is >= 0 and <= 15;

    static uint? ReadRgb(XElement? color)
    {
        string? rgb = (string?)color?.Attribute("rgb");
        if (rgb is null) return null;
        if (rgb.Length is not (6 or 8) || !uint.TryParse(rgb[^6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture,
            out uint result)) throw new InvalidDataException("色の情報が無効です。");
        return result;
    }

    static string ExcelFormat(FontStyle style)
    {
        string decimals = style.Decimals == 0 ? "" : "." + new string('0', style.Decimals);
        return style.Format switch
        {
            "Number" => "0" + decimals,
            "Comma" => "#,##0" + decimals,
            "Currency" => "\"¥\"#,##0" + decimals,
            "Percentage" => "0" + decimals + "%",
            "Short Date" => "yyyy/m/d",
            "Time" => "h:mm",
            "Text" => "@",
            _ => "General",
        };
    }

    static (string Format, int Decimals) DecodeExcelFormat(string code)
    {
        if (code == "General") return ("General", 2);
        if (code == "yyyy/m/d") return ("Short Date", 2);
        if (code == "h:mm") return ("Time", 2);
        if (code == "@") return ("Text", 2);
        foreach (string format in FormatNames) for (int decimals = 0; decimals <= 10; decimals++)
                if (ExcelFormat(DefaultFont with { Format = format, Decimals = decimals }) == code) return (format, decimals);
        throw new InvalidDataException("表示形式が無効です。");
    }

    void ApplyCellFormat(string command, JsonElement args)
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        if ((long)(bottom - top + 1) * (right - left + 1) > 100_000) throw new InvalidOperationException("書式を設定できる範囲は 100,000 セルまでです。");
        string format = command == "home.numberFormat" ? args.GetProperty("format").GetString() ?? "" : "";
        if (command == "home.numberFormat" && !FormatNames.Contains(format)) throw new InvalidOperationException("表示形式が無効です。");
        uint? color = command is "home.fontColor" or "home.fillColor" ? ParseColor(args.GetProperty("color")) : null;
        FontStyle active = Current.Fonts.GetValueOrDefault(ParseCell(Current.Active), DefaultFont);
        for (int row = top; row <= bottom; row++) for (int col = left; col <= right; col++)
            {
                (int, int) key = (row, col);
                FontStyle style = Current.Fonts.GetValueOrDefault(key, DefaultFont);
                style = command switch
                {
                    "home.fontColor" => style with { Color = color },
                    "home.fillColor" => style with { Fill = color },
                    "home.numberFormat" => style with { Format = format, Decimals = 2 },
                    "home.percent" => style with { Format = "Percentage", Decimals = 0 },
                    "home.comma" => style with { Format = "Comma", Decimals = 2 },
                    "home.increaseDecimal" => style with
                    {
                        Format = style.Format == "General" ? "Number" : style.Format,
                        Decimals = style.Format == "General" ? 1 : Math.Min(10, style.Decimals + 1)
                    },
                    "home.decreaseDecimal" => style with
                    {
                        Format = style.Format == "General" ? "Number" : style.Format,
                        Decimals = style.Format == "General" ? 0 : Math.Max(0, style.Decimals - 1)
                    },
                    "home.alignLeft" => style with { Horizontal = "Left" },
                    "home.alignCenter" => style with { Horizontal = "Center" },
                    "home.alignRight" => style with { Horizontal = "Right" },
                    "home.alignTop" => style with { Vertical = "Top" },
                    "home.alignMiddle" => style with { Vertical = "Center" },
                    "home.alignBottom" => style with { Vertical = "Bottom" },
                    "home.wrapText" => style with { Wrap = !active.Wrap },
                    "home.increaseIndent" => style with { Indent = Math.Min(15, style.Indent + 1) },
                    "home.decreaseIndent" => style with { Indent = Math.Max(0, style.Indent - 1) },
                    _ => style,
                };
                if (style == DefaultFont) Current.Fonts.Remove(key);
                else Current.Fonts[key] = style;
            }
    }

    static uint? ParseColor(JsonElement color)
    {
        if (color.ValueKind == JsonValueKind.String)
        {
            string value = color.GetString() ?? "";
            if (value is "auto" or "none") return null;
            if (value.Length == 7 && value[0] == '#' && uint.TryParse(value[1..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint rgb)) return rgb;
        }
        if (color.ValueKind == JsonValueKind.Object && color.TryGetProperty("theme",
            out JsonElement theme) && theme.TryGetInt32(out int index) && index >= 0 && index < ThemeColors.Length)
        {
            double tint = color.TryGetProperty("tint", out JsonElement t) ? t.GetDouble() : 0;
            if (!double.IsFinite(tint) || tint < -1 || tint > 1) throw new InvalidOperationException("色調が無効です。");
            uint rgb = ThemeColors[index];
            double r = ((rgb >> 16) & 255) / 255d, g = ((rgb >> 8) & 255) / 255d, b = (rgb & 255) / 255d;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), light = (max + min) / 2, delta = max - min;
            double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
            double hue = delta == 0 ? 0 : max == r ? ((g - b) / delta + 6) % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            light = tint < 0 ? light * (1 + tint) : light * (1 - tint) + tint;
            double chroma = (1 - Math.Abs(2 * light - 1)) * saturation, x = chroma * (1 - Math.Abs(hue % 2 - 1)), m = light - chroma / 2;
            (r, g, b) = hue switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x,
                chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };
            return ((uint)Math.Round((r + m) * 255) << 16) | ((uint)Math.Round((g + m) * 255) << 8) | (uint)Math.Round((b + m) * 255);
        }
        throw new InvalidOperationException("色を認識できません。");
    }

    (string Text, bool Numeric) FormatCell(Sheet sheet, (int Row, int Col) cell, FontStyle style)
    {
        (string value, bool numeric) = DisplayCell(sheet, cell);
        if (!numeric || style.Format == "General") return (value, numeric);
        if (style.Format == "Text") return (value, false);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double number) || !double.IsFinite(number)) return (value, numeric);
        string digits = new('0', style.Decimals);
        string suffix = digits.Length == 0 ? "" : "." + digits;
        switch (style.Format)
        {
            case "Number": return (number.ToString("0" + suffix, CultureInfo.CurrentCulture), true);
            case "Comma": return (number.ToString("#,##0" + suffix, CultureInfo.CurrentCulture), true);
            case "Currency": return ("¥" + number.ToString("#,##0" + suffix, CultureInfo.CurrentCulture), true);
            case "Percentage": return ((number * 100).ToString("0" + suffix, CultureInfo.CurrentCulture) + "%", true);
            case "Short Date":
                if (number < 0 || number >= 2958466) return ("#####", true);
                if (Math.Floor(number) == 60) return ("1900/2/29", true);
                DateTime date = new DateTime(1899, 12, 31).AddDays(number >= 60 ? number - 1 : number);
                return (date.ToString("yyyy/M/d", CultureInfo.InvariantCulture), true);
            case "Time":
                if (number < 0) return ("#####", true);
                return (TimeSpan.FromDays(number % 1).ToString(@"hh\:mm", CultureInfo.InvariantCulture), true);
            default: return (value, numeric);
        }
    }

    double EstimateRowHeight(Sheet sheet, (int Row, int Col) cell, FontStyle style)
    {
        double lineHeight = style.Size * 96 / 72 * 1.35;
        if (!style.Wrap) return lineHeight + 4;
        string text = FormatCell(sheet, cell, style).Text;
        double available = Math.Max(10, 90 - style.Indent * 12);
        double width = 0;
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n')
            {
                lines++;
                width = 0;
            }
            else
            {
                double next = style.Size * 96 / 72 * (char.IsAscii(c) ? 0.55 : 1);
                if (width > 0 && width + next > available)
                {
                    lines++;
                    width = 0;
                }
                width += next;
            }
            if (lines * lineHeight >= 540) return 544;
        }
        return Math.Min(544, lines * lineHeight + 4);
    }
}
