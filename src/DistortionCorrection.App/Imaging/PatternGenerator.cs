using DistortionCorrection.Models;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DistortionCorrection.Imaging;

public enum GrayCodeAxis
{
    None,
    X,
    Y
}

public sealed record CalibrationPattern(
    string Name,
    Bitmap Image,
    GrayCodeAxis Axis = GrayCodeAxis.None,
    int Bit = -1,
    bool Inverted = false);

public static class PatternGenerator
{
    public static PointF[] CreateRegularGridPoints(AppSettings settings)
    {
        Rectangle rect = settings.SourceContentRectangle;
        float radius = settings.DotDiameter / 2f;
        float left = rect.Left + radius;
        float right = rect.Right - 1 - radius;
        float top = rect.Top + radius;
        float bottom = rect.Bottom - 1 - radius;
        var points = new PointF[settings.GridColumns * settings.GridRows];
        for (int row = 0; row < settings.GridRows; row++)
        {
            float y = settings.GridRows == 1 ? (top + bottom) / 2 : top + (bottom - top) * row / (settings.GridRows - 1f);
            for (int column = 0; column < settings.GridColumns; column++)
            {
                float x = settings.GridColumns == 1 ? (left + right) / 2 : left + (right - left) * column / (settings.GridColumns - 1f);
                points[row * settings.GridColumns + column] = new PointF(x, y);
            }
        }
        return points;
    }

    public static Bitmap CreateDotPattern(AppSettings settings, IReadOnlyList<PointF>? projectorPoints = null)
    {
        projectorPoints ??= CreateRegularGridPoints(settings);
        if (projectorPoints.Count != settings.GridColumns * settings.GridRows)
        {
            throw new ArgumentException("点阵控制点数量不正确。", nameof(projectorPoints));
        }

        var bitmap = NewBlackCanvas(settings);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float diameter = settings.DotDiameter;
        float radius = diameter / 2f;
        foreach (PointF point in projectorPoints)
        {
            graphics.FillEllipse(Brushes.White, point.X - radius, point.Y - radius, diameter, diameter);
        }
        return bitmap;
    }

    /// <summary>
    /// 使用与任意图片批处理完全相同的 MeshWarp 路径生成校正点阵。
    /// 规则点阵先画在完整 1920×1080 输入画布上，再执行标定网格逆映射，
    /// 避免“点阵直接画控制点、普通图片走三角网格”造成的显示差异。
    /// </summary>
    public static Bitmap CreateWarpedDotPattern(AppSettings settings, MeshCalibration calibration)
    {
        settings.Validate();
        calibration.Validate();
        if (settings.GridColumns != calibration.Columns || settings.GridRows != calibration.Rows)
            throw new InvalidOperationException("点阵参数与标定网格行列数不一致。");
        using Bitmap regular = CreateDotPattern(settings);
        return MeshWarp.WarpToRequiredCanvas(regular, calibration, MeshWarp.PlacementMode.Auto);
    }

    public static IReadOnlyList<CalibrationPattern> CreateGrayCodeSequence(AppSettings settings)
    {
        settings.Validate();
        Rectangle rect = settings.CanvasRectangle;
        int cellsX = DivideRoundUp(rect.Width, settings.GrayCodeCellSize);
        int cellsY = DivideRoundUp(rect.Height, settings.GrayCodeCellSize);
        int bitsX = BitsRequired(cellsX);
        int bitsY = BitsRequired(cellsY);
        var patterns = new List<CalibrationPattern>(2 + 2 * (bitsX + bitsY))
        {
            new("00-black", NewBlackCanvas(settings)),
            new("01-white", CreateReference(settings, white: true))
        };

        AddAxisPatterns(patterns, settings, GrayCodeAxis.X, cellsX, bitsX);
        AddAxisPatterns(patterns, settings, GrayCodeAxis.Y, cellsY, bitsY);
        return patterns;
    }

    public static void SavePatterns(IEnumerable<CalibrationPattern> patterns, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (CalibrationPattern pattern in patterns)
        {
            string path = Path.Combine(directory, pattern.Name + ".png");
            pattern.Image.Save(path, ImageFormat.Png);
        }
    }

    private static void AddAxisPatterns(
        ICollection<CalibrationPattern> output,
        AppSettings settings,
        GrayCodeAxis axis,
        int cellCount,
        int bitCount)
    {
        for (int bit = bitCount - 1; bit >= 0; bit--)
        {
            string axisName = axis == GrayCodeAxis.X ? "x" : "y";
            output.Add(new CalibrationPattern(
                $"{output.Count:D2}-{axisName}-bit{bit:D2}-normal",
                CreateGrayBit(settings, axis, bit, inverted: false), axis, bit, false));
            output.Add(new CalibrationPattern(
                $"{output.Count:D2}-{axisName}-bit{bit:D2}-inverse",
                CreateGrayBit(settings, axis, bit, inverted: true), axis, bit, true));
        }
    }

    private static Bitmap CreateReference(AppSettings settings, bool white)
    {
        Bitmap bitmap = NewBlackCanvas(settings);
        if (!white) return bitmap;
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.FillRectangle(Brushes.White, settings.CanvasRectangle);
        return bitmap;
    }

    private static Bitmap CreateGrayBit(AppSettings settings, GrayCodeAxis axis, int bit, bool inverted)
    {
        Bitmap bitmap = NewBlackCanvas(settings);
        Rectangle rect = settings.CanvasRectangle;
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        int length = axis == GrayCodeAxis.X ? rect.Width : rect.Height;
        for (int position = 0; position < length; position += settings.GrayCodeCellSize)
        {
            int cell = position / settings.GrayCodeCellSize;
            int gray = cell ^ (cell >> 1);
            bool on = ((gray >> bit) & 1) != 0;
            if (inverted) on = !on;
            if (!on) continue;
            int run = Math.Min(settings.GrayCodeCellSize, length - position);
            Rectangle stripe = axis == GrayCodeAxis.X
                ? new Rectangle(rect.Left + position, rect.Top, run, rect.Height)
                : new Rectangle(rect.Left, rect.Top + position, rect.Width, run);
            graphics.FillRectangle(Brushes.White, stripe);
        }
        return bitmap;
    }

    private static Bitmap NewBlackCanvas(AppSettings settings)
    {
        var bitmap = new Bitmap(settings.CanvasWidth, settings.CanvasHeight, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        return bitmap;
    }

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
    private static int BitsRequired(int value)
    {
        int bits = 0;
        for (int maximum = Math.Max(1, value - 1); maximum > 0; maximum >>= 1) bits++;
        return Math.Max(1, bits);
    }
}
