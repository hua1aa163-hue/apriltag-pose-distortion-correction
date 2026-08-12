using System.Drawing.Drawing2D;

namespace DistortionCorrection.Imaging;

public sealed record BrightComponent(
    PointF Center,
    int Area,
    Rectangle Bounds,
    double MeanBrightness,
    double FillRatio);

public sealed class PointGridDetectionResult
{
    public required int Threshold { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required PointF[] OrderedPoints { get; init; }
    public required IReadOnlyList<BrightComponent> Components { get; init; }
    public required double AssignmentRms { get; init; }
}

/// <summary>针对黑底白点图卡的轻量检测器，不依赖外部 OpenCV 运行库。</summary>
public static class PointGridDetector
{
    /// <summary>
    /// 根据稠密映射给出的相机预测坐标，在每个预测点的小窗口内做局部亮斑质心定位。
    /// 这种方式不依赖整幅 5472×3648 图像的全局阈值，适合反射系统中亮度不均、点很小的回环图。
    /// </summary>
    public static PointGridDetectionResult DetectNearExpected(
        Bitmap bitmap,
        IReadOnlyList<PointF> expectedPoints,
        int columns,
        int rows)
    {
        int expectedCount = checked(columns * rows);
        if (expectedPoints.Count != expectedCount)
        {
            throw new ArgumentException($"预测点数量应为 {expectedCount}。", nameof(expectedPoints));
        }

        GrayImage gray = BitmapTools.ToGrayscale(bitmap);
        double minimumSpacing = MinimumGridSpacing(expectedPoints, columns, rows);
        int radius = (int)Math.Clamp(Math.Round(minimumSpacing * 0.38), 6, 48);
        var ordered = new PointF[expectedCount];
        var components = new List<BrightComponent>(expectedCount);
        double normalizedSquaredError = 0;
        int thresholdSum = 0;

        for (int index = 0; index < expectedCount; index++)
        {
            PointF expected = expectedPoints[index];
            int left = Math.Max(0, (int)Math.Floor(expected.X) - radius);
            int right = Math.Min(gray.Width - 1, (int)Math.Ceiling(expected.X) + radius);
            int top = Math.Max(0, (int)Math.Floor(expected.Y) - radius);
            int bottom = Math.Min(gray.Height - 1, (int)Math.Ceiling(expected.Y) + radius);
            if (left >= right || top >= bottom)
            {
                throw new InvalidDataException($"预测点 {index} 越出相机画面：({expected.X:F1},{expected.Y:F1})。");
            }

            int[] histogram = new int[256];
            int maximum = 0;
            int pixelCount = 0;
            for (int y = top; y <= bottom; y++)
            {
                int offset = y * gray.Width;
                for (int x = left; x <= right; x++)
                {
                    int value = gray.Pixels[offset + x];
                    histogram[value]++;
                    maximum = Math.Max(maximum, value);
                    pixelCount++;
                }
            }

            int background = Percentile(histogram, pixelCount, 0.55);
            int contrast = maximum - background;
            if (contrast < 4)
            {
                throw new InvalidDataException(
                    $"预测点 {index} 的局部对比度只有 {contrast}，请提高相机曝光/增益或检查投影亮度。");
            }
            int threshold = Math.Clamp(background + Math.Max(3, (int)Math.Round(contrast * 0.35)), 1, 254);
            thresholdSum += threshold;

            double weightSum = 0;
            double sumX = 0;
            double sumY = 0;
            double brightnessSum = 0;
            int area = 0;
            int minX = right, minY = bottom, maxX = left, maxY = top;
            for (int y = top; y <= bottom; y++)
            {
                int offset = y * gray.Width;
                for (int x = left; x <= right; x++)
                {
                    int value = gray.Pixels[offset + x];
                    if (value < threshold) continue;
                    double weight = 1 + value - threshold;
                    weightSum += weight;
                    sumX += x * weight;
                    sumY += y * weight;
                    brightnessSum += value;
                    area++;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (area < 2 || weightSum <= 0)
            {
                throw new InvalidDataException($"预测点 {index} 附近没有可用亮斑。");
            }

            var center = new PointF((float)(sumX / weightSum), (float)(sumY / weightSum));
            ordered[index] = center;
            double dx = center.X - expected.X;
            double dy = center.Y - expected.Y;
            normalizedSquaredError += (dx * dx + dy * dy) / Math.Max(1, minimumSpacing * minimumSpacing);
            int boundsWidth = maxX - minX + 1;
            int boundsHeight = maxY - minY + 1;
            components.Add(new BrightComponent(center, area,
                new Rectangle(minX, minY, boundsWidth, boundsHeight),
                brightnessSum / Math.Max(1, area),
                area / (double)Math.Max(1, boundsWidth * boundsHeight)));
        }

        return new PointGridDetectionResult
        {
            Threshold = thresholdSum / expectedCount,
            Columns = columns,
            Rows = rows,
            OrderedPoints = ordered,
            Components = components,
            AssignmentRms = Math.Sqrt(normalizedSquaredError / expectedCount)
        };
    }

    public static PointGridDetectionResult Detect(Bitmap bitmap, int columns, int rows, int threshold = 0)
    {
        if (columns < 2 || rows < 2) throw new ArgumentOutOfRangeException(nameof(columns));
        GrayImage gray = BitmapTools.ToGrayscale(bitmap);
        int expected = checked(columns * rows);
        int[] thresholds = threshold > 0
            ? [Math.Clamp(threshold, 1, 254)]
            : BuildThresholdCandidates(gray);

        PointGridDetectionResult? best = null;
        foreach (int currentThreshold in thresholds)
        {
            List<BrightComponent> components = FindComponents(gray, currentThreshold);
            if (components.Count < expected) continue;

            foreach (IReadOnlyList<BrightComponent> subset in CandidateSubsets(components, expected))
            {
                if (!TryOrder(subset, columns, rows, out PointF[]? ordered, out double rms)) continue;
                var candidate = new PointGridDetectionResult
                {
                    Threshold = currentThreshold,
                    Columns = columns,
                    Rows = rows,
                    OrderedPoints = ordered,
                    Components = components,
                    AssignmentRms = rms
                };
                if (best is null || candidate.AssignmentRms < best.AssignmentRms) best = candidate;
                if (rms < 0.18) return candidate;
            }
        }

        return best ?? throw new InvalidDataException(
            $"未能稳定识别 {columns}×{rows}={expected} 个点。请调整曝光、增益、识别阈值或相机 ROI。");
    }

    public static Bitmap DrawOverlay(Bitmap source, PointGridDetectionResult result)
    {
        Bitmap overlay = BitmapTools.Normalize32(source);
        using Graphics graphics = Graphics.FromImage(overlay);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pointPen = new Pen(Color.Lime, Math.Max(1f, source.Width / 1600f));
        using var rowPen = new Pen(Color.FromArgb(180, Color.DeepSkyBlue), Math.Max(1f, source.Width / 2500f));
        using var font = new Font(FontFamily.GenericSansSerif, Math.Max(7f, source.Width / 520f));
        float marker = Math.Max(4f, source.Width / 700f);

        for (int row = 0; row < result.Rows; row++)
        {
            PointF[] rowPoints = new PointF[result.Columns];
            for (int column = 0; column < result.Columns; column++)
            {
                int index = row * result.Columns + column;
                PointF point = result.OrderedPoints[index];
                rowPoints[column] = point;
                graphics.DrawEllipse(pointPen, point.X - marker, point.Y - marker, marker * 2, marker * 2);
                if ((column == 0 || column == result.Columns - 1) && (row == 0 || row == result.Rows - 1))
                {
                    graphics.DrawString($"{column},{row}", font, Brushes.Yellow, point.X + marker, point.Y + marker);
                }
            }
            graphics.DrawLines(rowPen, rowPoints);
        }
        return overlay;
    }

    private static int[] BuildThresholdCandidates(GrayImage image)
    {
        int[] histogram = new int[256];
        foreach (byte value in image.Pixels) histogram[value]++;
        int otsu = Otsu(histogram, image.Pixels.Length);
        int percentile = Percentile(histogram, image.Pixels.Length, 0.9995);
        return new[] { 220, 200, 180, 160, 140, 120, 100, 80, 64, 48, 40, 32, 24, 16, 12, 8,
                Math.Clamp(otsu + 12, 8, 220), percentile }
            .Where(value => value is > 0 and < 255)
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
    }

    private static List<BrightComponent> FindComponents(GrayImage gray, int threshold)
    {
        int width = gray.Width;
        int height = gray.Height;
        byte[] state = new byte[gray.Pixels.Length];
        for (int i = 0; i < state.Length; i++) state[i] = gray.Pixels[i] >= threshold ? (byte)1 : (byte)0;

        int maximumArea = Math.Max(2500, gray.Pixels.Length / 2500);
        var result = new List<BrightComponent>();
        int[] queue = new int[256];
        for (int start = 0; start < state.Length; start++)
        {
            if (state[start] != 1) continue;
            int head = 0;
            int tail = 0;
            queue[tail++] = start;
            state[start] = 2;
            int area = 0;
            long sumX = 0;
            long sumY = 0;
            long sumBrightness = 0;
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            while (head < tail)
            {
                int index = queue[head++];
                int y = Math.DivRem(index, width, out int x);
                area++;
                sumX += x;
                sumY += y;
                sumBrightness += gray.Pixels[index];
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                int fromY = Math.Max(0, y - 1);
                int toY = Math.Min(height - 1, y + 1);
                int fromX = Math.Max(0, x - 1);
                int toX = Math.Min(width - 1, x + 1);
                for (int neighborY = fromY; neighborY <= toY; neighborY++)
                {
                    int rowStart = neighborY * width;
                    for (int neighborX = fromX; neighborX <= toX; neighborX++)
                    {
                        int neighbor = rowStart + neighborX;
                        if (state[neighbor] != 1) continue;
                        state[neighbor] = 2;
                        if (tail == queue.Length) Array.Resize(ref queue, queue.Length * 2);
                        queue[tail++] = neighbor;
                    }
                }
            }

            int boundsWidth = maxX - minX + 1;
            int boundsHeight = maxY - minY + 1;
            double aspect = boundsWidth / (double)boundsHeight;
            double fill = area / (double)(boundsWidth * boundsHeight);
            if (area < 3 || area > maximumArea || boundsWidth < 2 || boundsHeight < 2 ||
                aspect < 0.25 || aspect > 4.0 || fill < 0.18) continue;

            result.Add(new BrightComponent(
                new PointF((float)(sumX / (double)area), (float)(sumY / (double)area)),
                area,
                new Rectangle(minX, minY, boundsWidth, boundsHeight),
                sumBrightness / (double)area,
                fill));
        }
        return result;
    }

    private static IEnumerable<IReadOnlyList<BrightComponent>> CandidateSubsets(
        IReadOnlyList<BrightComponent> components,
        int expected)
    {
        yield return components;
        if (components.Count <= expected) yield break;

        BrightComponent[] bright = components
            .OrderByDescending(component => component.MeanBrightness * Math.Sqrt(component.Area) * component.FillRatio)
            .Take(Math.Min(components.Count, expected * 3))
            .ToArray();
        yield return bright;
        yield return bright.Take(expected).ToArray();

        if (components.Count <= 1200)
        {
            BrightComponent[] dense = components
                .Select((component, index) => (component, score: FourthNeighborDistanceSquared(components, index)))
                .OrderBy(item => item.score)
                .Take(Math.Min(components.Count, expected * 3))
                .Select(item => item.component)
                .ToArray();
            yield return dense;
        }
    }

    private static double FourthNeighborDistanceSquared(IReadOnlyList<BrightComponent> points, int index)
    {
        Span<double> nearest = stackalloc double[4] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        PointF p = points[index].Center;
        for (int i = 0; i < points.Count; i++)
        {
            if (i == index) continue;
            PointF q = points[i].Center;
            double distance = (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
            if (distance >= nearest[3]) continue;
            int insert = 3;
            while (insert > 0 && distance < nearest[insert - 1])
            {
                nearest[insert] = nearest[insert - 1];
                insert--;
            }
            nearest[insert] = distance;
        }
        return nearest[3];
    }

    private static bool TryOrder(
        IReadOnlyList<BrightComponent> candidates,
        int columns,
        int rows,
        out PointF[] ordered,
        out double rms)
    {
        ordered = [];
        rms = double.PositiveInfinity;
        int expected = columns * rows;
        if (candidates.Count < expected) return false;

        double meanX = candidates.Average(c => c.Center.X);
        double meanY = candidates.Average(c => c.Center.Y);
        double xx = 0, xy = 0, yy = 0;
        foreach (BrightComponent component in candidates)
        {
            double dx = component.Center.X - meanX;
            double dy = component.Center.Y - meanY;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        double angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
        double ux = Math.Cos(angle);
        double uy = Math.Sin(angle);
        // 主轴必须沿 27 列方向。若 PCA 受噪声翻转，则使用图像 x 正方向固定朝向。
        if (ux < 0) { ux = -ux; uy = -uy; }
        double vx = -uy;
        double vy = ux;
        if (vy < 0) { vx = -vx; vy = -vy; }

        double[] u = new double[candidates.Count];
        double[] v = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            double dx = candidates[i].Center.X - meanX;
            double dy = candidates[i].Center.Y - meanY;
            u[i] = dx * ux + dy * uy;
            v[i] = dx * vx + dy * vy;
        }

        double[] centersU = KMeans1D(u, columns);
        double[] centersV = KMeans1D(v, rows);
        if (centersU.Length != columns || centersV.Length != rows) return false;
        double spacingU = MedianSpacing(centersU);
        double spacingV = MedianSpacing(centersV);
        if (spacingU <= 1 || spacingV <= 1) return false;

        var chosen = new (int index, double cost)[expected];
        Array.Fill(chosen, (-1, double.PositiveInfinity));
        for (int i = 0; i < candidates.Count; i++)
        {
            int column = Nearest(centersU, u[i]);
            int row = Nearest(centersV, v[i]);
            double du = (u[i] - centersU[column]) / spacingU;
            double dv = (v[i] - centersV[row]) / spacingV;
            double cost = du * du + dv * dv;
            int cell = row * columns + column;
            if (cost < chosen[cell].cost) chosen[cell] = (i, cost);
        }

        if (chosen.Any(item => item.index < 0 || item.cost > 0.45)) return false;
        ordered = new PointF[expected];
        double total = 0;
        for (int i = 0; i < expected; i++)
        {
            ordered[i] = candidates[chosen[i].index].Center;
            total += chosen[i].cost;
        }
        rms = Math.Sqrt(total / expected);
        return true;
    }

    private static double[] KMeans1D(double[] values, int clusterCount)
    {
        if (values.Length < clusterCount) return [];
        double[] sorted = values.OrderBy(value => value).ToArray();
        double[] centers = new double[clusterCount];
        for (int i = 0; i < clusterCount; i++)
        {
            int index = (int)Math.Round((i + 0.5) * sorted.Length / clusterCount - 0.5);
            centers[i] = sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }

        int[] assignments = new int[values.Length];
        for (int iteration = 0; iteration < 40; iteration++)
        {
            double[] sums = new double[clusterCount];
            int[] counts = new int[clusterCount];
            for (int i = 0; i < values.Length; i++)
            {
                int cluster = Nearest(centers, values[i]);
                assignments[i] = cluster;
                sums[cluster] += values[i];
                counts[cluster]++;
            }

            bool changed = false;
            for (int cluster = 0; cluster < clusterCount; cluster++)
            {
                if (counts[cluster] == 0) return [];
                double next = sums[cluster] / counts[cluster];
                changed |= Math.Abs(next - centers[cluster]) > 1e-4;
                centers[cluster] = next;
            }
            Array.Sort(centers);
            if (!changed) break;
        }
        return centers;
    }

    private static int Nearest(double[] centers, double value)
    {
        int best = 0;
        double distance = Math.Abs(value - centers[0]);
        for (int i = 1; i < centers.Length; i++)
        {
            double current = Math.Abs(value - centers[i]);
            if (current >= distance) continue;
            distance = current;
            best = i;
        }
        return best;
    }

    private static double MedianSpacing(double[] centers)
    {
        double[] spacing = new double[centers.Length - 1];
        for (int i = 0; i < spacing.Length; i++) spacing[i] = centers[i + 1] - centers[i];
        Array.Sort(spacing);
        return spacing[spacing.Length / 2];
    }

    private static int Otsu(int[] histogram, int total)
    {
        long weightedTotal = 0;
        for (int i = 0; i < histogram.Length; i++) weightedTotal += (long)i * histogram[i];
        long backgroundWeighted = 0;
        int backgroundCount = 0;
        double maximum = -1;
        int threshold = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            backgroundCount += histogram[i];
            if (backgroundCount == 0) continue;
            int foregroundCount = total - backgroundCount;
            if (foregroundCount == 0) break;
            backgroundWeighted += (long)i * histogram[i];
            double meanBackground = backgroundWeighted / (double)backgroundCount;
            double meanForeground = (weightedTotal - backgroundWeighted) / (double)foregroundCount;
            double between = (double)backgroundCount * foregroundCount *
                             (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (between > maximum) { maximum = between; threshold = i; }
        }
        return threshold;
    }

    private static int Percentile(int[] histogram, int total, double percentile)
    {
        int target = (int)Math.Ceiling(total * percentile);
        int cumulative = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target) return i;
        }
        return 255;
    }

    private static double MinimumGridSpacing(IReadOnlyList<PointF> points, int columns, int rows)
    {
        double minimum = double.PositiveInfinity;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (column + 1 < columns) minimum = Math.Min(minimum, Distance(points[index], points[index + 1]));
                if (row + 1 < rows) minimum = Math.Min(minimum, Distance(points[index], points[index + columns]));
            }
        }
        return double.IsFinite(minimum) ? minimum : 1;
    }

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
