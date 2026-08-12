namespace DistortionCorrection.Imaging;

public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != checked(width * height)) throw new ArgumentException("灰度数据尺寸不匹配。", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public byte this[int x, int y] => Pixels[checked(y * Width + x)];
}
