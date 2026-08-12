using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DistortionCorrection.Imaging;

public static class BitmapTools
{
    public static GrayImage ToGrayscale(Bitmap source)
    {
        using Bitmap normalized = Normalize32(source);
        var result = new byte[checked(normalized.Width * normalized.Height)];
        var rect = new Rectangle(0, 0, normalized.Width, normalized.Height);
        BitmapData data = normalized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] row = new byte[stride];
            for (int y = 0; y < normalized.Height; y++)
            {
                IntPtr rowPointer = data.Scan0 + y * data.Stride;
                Marshal.Copy(rowPointer, row, 0, stride);
                int output = y * normalized.Width;
                for (int x = 0; x < normalized.Width; x++)
                {
                    int i = x * 4;
                    // BT.601 整数近似，白点识别与 Gray Code 解码均只关心亮度顺序。
                    result[output + x] = (byte)((29 * row[i] + 150 * row[i + 1] + 77 * row[i + 2] + 128) >> 8);
                }
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }

        return new GrayImage(normalized.Width, normalized.Height, result);
    }

    public static Bitmap Normalize32(Image source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return result;
    }

    public static Bitmap FitToSize(Image source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        double scale = Math.Min(width / (double)source.Width, height / (double)source.Height);
        int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        int x = (width - drawWidth) / 2;
        int y = (height - drawHeight) / 2;
        graphics.DrawImage(source, new Rectangle(x, y, drawWidth, drawHeight));
        return result;
    }

    /// <summary>截取输入图像正中心的指定矩形，并拉伸到完整逻辑输入画布。</summary>
    public static Bitmap ExtractCenteredAndStretch(
        Image source,
        int contentWidth,
        int contentHeight,
        int outputWidth,
        int outputHeight)
    {
        if (contentWidth < 1 || contentWidth > source.Width ||
            contentHeight < 1 || contentHeight > source.Height)
        {
            throw new InvalidDataException(
                $"居中内容尺寸 {contentWidth}×{contentHeight} 超出输入图片 " +
                $"{source.Width}×{source.Height}。");
        }
        if (outputWidth < 1 || outputHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(outputWidth), "输出尺寸必须大于 0。");

        int sourceX = (source.Width - contentWidth) / 2;
        int sourceY = (source.Height - contentHeight) / 2;
        var result = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, outputWidth, outputHeight),
            new Rectangle(sourceX, sourceY, contentWidth, contentHeight),
            GraphicsUnit.Pixel);
        return result;
    }

    public static byte[] ReadBgra32(Bitmap source)
    {
        using Bitmap normalized = Normalize32(source);
        int rowBytes = checked(normalized.Width * 4);
        byte[] pixels = new byte[checked(rowBytes * normalized.Height)];
        var rect = new Rectangle(0, 0, normalized.Width, normalized.Height);
        BitmapData data = normalized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < normalized.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }
        return pixels;
    }

    public static Bitmap FromBgra32(int width, int height, byte[] pixels)
    {
        int rowBytes = checked(width * 4);
        if (pixels.Length != checked(rowBytes * height)) throw new ArgumentException("像素数据尺寸不匹配。", nameof(pixels));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    public static Color SampleBilinear(byte[] bgra, int width, int height, double x, double y)
    {
        if (x < 0 || y < 0 || x > width - 1 || y > height - 1) return Color.Black;
        int x0 = Math.Clamp((int)Math.Floor(x), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(y), 0, height - 1);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        double fx = x - x0;
        double fy = y - y0;
        int i00 = (y0 * width + x0) * 4;
        int i10 = (y0 * width + x1) * 4;
        int i01 = (y1 * width + x0) * 4;
        int i11 = (y1 * width + x1) * 4;
        byte b = Blend(bgra[i00], bgra[i10], bgra[i01], bgra[i11], fx, fy);
        byte g = Blend(bgra[i00 + 1], bgra[i10 + 1], bgra[i01 + 1], bgra[i11 + 1], fx, fy);
        byte r = Blend(bgra[i00 + 2], bgra[i10 + 2], bgra[i01 + 2], bgra[i11 + 2], fx, fy);
        return Color.FromArgb(255, r, g, b);
    }

    private static byte Blend(byte a, byte b, byte c, byte d, double fx, double fy)
    {
        double top = a + (b - a) * fx;
        double bottom = c + (d - c) * fx;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
    }
}
