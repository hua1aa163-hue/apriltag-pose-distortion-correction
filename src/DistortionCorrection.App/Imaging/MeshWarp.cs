using DistortionCorrection.Models;

namespace DistortionCorrection.Imaging;

public sealed record MeshGeometryDiagnostics(
    RectangleF Bounds,
    int TriangleCount,
    int FoldedTriangleCount,
    int DegenerateTriangleCount,
    int OutsideCanvasPointCount,
    double MinimumControlPointSpacing);

public static class MeshWarp
{
    public enum PlacementMode
    {
        Auto,
        Fixed
    }

    /// <summary>
    /// 把任意输入先按“适应”缩放为完整 1920×1080，再按标定网格预畸变，
    /// 输出仍为 1920×1080 黑底画布。标定中记录的目标宽高是自然逆映射结果的外接尺寸，
    /// 不是输入裁剪区域。
    /// </summary>
    public static Bitmap WarpToRequiredCanvas(
        Image input,
        MeshCalibration calibration,
        PlacementMode placementMode = PlacementMode.Auto,
        Point? fixedTopLeft = null)
    {
        calibration.Validate();
        Rectangle rect = calibration.SourceContentRectangle;
        using Bitmap logicalContent = BitmapTools.FitToSize(input, rect.Width, rect.Height);
        byte[] source = BitmapTools.ReadBgra32(logicalContent);
        byte[] output = new byte[checked(calibration.CanvasWidth * calibration.CanvasHeight * 4)];
        for (int i = 3; i < output.Length; i += 4) output[i] = 255;

        ExpandedMesh mesh = ExpandToContentBoundary(calibration);
        if (placementMode == PlacementMode.Fixed)
        {
            RectangleF bounds = BoundsOf(mesh.Destination);
            Point target = fixedTopLeft ?? new Point(
                (calibration.CanvasWidth - calibration.ExpectedInverseMappedWidth) / 2,
                (calibration.CanvasHeight - calibration.ExpectedInverseMappedHeight) / 2);
            float dx = target.X - bounds.Left;
            float dy = target.Y - bounds.Top;
            mesh = mesh with
            {
                Destination = mesh.Destination.Select(point => new PointF(point.X + dx, point.Y + dy)).ToArray()
            };
        }
        EnsureInsideCanvas(BoundsOf(mesh.Destination), calibration.CanvasWidth, calibration.CanvasHeight);
        Rectangle outputClip = new(0, 0, calibration.CanvasWidth, calibration.CanvasHeight);
        for (int row = 0; row < mesh.Rows - 1; row++)
        {
            for (int column = 0; column < mesh.Columns - 1; column++)
            {
                int a = row * mesh.Columns + column;
                int b = a + 1;
                int c = a + mesh.Columns;
                int d = c + 1;
                RasterizeTriangle(source, rect.Width, rect.Height, output, calibration.CanvasWidth,
                    calibration.CanvasHeight, rect, outputClip, mesh.Source[a], mesh.Source[b], mesh.Source[d],
                    mesh.Destination[a], mesh.Destination[b], mesh.Destination[d]);
                RasterizeTriangle(source, rect.Width, rect.Height, output, calibration.CanvasWidth,
                    calibration.CanvasHeight, rect, outputClip, mesh.Source[a], mesh.Source[d], mesh.Source[c],
                    mesh.Destination[a], mesh.Destination[d], mesh.Destination[c]);
            }
        }
        return BitmapTools.FromBgra32(calibration.CanvasWidth, calibration.CanvasHeight, output);
    }

    public static RectangleF GetInverseMappedBounds(
        MeshCalibration calibration,
        PlacementMode placementMode = PlacementMode.Auto,
        Point? fixedTopLeft = null)
    {
        ExpandedMesh mesh = ExpandToContentBoundary(calibration);
        RectangleF bounds = BoundsOf(mesh.Destination);
        if (placementMode == PlacementMode.Fixed)
        {
            Point target = fixedTopLeft ?? new Point(
                (calibration.CanvasWidth - calibration.ExpectedInverseMappedWidth) / 2,
                (calibration.CanvasHeight - calibration.ExpectedInverseMappedHeight) / 2);
            bounds = new RectangleF(target.X, target.Y, bounds.Width, bounds.Height);
        }
        return bounds;
    }

