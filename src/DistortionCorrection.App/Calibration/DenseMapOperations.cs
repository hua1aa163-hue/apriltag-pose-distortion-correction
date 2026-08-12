using DistortionCorrection.Imaging;
using DistortionCorrection.Models;

namespace DistortionCorrection.Calibration;

public static class DenseMapOperations
{
    public static PointF InterpolateCamera(DenseProjectorMap map, PointF projectorPoint)
    {
        Rectangle rect = ContentRectangle(map);
        double gx = (projectorPoint.X - rect.Left) / map.CellSize - 0.5;
        double gy = (projectorPoint.Y - rect.Top) / map.CellSize - 0.5;
        int x0 = (int)Math.Floor(gx);
        int y0 = (int)Math.Floor(gy);
        double fx = gx - x0;
        double fy = gy - y0;
        x0 = Math.Clamp(x0, 0, map.CellColumns - 1);
        y0 = Math.Clamp(y0, 0, map.CellRows - 1);
        int x1 = Math.Min(x0 + 1, map.CellColumns - 1);
        int y1 = Math.Min(y0 + 1, map.CellRows - 1);

        var samples = new (int x, int y, double weight)[]
        {
            (x0, y0, (1 - fx) * (1 - fy)),
            (x1, y0, fx * (1 - fy)),
            (x0, y1, (1 - fx) * fy),
            (x1, y1, fx * fy)
        };
        double sumWeight = 0;
        double cameraX = 0;
        double cameraY = 0;
        foreach ((int x, int y, double weight) in samples)
        {
            int index = y * map.CellColumns + x;
            if (map.SamplesPerCell[index] <= 0) continue;
            PointF point = map.CameraAtProjectorCell[index];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) continue;
            double adjusted = Math.Max(1e-6, weight);
            sumWeight += adjusted;
            cameraX += point.X * adjusted;
            cameraY += point.Y * adjusted;
        }
        if (sumWeight > 1e-5) return new PointF((float)(cameraX / sumWeight), (float)(cameraY / sumWeight));
        return NearestValid(map, projectorPoint);
    }

    public static PointF InvertCameraPoint(DenseProjectorMap map, PointF desiredCameraPoint)
    {
        Rectangle rect = ContentRectangle(map);
        int nearest = -1;
        double nearestDistance = double.PositiveInfinity;
        for (int i = 0; i < map.CameraAtProjectorCell.Length; i++)
        {
            if (map.SamplesPerCell[i] <= 0) continue;
            PointF camera = map.CameraAtProjectorCell[i];
            double dx = camera.X - desiredCameraPoint.X;
            double dy = camera.Y - desiredCameraPoint.Y;
            double distance = dx * dx + dy * dy;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = i;
        }
        if (nearest < 0) throw new InvalidDataException("稠密映射中没有有效单元。");

        int cellY = Math.DivRem(nearest, map.CellColumns, out int cellX);
        var current = new PointF(
            rect.Left + Math.Min(rect.Width - 1, cellX * map.CellSize + map.CellSize / 2f),
            rect.Top + Math.Min(rect.Height - 1, cellY * map.CellSize + map.CellSize / 2f));
        for (int iteration = 0; iteration < 12; iteration++)
        {
            PointF camera = InterpolateCamera(map, current);
            double ex = desiredCameraPoint.X - camera.X;
            double ey = desiredCameraPoint.Y - camera.Y;
            if (ex * ex + ey * ey < 0.04) break;
            if (!TryJacobian(map, current, out double j11, out double j12, out double j21, out double j22)) break;
            double determinant = j11 * j22 - j12 * j21;
            if (Math.Abs(determinant) < 1e-8) break;
            double dx = (j22 * ex - j12 * ey) / determinant;
            double dy = (-j21 * ex + j11 * ey) / determinant;
            double magnitude = Math.Sqrt(dx * dx + dy * dy);
            if (magnitude > map.CellSize * 3)
            {
                double scale = map.CellSize * 3 / magnitude;
                dx *= scale;
                dy *= scale;
            }
            current = new PointF(
                (float)Math.Clamp(current.X + dx, rect.Left, rect.Right - 1),
                (float)Math.Clamp(current.Y + dy, rect.Top, rect.Bottom - 1));
        }
        return current;
    }

    public static bool TryJacobian(
        DenseProjectorMap map,
        PointF projectorPoint,
        out double j11,
        out double j12,
        out double j21,
        out double j22)
    {
        Rectangle rect = ContentRectangle(map);
        float delta = Math.Max(1f, map.CellSize);
        PointF left = InterpolateCamera(map, new PointF(Math.Max(rect.Left, projectorPoint.X - delta), projectorPoint.Y));
        PointF right = InterpolateCamera(map, new PointF(Math.Min(rect.Right - 1, projectorPoint.X + delta), projectorPoint.Y));
        PointF top = InterpolateCamera(map, new PointF(projectorPoint.X, Math.Max(rect.Top, projectorPoint.Y - delta)));
        PointF bottom = InterpolateCamera(map, new PointF(projectorPoint.X, Math.Min(rect.Bottom - 1, projectorPoint.Y + delta)));
        double dx = Math.Max(1e-6, Math.Min(rect.Right - 1, projectorPoint.X + delta) - Math.Max(rect.Left, projectorPoint.X - delta));
        double dy = Math.Max(1e-6, Math.Min(rect.Bottom - 1, projectorPoint.Y + delta) - Math.Max(rect.Top, projectorPoint.Y - delta));
        j11 = (right.X - left.X) / dx;
        j21 = (right.Y - left.Y) / dx;
        j12 = (bottom.X - top.X) / dy;
        j22 = (bottom.Y - top.Y) / dy;
        return double.IsFinite(j11 + j12 + j21 + j22);
    }

    public static PointF[] BuildDesiredCameraGrid(DenseProjectorMap map, AppSettings settings)
    {
        return SolveDesiredGrid(map, settings).DesiredCameraPoints;
    }

    private static PointF[] CreateDesiredGrid(
        AppSettings settings,
        float centerX,
        float centerY,
        double width,
        double height)
    {
        var result = new PointF[checked(settings.GridColumns * settings.GridRows)];
        float left = (float)(centerX - width / 2);
        float top = (float)(centerY - height / 2);
        for (int row = 0; row < settings.GridRows; row++)
        {
            float y = top + (float)height * row / (settings.GridRows - 1f);
            for (int column = 0; column < settings.GridColumns; column++)
            {
                float x = left + (float)width * column / (settings.GridColumns - 1f);
                result[row * settings.GridColumns + column] = new PointF(x, y);
            }
        }
        return result;
    }

    public static MeshCalibration CreateInitialMesh(DenseProjectorMap map, AppSettings settings, string cameraSerial)
    {
        PointF[] source = PatternGenerator.CreateRegularGridPoints(settings);
        DesiredGridSolution solution = SolveDesiredGrid(map, settings);
        var mesh = new MeshCalibration
        {
            CameraSerial = cameraSerial,
            CanvasWidth = settings.CanvasWidth,
            CanvasHeight = settings.CanvasHeight,
            SourceContentWidth = settings.SourceContentWidth,
            SourceContentHeight = settings.SourceContentHeight,
            ExpectedInverseMappedWidth = settings.ExpectedInverseMappedWidth,
            ExpectedInverseMappedHeight = settings.ExpectedInverseMappedHeight,
            Columns = settings.GridColumns,
            Rows = settings.GridRows,
            SourcePoints = source,
            ProjectorPoints = solution.ProjectorPoints,
            DesiredCameraPoints = solution.DesiredCameraPoints
        };
        ValidateProjectionMesh(mesh, settings);
        return mesh;
    }

    /// <summary>
    /// 以用户指定的自然逆映射外接尺寸为约束，反求相机中的规则目标网格尺寸。
    /// 反射光路在横纵方向的缩放不同，因此宽高必须独立求解，不能预设 16:9。
    /// </summary>
    private static DesiredGridSolution SolveDesiredGrid(DenseProjectorMap map, AppSettings settings)
    {
        PointF[] source = PatternGenerator.CreateRegularGridPoints(settings);
        PointF[] observed = source.Select(point => InterpolateCamera(map, point)).ToArray();
        float minX = observed.Min(point => point.X);
        float maxX = observed.Max(point => point.X);
        float minY = observed.Min(point => point.Y);
        float maxY = observed.Max(point => point.Y);
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        double width = Math.Max(20, (maxX - minX) * 0.72);
        double height = Math.Max(20, (maxY - minY) * 0.55);
        DesiredGridSolution? best = null;
        double bestError = double.PositiveInfinity;

        for (int iteration = 0; iteration < 32; iteration++)
        {
            PointF[] desired = CreateDesiredGrid(settings, centerX, centerY, width, height);
            PointF[] projector = desired.Select(point => InvertCameraPoint(map, point)).ToArray();
            var candidate = CreateCalibration(settings, source, desired, projector);
            RectangleF bounds = MeshWarp.GetInverseMappedBounds(candidate);
            double widthError = settings.ExpectedInverseMappedWidth - bounds.Width;
            double heightError = settings.ExpectedInverseMappedHeight - bounds.Height;
            double error = Math.Abs(widthError) + Math.Abs(heightError);
            if (error < bestError)
            {
                bestError = error;
                best = new DesiredGridSolution(desired, projector, bounds, width, height);
            }
            if (Math.Abs(widthError) <= 0.35 && Math.Abs(heightError) <= 0.35) break;

            double widthRatio = settings.ExpectedInverseMappedWidth / Math.Max(1, bounds.Width);
            double heightRatio = settings.ExpectedInverseMappedHeight / Math.Max(1, bounds.Height);
            width *= Math.Clamp(widthRatio, 0.72, 1.30);
            height *= Math.Clamp(heightRatio, 0.72, 1.30);
            width = Math.Clamp(width, 20, Math.Max(40, (maxX - minX) * 1.25));
            height = Math.Clamp(height, 20, Math.Max(40, (maxY - minY) * 1.25));
        }

        DesiredGridSolution result = best ?? throw new InvalidDataException("无法建立逆映射目标网格。");
        if (Math.Abs(result.Bounds.Width - settings.ExpectedInverseMappedWidth) > 2.0 ||
            Math.Abs(result.Bounds.Height - settings.ExpectedInverseMappedHeight) > 2.0)
        {
            throw new InvalidDataException(
                $"无法满足 {settings.ExpectedInverseMappedWidth}×{settings.ExpectedInverseMappedHeight} " +
                $"逆映射约束；当前为 {result.Bounds.Width:F1}×{result.Bounds.Height:F1}。" +
                "请检查 Gray Code 覆盖范围和相机视野。");
        }
        return result;
    }

    public static MeshGeometryDiagnostics ValidateProjectionMesh(MeshCalibration mesh, AppSettings settings)
    {
        MeshGeometryDiagnostics diagnostics = MeshWarp.AnalyzeGeometry(mesh);
        double widthError = Math.Abs(diagnostics.Bounds.Width - settings.ExpectedInverseMappedWidth);
        double heightError = Math.Abs(diagnostics.Bounds.Height - settings.ExpectedInverseMappedHeight);
        double requiredSpacing = Math.Max(settings.DotDiameter * 1.6, 10);
        var failures = new List<string>();
        if (widthError > 2 || heightError > 2)
        {
            failures.Add(
                $"外接框 {diagnostics.Bounds.Width:F1}×{diagnostics.Bounds.Height:F1} " +
                $"不是目标 {settings.ExpectedInverseMappedWidth}×{settings.ExpectedInverseMappedHeight}");
        }
        if (diagnostics.OutsideCanvasPointCount > 0)
        {
            failures.Add($"{diagnostics.OutsideCanvasPointCount} 个扩展控制点越出 1920×1080");
        }
        if (diagnostics.FoldedTriangleCount > 0 || diagnostics.DegenerateTriangleCount > 0)
        {
            failures.Add($"网格含 {diagnostics.FoldedTriangleCount} 个翻折、{diagnostics.DegenerateTriangleCount} 个退化三角形");
        }
        if (diagnostics.MinimumControlPointSpacing < requiredSpacing)
        {
            failures.Add($"最小点间距 {diagnostics.MinimumControlPointSpacing:F1}px，小于安全值 {requiredSpacing:F1}px");
        }
        if (failures.Count > 0)
        {
            throw new InvalidDataException("投影前安全检查失败：" + string.Join("；", failures) + "。");
        }
        return diagnostics;
    }

    private static MeshCalibration CreateCalibration(
        AppSettings settings,
        PointF[] source,
        PointF[] desired,
        PointF[] projector) => new()
    {
        CanvasWidth = settings.CanvasWidth,
        CanvasHeight = settings.CanvasHeight,
        SourceContentWidth = settings.SourceContentWidth,
        SourceContentHeight = settings.SourceContentHeight,
        ExpectedInverseMappedWidth = settings.ExpectedInverseMappedWidth,
        ExpectedInverseMappedHeight = settings.ExpectedInverseMappedHeight,
        Columns = settings.GridColumns,
        Rows = settings.GridRows,
        SourcePoints = source,
        DesiredCameraPoints = desired,
        ProjectorPoints = projector
    };

    public static double UpdateMeshFromObservation(
        MeshCalibration mesh,
        DenseProjectorMap map,
        IReadOnlyList<PointF> observed,
        double relaxation)
    {
        mesh.Validate();
        if (mesh.DesiredCameraPoints.Length != observed.Count || observed.Count != mesh.ProjectorPoints.Length)
        {
            throw new ArgumentException("观测点数量与标定网格不一致。", nameof(observed));
        }
        double squaredError = 0;
        Rectangle rect = new(0, 0, mesh.CanvasWidth, mesh.CanvasHeight);
        for (int i = 0; i < observed.Count; i++)
        {
            double ex = mesh.DesiredCameraPoints[i].X - observed[i].X;
            double ey = mesh.DesiredCameraPoints[i].Y - observed[i].Y;
            squaredError += ex * ex + ey * ey;
            PointF projector = mesh.ProjectorPoints[i];
            if (!TryJacobian(map, projector, out double j11, out double j12, out double j21, out double j22)) continue;
            double determinant = j11 * j22 - j12 * j21;
            if (Math.Abs(determinant) < 1e-8) continue;
            double dx = relaxation * (j22 * ex - j12 * ey) / determinant;
            double dy = relaxation * (-j21 * ex + j11 * ey) / determinant;
            double maximumStep = map.CellSize * 4.0;
            double magnitude = Math.Sqrt(dx * dx + dy * dy);
            if (magnitude > maximumStep)
            {
                double scale = maximumStep / magnitude;
                dx *= scale;
                dy *= scale;
            }
            mesh.ProjectorPoints[i] = new PointF(
                (float)Math.Clamp(projector.X + dx, rect.Left, rect.Right - 1),
                (float)Math.Clamp(projector.Y + dy, rect.Top, rect.Bottom - 1));
        }
        mesh.LastObservedCameraPoints = observed.ToArray();
        mesh.LastRmsErrorPixels = Math.Sqrt(squaredError / observed.Count);
        return mesh.LastRmsErrorPixels;
    }

    private static PointF NearestValid(DenseProjectorMap map, PointF projectorPoint)
    {
        Rectangle rect = ContentRectangle(map);
        int column = Math.Clamp((int)((projectorPoint.X - rect.Left) / map.CellSize), 0, map.CellColumns - 1);
        int row = Math.Clamp((int)((projectorPoint.Y - rect.Top) / map.CellSize), 0, map.CellRows - 1);
        int maximumRadius = Math.Max(map.CellColumns, map.CellRows);
        for (int radius = 0; radius <= maximumRadius; radius++)
        {
            for (int y = Math.Max(0, row - radius); y <= Math.Min(map.CellRows - 1, row + radius); y++)
            {
                for (int x = Math.Max(0, column - radius); x <= Math.Min(map.CellColumns - 1, column + radius); x++)
                {
                    int index = y * map.CellColumns + x;
                    if (map.SamplesPerCell[index] > 0) return map.CameraAtProjectorCell[index];
                }
            }
        }
        return new PointF(float.NaN, float.NaN);
    }

    private static Rectangle ContentRectangle(DenseProjectorMap map) => new(
        0, 0, map.CanvasWidth, map.CanvasHeight);

    private sealed record DesiredGridSolution(
        PointF[] DesiredCameraPoints,
        PointF[] ProjectorPoints,
        RectangleF Bounds,
        double CameraTargetWidth,
        double CameraTargetHeight);
}
