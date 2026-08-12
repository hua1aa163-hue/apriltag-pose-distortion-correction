using DistortionCorrection.Imaging;
using DistortionCorrection.Models;

namespace DistortionCorrection.Calibration;

public sealed class GrayCodeAccumulator
{
    private readonly AppSettings _settings;
    private readonly GrayImage _black;
    private readonly GrayImage _white;
    private readonly ushort[] _grayX;
    private readonly ushort[] _grayY;
    private readonly byte[] _minimumPairContrast;
    private int _pairsAdded;

    public GrayCodeAccumulator(AppSettings settings, GrayImage black, GrayImage white)
    {
        if (black.Width != white.Width || black.Height != white.Height) throw new ArgumentException("黑白参考帧尺寸不一致。");
        _settings = settings;
        _black = black;
        _white = white;
        int pixels = checked(black.Width * black.Height);
        _grayX = new ushort[pixels];
        _grayY = new ushort[pixels];
        _minimumPairContrast = new byte[pixels];
        Array.Fill(_minimumPairContrast, byte.MaxValue);
    }

    public void AddPair(GrayCodeAxis axis, int bit, GrayImage normal, GrayImage inverse)
    {
        if (axis is not (GrayCodeAxis.X or GrayCodeAxis.Y)) throw new ArgumentOutOfRangeException(nameof(axis));
        ValidateDimensions(normal);
        ValidateDimensions(inverse);
        int mask = checked(1 << bit);
        ushort[] codes = axis == GrayCodeAxis.X ? _grayX : _grayY;
        for (int i = 0; i < codes.Length; i++)
        {
            int difference = normal.Pixels[i] - inverse.Pixels[i];
            int contrast = Math.Abs(difference);
            if (contrast < _minimumPairContrast[i]) _minimumPairContrast[i] = (byte)Math.Min(255, contrast);
            if (difference > 0) codes[i] = (ushort)(codes[i] | mask);
        }
        _pairsAdded++;
    }

    public DenseProjectorMap Complete()
    {
        if (_pairsAdded == 0) throw new InvalidOperationException("尚未加入任何 Gray Code 图卡对。");
        Rectangle rect = _settings.CanvasRectangle;
        int cellsX = (rect.Width + _settings.GrayCodeCellSize - 1) / _settings.GrayCodeCellSize;
        int cellsY = (rect.Height + _settings.GrayCodeCellSize - 1) / _settings.GrayCodeCellSize;
        int cells = checked(cellsX * cellsY);
        long[] sumX = new long[cells];
        long[] sumY = new long[cells];
        int[] count = new int[cells];

        for (int y = 0; y < _black.Height; y++)
        {
            int row = y * _black.Width;
            for (int x = 0; x < _black.Width; x++)
            {
                int pixel = row + x;
                int referenceContrast = _white.Pixels[pixel] - _black.Pixels[pixel];
                if (referenceContrast < _settings.MinimumGrayCodeContrast ||
                    _minimumPairContrast[pixel] < _settings.MinimumGrayCodeContrast) continue;
                int cellX = GrayToBinary(_grayX[pixel]);
                int cellY = GrayToBinary(_grayY[pixel]);
                if ((uint)cellX >= (uint)cellsX || (uint)cellY >= (uint)cellsY) continue;
                int cell = cellY * cellsX + cellX;
                sumX[cell] += x;
                sumY[cell] += y;
                count[cell]++;
            }
        }

        var cameraPoints = new PointF[cells];
        for (int i = 0; i < cells; i++)
        {
            cameraPoints[i] = count[i] == 0
                ? new PointF(float.NaN, float.NaN)
                : new PointF(sumX[i] / (float)count[i], sumY[i] / (float)count[i]);
        }

        return new DenseProjectorMap
        {
            CanvasWidth = _settings.CanvasWidth,
            CanvasHeight = _settings.CanvasHeight,
            CellSize = _settings.GrayCodeCellSize,
            CellColumns = cellsX,
            CellRows = cellsY,
            CameraWidth = _black.Width,
            CameraHeight = _black.Height,
            CameraAtProjectorCell = cameraPoints,
            SamplesPerCell = count
        };
    }

    private void ValidateDimensions(GrayImage image)
    {
        if (image.Width != _black.Width || image.Height != _black.Height)
        {
            throw new InvalidDataException("Gray Code 采集过程中相机图像尺寸发生变化。");
        }
    }

    internal static int GrayToBinary(int gray)
    {
        int binary = 0;
        for (; gray != 0; gray >>= 1) binary ^= gray;
        return binary;
    }
}
