namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record CellRange(int Top, int Left, int Bottom, int Right)
    {
        public bool Intersects(CellRange other) => Top <= other.Bottom && Bottom >= other.Top && Left <= other.Right && Right >= other.Left;
        public bool Contains(int row, int col) => row >= Top && row <= Bottom && col >= Left && col <= Right;
    }

    void MergeCells()
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        CellRange range = new(top, left, bottom, right);
        if (top == bottom && left == right) throw new InvalidOperationException("結合するセルを複数選択してください。");
        if ((long)(bottom - top + 1) * (right - left + 1) > 100_000) throw new InvalidOperationException("結合できる範囲は 100,000 セルまでです。");
        if (Current.Merges.Any(m => m.Intersects(range))) throw new InvalidOperationException("結合済みのセルと重なる範囲は結合できません。");
        if (Current.Tables.Any(table => new CellRange(table.Top, table.Left, table.Bottom,
            table.Right).Intersects(range))) throw new InvalidOperationException("テーブルと重なる範囲は結合できません。テーブルを解除してから操作してください。");
        if (Current.Cells.Any(p => range.Contains(p.Key.Row, p.Key.Col) && p.Key != (top,
            left))) throw new InvalidOperationException("左上以外のセルにデータがあります。データを消去してから結合してください。");
        Current.Merges.Add(range);
        (int, int) anchor = (top, left);
        FontStyle style = Current.Fonts.GetValueOrDefault(anchor, DefaultFont);
        Current.Fonts[anchor] = style with { Horizontal = "Center", Vertical = "Center" };
    }

    void UnmergeCells()
    {
        (int top, int left, int bottom, int right) = ParseRange(Current.Selection);
        CellRange range = new(top, left, bottom, right);
        if (Current.Merges.RemoveAll(m => m.Intersects(range)) == 0) throw new InvalidOperationException("選択範囲に結合されたセルがありません。");
    }
}
