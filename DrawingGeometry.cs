namespace Chlorella;

static class DrawingGeometry
{
    public static double SnapRotation(double angle, bool snap) => snap ? Math.Round(angle / 15, MidpointRounding.AwayFromZero) * 15 : angle;

    public static double StepRotation(double angle, int direction, bool snap) => snap
        ? (direction > 0 ? Math.Floor(angle / 15) + 1 : Math.Ceiling(angle / 15) - 1) * 15
        : angle + direction;

    public static (double Length, double Angle) LineDirection(double width, double height, bool flipV) =>
        (Math.Sqrt(width * width + height * height), Math.Atan2(flipV ? -height : height, width) * 180 / Math.PI);

    public static (double X, double Y) RotatePoint(double x, double y, double width, double height, double px, double py, double angle)
    {
        double radians = angle * Math.PI / 180, cosine = Math.Cos(radians), sine = Math.Sin(radians);
        double rx = px - width / 2, ry = py - height / 2;
        return (x + width / 2 + rx * cosine - ry * sine, y + height / 2 + rx * sine + ry * cosine);
    }

    public static (double Left, double Top, double Width, double Height, bool FlipV) LineBounds(
        double x1, double y1, double x2, double y2) =>
        (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(.01, Math.Abs(x1 - x2)), Math.Max(.01, Math.Abs(y1 - y2)),
            (x1 - x2) * (y1 - y2) < 0);

    public static int MovedEndpointIndex(double movedX, double fixedX, int originalIndex) =>
        movedX < fixedX ? 0 : movedX > fixedX ? 1 : originalIndex;
}