    /// <summary>检查扩展到完整输入边界后的网格是否折叠、越界或点间距过小。</summary>
    public static MeshGeometryDiagnostics AnalyzeGeometry(MeshCalibration calibration)
    {
        calibration.Validate();
        ExpandedMesh mesh = ExpandToContentBoundary(calibration);
        RectangleF bounds = BoundsOf(mesh.Destination);
        int folded = 0;
        int degenerate = 0;
        int triangles = 0;
        double referenceSign = 0;

        for (int row = 0; row < mesh.Rows - 1; row++)
        {
            for (int column = 0; column < mesh.Columns - 1; column++)
            {
                int a = row * mesh.Columns + column;
                int b = a + 1;
                int c = a + mesh.Columns;
                int d = c + 1;
                CheckTriangle(mesh.Destination[a], mesh.Destination[b], mesh.Destination[d]);
                CheckTriangle(mesh.Destination[a], mesh.Destination[d], mesh.Destination[c]);
            }
        }

        int outside = mesh.Destination.Count(point =>
            point.X < 0 || point.Y < 0 ||
            point.X > calibration.CanvasWidth - 1 || point.Y > calibration.CanvasHeight - 1);

        double minimumSpacing = double.PositiveInfinity;
        for (int row = 0; row < calibration.Rows; row++)
        {
            for (int column = 0; column < calibration.Columns; column++)
            {
                int index = row * calibration.Columns + column;
                if (column + 1 < calibration.Columns)
                {
                    minimumSpacing = Math.Min(minimumSpacing,
                        Distance(calibration.ProjectorPoints[index], calibration.ProjectorPoints[index + 1]));
                }
                if (row + 1 < calibration.Rows)
                {
                    minimumSpacing = Math.Min(minimumSpacing,
                        Distance(calibration.ProjectorPoints[index],
                            calibration.ProjectorPoints[index + calibration.Columns]));
                }
            }
        }

        return new MeshGeometryDiagnostics(bounds, triangles, folded, degenerate, outside,
            double.IsFinite(minimumSpacing) ? minimumSpacing : 0);

        void CheckTriangle(PointF a, PointF b, PointF c)
        {
            triangles++;
            double area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(area2) < 0.5)
            {
                degenerate++;
                return;
            }
            double sign = Math.Sign(area2);
            if (referenceSign == 0) referenceSign = sign;
            else if (sign != referenceSign) folded++;
        }
    }

    private static ExpandedMesh ExpandToContentBoundary(MeshCalibration calibration)
    {
        int columns = calibration.Columns + 2;
        int rows = calibration.Rows + 2;
        Rectangle rect = calibration.SourceContentRectangle;
        var source = new PointF[columns * rows];
        var destination = new PointF[columns * rows];

        // 先填充用户配置的内部点阵，外圈随后按相邻两点线性外推到有效区边界。
        for (int row = 0; row < calibration.Rows; row++)
        {
            for (int column = 0; column < calibration.Columns; column++)
            {
                int inner = row * calibration.Columns + column;
                int expanded = (row + 1) * columns + column + 1;
                source[expanded] = calibration.SourcePoints[inner];
                destination[expanded] = calibration.ProjectorPoints[inner];
            }
        }

        for (int row = 1; row <= calibration.Rows; row++)
        {
            int first = row * columns + 1;
            int second = first + 1;
            int last = row * columns + calibration.Columns;
            int beforeLast = last - 1;
            source[row * columns] = new PointF(rect.Left, source[first].Y);
            source[row * columns + columns - 1] = new PointF(rect.Right - 1, source[last].Y);
            destination[row * columns] = Extrapolate(destination[first], destination[second],
                (source[first].X - rect.Left) / Math.Max(1e-6f, source[second].X - source[first].X));
            destination[row * columns + columns - 1] = Extrapolate(destination[last], destination[beforeLast],
                (rect.Right - 1 - source[last].X) / Math.Max(1e-6f, source[last].X - source[beforeLast].X));
        }

        for (int column = 0; column < columns; column++)
        {
            int first = columns + column;
            int second = 2 * columns + column;
            int last = calibration.Rows * columns + column;
            int beforeLast = (calibration.Rows - 1) * columns + column;
            source[column] = new PointF(source[first].X, rect.Top);
            source[(rows - 1) * columns + column] = new PointF(source[last].X, rect.Bottom - 1);
            destination[column] = Extrapolate(destination[first], destination[second],
                (source[first].Y - rect.Top) / Math.Max(1e-6f, source[second].Y - source[first].Y));
            destination[(rows - 1) * columns + column] = Extrapolate(destination[last], destination[beforeLast],
                (rect.Bottom - 1 - source[last].Y) / Math.Max(1e-6f, source[last].Y - source[beforeLast].Y));
        }

        source[0] = new PointF(rect.Left, rect.Top);
        source[columns - 1] = new PointF(rect.Right - 1, rect.Top);
        source[(rows - 1) * columns] = new PointF(rect.Left, rect.Bottom - 1);
        source[rows * columns - 1] = new PointF(rect.Right - 1, rect.Bottom - 1);
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = new PointF(
                Math.Clamp(destination[i].X, rect.Left, rect.Right - 1),
                Math.Clamp(destination[i].Y, rect.Top, rect.Bottom - 1));
        }
        return new ExpandedMesh(columns, rows, source, destination);
    }

    private static PointF Extrapolate(PointF edge, PointF inward, float factor) =>
        new(edge.X + (edge.X - inward.X) * factor, edge.Y + (edge.Y - inward.Y) * factor);

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void RasterizeTriangle(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        byte[] output,
        int outputWidth,
        int outputHeight,
        Rectangle sourceRect,
        Rectangle destinationClip,
        PointF sourceA,
        PointF sourceB,
        PointF sourceC,
        PointF destinationA,
        PointF destinationB,
        PointF destinationC)
    {
        double denominator = (destinationB.Y - destinationC.Y) * (destinationA.X - destinationC.X) +
                             (destinationC.X - destinationB.X) * (destinationA.Y - destinationC.Y);
        if (Math.Abs(denominator) < 1e-8) return;
        int minX = Math.Max(destinationClip.Left, (int)Math.Floor(Math.Min(destinationA.X, Math.Min(destinationB.X, destinationC.X))));
        int maxX = Math.Min(destinationClip.Right - 1, (int)Math.Ceiling(Math.Max(destinationA.X, Math.Max(destinationB.X, destinationC.X))));
        int minY = Math.Max(destinationClip.Top, (int)Math.Floor(Math.Min(destinationA.Y, Math.Min(destinationB.Y, destinationC.Y))));
        int maxY = Math.Min(destinationClip.Bottom - 1, (int)Math.Ceiling(Math.Max(destinationA.Y, Math.Max(destinationB.Y, destinationC.Y))));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double wA = ((destinationB.Y - destinationC.Y) * (px - destinationC.X) +
                             (destinationC.X - destinationB.X) * (py - destinationC.Y)) / denominator;
                double wB = ((destinationC.Y - destinationA.Y) * (px - destinationC.X) +
                             (destinationA.X - destinationC.X) * (py - destinationC.Y)) / denominator;
                double wC = 1 - wA - wB;
                if (wA < -1e-5 || wB < -1e-5 || wC < -1e-5) continue;
                double sourceX = wA * sourceA.X + wB * sourceB.X + wC * sourceC.X - sourceRect.Left;
                double sourceY = wA * sourceA.Y + wB * sourceB.Y + wC * sourceC.Y - sourceRect.Top;
                Color color = BitmapTools.SampleBilinear(source, sourceWidth, sourceHeight, sourceX, sourceY);
                int index = (y * outputWidth + x) * 4;
                output[index] = color.B;
                output[index + 1] = color.G;
                output[index + 2] = color.R;
                output[index + 3] = 255;
            }
        }
    }

    private static RectangleF BoundsOf(IReadOnlyList<PointF> points)
    {
        float minX = points.Min(point => point.X);
        float maxX = points.Max(point => point.X);
        float minY = points.Min(point => point.Y);
        float maxY = points.Max(point => point.Y);
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    private static void EnsureInsideCanvas(RectangleF bounds, int canvasWidth, int canvasHeight)
    {
        const float tolerance = 0.51f;
        if (bounds.Left < -tolerance || bounds.Top < -tolerance ||
            bounds.Right > canvasWidth - 1 + tolerance || bounds.Bottom > canvasHeight - 1 + tolerance)
        {
            throw new InvalidDataException(
                $"预扭曲结果会越出 {canvasWidth}×{canvasHeight} 画布并被裁切：" +
                $"left={bounds.Left:F1}, top={bounds.Top:F1}, right={bounds.Right:F1}, bottom={bounds.Bottom:F1}。" +
                "请调整固定位置或重新标定较小的目标尺寸。");
        }
    }

    private sealed record ExpandedMesh(int Columns, int Rows, PointF[] Source, PointF[] Destination);
}
