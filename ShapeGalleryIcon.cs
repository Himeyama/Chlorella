using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Chlorella;

static class ShapeGalleryIcon
{
    public static IconElement Create(string kind)
    {
        // Filled outlines share a 20 × 20 coordinate system and inherit the button foreground.
        string data = kind switch
        {
            "rectangle" => "F1 M 2,2 H 18 V 18 H 2 Z M 4,4 V 16 H 16 V 4 Z",
            "ellipse" => "F1 M 10,1 A 9,9 0 1 1 10,19 A 9,9 0 1 1 10,1 Z M 10,3 A 7,7 0 1 0 10,17 A 7,7 0 1 0 10,3 Z",
            "roundedRectangle" =>
                "F1 M 5,2 H 15 A 3,3 0 0 1 18,5 V 15 A 3,3 0 0 1 15,18 H 5 A 3,3 0 0 1 2,15 V 5 A 3,3 0 0 1 5,2 Z M 5,4 A 1,1 0 0 0 4,5 V 15 A 1,1 0 0 0 5,16 H 15 A 1,1 0 0 0 16,15 V 5 A 1,1 0 0 0 15,4 Z",
            "triangle" => "F1 M 10,1 L 19,18 H 1 Z M 10,5 L 4.3,16 H 15.7 Z",
            "diamond" => "F1 M 10,1 L 19,10 10,19 1,10 Z M 10,3.8 L 3.8,10 10,16.2 16.2,10 Z",
            "pentagon" => "F1 M 10,1 L 19,7.5 15.6,18 H 4.4 L 1,7.5 Z M 10,3.5 L 3.4,8.3 5.9,16 H 14.1 L 16.6,8.3 Z",
            "hexagon" => "F1 M 5,2 H 15 L 19.5,10 15,18 H 5 L 0.5,10 Z M 6.2,4 L 2.8,10 6.2,16 H 13.8 L 17.2,10 13.8,4 Z",
            "line" => "M 2,3.4 L 3.4,2 18,16.6 16.6,18 Z",
            "arrow" => "M 1,7 H 11 V 2 L 19,10 11,18 V 13 H 1 Z",
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
        };
        return (PathIcon)XamlReader.Load(
            $"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20' Data='{data}'/>");
    }
}
