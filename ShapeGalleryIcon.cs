using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Chlorella;

static class ShapeGalleryIcon
{
    public static IconElement Create(string kind)
    {
        string? glyph = kind switch
        {
            "rectangle" => "\uE739",
            "ellipse" => "\uEA3A",
            _ => null
        };
        if (glyph is not null)
        {
            return new FontIcon
            {
                Glyph = glyph,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // Filled outlines share a 20 × 20 coordinate system and inherit the button foreground.
        string data = kind switch
        {
            "roundedRectangle" =>
                "F1 M 4,2 H 16 A 2,2 0 0 1 18,4 V 16 A 2,2 0 0 1 16,18 H 4 A 2,2 0 0 1 2,16 V 4 A 2,2 0 0 1 4,2 Z M 4,4 V 16 H 16 V 4 Z",
            "triangle" => "F1 M 10,1 L 19,18 H 1 Z M 10,5 L 4.3,16 H 15.7 Z",
            "diamond" => "F1 M 10,1 L 19,10 10,19 1,10 Z M 10,3.8 L 3.8,10 10,16.2 16.2,10 Z",
            "pentagon" => "F1 M 10,1 L 19,7.5 15.6,18 H 4.4 L 1,7.5 Z M 10,3.5 L 3.4,8.3 5.9,16 H 14.1 L 16.6,8.3 Z",
            "hexagon" => "F1 M 5,2 H 15 L 19.5,10 15,18 H 5 L 0.5,10 Z M 6.2,4 L 2.8,10 6.2,16 H 13.8 L 17.2,10 13.8,4 Z",
            "line" => "M 2,16.6 L 16.6,2 18,3.4 3.4,18 Z",
            "arrow" => "F1 M 1,7 H 11 V 2 L 19,10 11,18 V 13 H 1 Z M 3,9 V 11 H 13 V 13.2 L 16.2,10 13,6.8 V 9 Z",
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
        };
        return (PathIcon)XamlReader.Load(
            $"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20' Data='{data}'/>");
    }
}
