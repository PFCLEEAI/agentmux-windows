namespace AgentMux.Win.App.Controls;

internal static class TerminalPaneSizeCalculator
{
    private const double DefaultCellWidth = 8.0;
    private const double DefaultCellHeight = 17.0;
    private const int MinCols = 20;
    private const int MinRows = 5;
    private const int MaxCols = 500;
    private const int MaxRows = 200;

    public static bool TryCalculate(double width, double height, out int cols, out int rows)
    {
        cols = 0;
        rows = 0;

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return false;
        }

        return TryNormalize((int)Math.Floor(width / DefaultCellWidth), (int)Math.Floor(height / DefaultCellHeight), out cols, out rows);
    }

    public static bool TryNormalize(int cols, int rows, out int normalizedCols, out int normalizedRows)
    {
        normalizedCols = 0;
        normalizedRows = 0;

        if (cols <= 0 || rows <= 0)
        {
            return false;
        }

        normalizedCols = Math.Clamp(cols, MinCols, MaxCols);
        normalizedRows = Math.Clamp(rows, MinRows, MaxRows);
        return true;
    }
}
