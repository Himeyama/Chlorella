namespace Chlorella;

sealed partial class WorksheetEngine
{
    readonly Dictionary<Sheet, Dictionary<int, double>> drawingHeightCache = new();

    Dictionary<int, double> DrawingRowHeights()
    {
        if (drawingHeightCache.TryGetValue(Current, out Dictionary<int, double>? cached)) return cached;
        Dictionary<int, double> heights = Current.Fonts.GroupBy(pair => pair.Key.Row).ToDictionary(group => group.Key, group => Math.Max(24, group.Max(pair => EstimateRowHeight(Current, pair.Key, pair.Value))));
        foreach (KeyValuePair<int, double> entry in Current.RowHeights) heights[entry.Key] = Math.Max(18, entry.Value);
        drawingHeightCache[Current] = heights;
        return heights;
    }

    sealed class AxisOffsets
    {
        readonly int[] keys;
        readonly double[] cumulative;
        readonly double fallback;

        public AxisOffsets(Dictionary<int, double> dimensions, double defaultSize)
        {
            fallback = defaultSize;
            keys = dimensions.Keys.Order().ToArray();
            cumulative = new double[keys.Length + 1];
            for (int i = 0; i < keys.Length; i++) cumulative[i + 1] = cumulative[i] + dimensions[keys[i]] - fallback;
        }
        int LowerBound(int index)
        {
            int lo = 0, hi = keys.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (keys[mid] < index) lo = mid + 1; else hi = mid; }
            return lo;
        }
        public double At(int index) => index * fallback + cumulative[LowerBound(index)];
    }

    List<(DrawingObject Item, double X, double Y)> VisibleDrawingObjects(int top, int left, int bottom, int right)
    {
        if (Current.Objects.Count == 0) return [];
        Dictionary<int, double> heights = new(DrawingRowHeights());
        HashSet<int> hiddenRows = FilteredRows();
        foreach (int row in hiddenRows) heights[row] = 0;
        Dictionary<int, double> widths = Current.ColumnWidths.ToDictionary(pair => pair.Key, pair => Math.Max(20, pair.Value));
        AxisOffsets rows = new(heights, 24), columns = new(widths, 100);
        double originX = columns.At(left), originY = rows.At(top);
        double viewportWidth = columns.At(right + 1) - originX, viewportHeight = rows.At(bottom + 1) - originY;
        List<(DrawingObject Item, double X, double Y)> visible = [];
        foreach (DrawingObject item in Current.Objects)
        {
            if (hiddenRows.Contains(item.Row)) continue;
            double x = columns.At(item.Col) - originX + item.Dx, y = rows.At(item.Row) - originY + item.Dy;
            if (x < viewportWidth && x + item.Width > 0 && y < viewportHeight && y + item.Height > 0) visible.Add((item, x, y));
        }
        return visible;
    }
}
