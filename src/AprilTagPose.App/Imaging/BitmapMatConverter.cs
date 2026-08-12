using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace AprilTagPose.Imaging;

public static class BitmapMatConverter
{
    public static Mat ToMat(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(normalized))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        Rectangle rectangle = new(0, 0, normalized.Width, normalized.Height);
        BitmapData data = normalized.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            using Mat wrapped = Mat.FromPixelData(
                normalized.Height, normalized.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }

    public static Bitmap ToBitmap(Mat source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Empty()) throw new ArgumentException("图像为空。", nameof(source));

        using Mat bgr = NormalizeToBgr(source);
        var bitmap = new Bitmap(bgr.Width, bgr.Height, PixelFormat.Format24bppRgb);
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int sourceStride = checked((int)bgr.Step());
            int bytesPerRow = bgr.Width * 3;
            byte[] row = new byte[bytesPerRow];
            int height = bgr.Height;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(bgr.Data, y * sourceStride), row, 0, bytesPerRow);
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), bytesPerRow);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Mat NormalizeToBgr(Mat source)
    {
        using Mat eightBit = NormalizeDepthTo8(source);
        var result = new Mat();
        if (eightBit.Channels() == 3)
        {
            eightBit.CopyTo(result);
        }
        else if (eightBit.Channels() == 1)
        {
            Cv2.CvtColor(eightBit, result, ColorConversionCodes.GRAY2BGR);
        }
        else if (eightBit.Channels() == 4)
        {
            Cv2.CvtColor(eightBit, result, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            result.Dispose();
            throw new NotSupportedException($"不支持 {source.Channels()} 通道图像。");
        }

        return result;
    }

    private static Mat NormalizeDepthTo8(Mat source)
    {
        var converted = new Mat();
        int depth = source.Depth();
        if (depth == MatType.CV_8U) source.CopyTo(converted);
        else if (depth == MatType.CV_8S) source.ConvertTo(converted, MatType.CV_8U, 1d, 128d);
        else if (depth == MatType.CV_16U) source.ConvertTo(converted, MatType.CV_8U, 1d / 256d);
        else if (depth == MatType.CV_16S) source.ConvertTo(converted, MatType.CV_8U, 1d / 256d, 128d);
        else Cv2.Normalize(source, converted, 0d, 255d, NormTypes.MinMax, MatType.CV_8U);
        return converted;
    }
}
