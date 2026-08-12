using DistortionCorrection.Calibration;
using DistortionCorrection.Camera;
using DistortionCorrection.Imaging;
using DistortionCorrection.Models;

namespace DistortionCorrection.Diagnostics;

public static class SelfTests
{
    public static int Run(Action<string> log)
    {
        TestConfigurableSettings(log);
        TestDotGrids(log);
        TestGrayCode(log);
        TestBlackCanvasWarp(log);
        TestWarpedDotConsistency(log);
        TestManualAdjustment(log);
        TestBatchFolder(log);
        TestCenteredContentBatch(log);
        TestOpenCvLensCorrection(log);
        log("全部自测试通过。");
        return 0;
    }

    private static void TestConfigurableSettings(Action<string> log)
    {
        var settings = new AppSettings
        {
            GridColumns = 13,
            GridRows = 5,
            ExpectedInverseMappedWidth = 1600,
            ExpectedInverseMappedHeight = 720
        };
        settings.Validate();
        Assert(settings.SourceContentRectangle == new Rectangle(0, 0, 1920, 1080),
            $"输入区域错误：{settings.SourceContentRectangle}");
        log("PASS 可配置参数：13×5 点阵、1600×720 目标；输入/输出仍为 1920×1080");
    }

    private static void TestDotGrids(Action<string> log)
    {
        foreach ((int columns, int rows) in new[] { (27, 7), (13, 5) })
        {
            var settings = new AppSettings { GridColumns = columns, GridRows = rows };
            using Bitmap dot = PatternGenerator.CreateDotPattern(settings);
            PointGridDetectionResult detected = PointGridDetector.Detect(dot, columns, rows);
            Assert(detected.OrderedPoints.Length == columns * rows,
                $"规则点阵 {columns}×{rows} 未识别出全部点。");
            log($"PASS 规则点阵生成/识别：{columns}×{rows}，{detected.OrderedPoints.Length} 点");
        }
    }

    private static void TestGrayCode(Action<string> log)
    {
        for (int value = 0; value < 2048; value++)
        {
            int gray = value ^ (value >> 1);
            Assert(GrayCodeAccumulator.GrayToBinary(gray) == value, $"Gray Code 解码错误：{value}");
        }
        var settings = new AppSettings();
        IReadOnlyList<CalibrationPattern> patterns = PatternGenerator.CreateGrayCodeSequence(settings);
        try
        {
            Assert(patterns.Count >= 30 && patterns.Count <= 46, $"Gray Code 图卡数异常：{patterns.Count}");
            log($"PASS Gray Code 图卡：{patterns.Count} 张，cell={settings.GrayCodeCellSize}px");
        }
        finally
        {
            foreach (CalibrationPattern pattern in patterns) pattern.Image.Dispose();
        }
    }

    private static void TestBlackCanvasWarp(Action<string> log)
    {
        var settings = new AppSettings
        {
            GridColumns = 11,
            GridRows = 5,
            ExpectedInverseMappedWidth = 1500,
            ExpectedInverseMappedHeight = 600
        };
        PointF[] sourceGrid = PatternGenerator.CreateRegularGridPoints(settings);
        PointF[] destinationGrid = sourceGrid.Select(point => new PointF(
            100 + point.X / 1919f * 1500,
            200 + point.Y / 1079f * 600)).ToArray();
        var calibration = new MeshCalibration
        {
            ExpectedInverseMappedWidth = 1500,
            ExpectedInverseMappedHeight = 600,
            Columns = settings.GridColumns,
            Rows = settings.GridRows,
            SourcePoints = sourceGrid,
            ProjectorPoints = destinationGrid,
            DesiredCameraPoints = destinationGrid.ToArray()
        };
        using var input = new Bitmap(1920, 1080);
        using (Graphics graphics = Graphics.FromImage(input)) graphics.Clear(Color.FromArgb(30, 100, 220));
        using Bitmap warped = MeshWarp.WarpToRequiredCanvas(input, calibration);
        Assert(warped.Width == 1920 && warped.Height == 1080, "预扭曲输出不是 1920×1080。");
        Assert(warped.GetPixel(10, 10).ToArgb() == Color.Black.ToArgb(), "映射区外没有填黑。");
        Assert(warped.GetPixel(850, 500).B > 150, "映射区内部没有图像内容。");
        AssertThrows<InvalidDataException>(() =>
        {
            using Bitmap _ = MeshWarp.WarpToRequiredCanvas(
                input, calibration, MeshWarp.PlacementMode.Fixed, new Point(800, 600));
        }, "固定位置越界时没有阻止静默裁切。");
        log("PASS 1920×1080 黑底输出与固定位置越界保护");
    }

    private static void TestBatchFolder(Action<string> log)
    {
        string root = Path.Combine(Path.GetTempPath(), "distortion-correction-selftest-" + Guid.NewGuid().ToString("N"));
        string inputDirectory = Path.Combine(root, "input");
        string childDirectory = Path.Combine(inputDirectory, "child");
        string outputDirectory = Path.Combine(inputDirectory, "generated");
        Directory.CreateDirectory(childDirectory);
        try
        {
            using (var image = new Bitmap(320, 200))
            {
                using Graphics graphics = Graphics.FromImage(image);
                graphics.Clear(Color.CornflowerBlue);
                image.Save(Path.Combine(inputDirectory, "one.jpg"));
                image.Save(Path.Combine(childDirectory, "two.png"));
            }

            var settings = new AppSettings { GridColumns = 7, GridRows = 4 };
            PointF[] grid = PatternGenerator.CreateRegularGridPoints(settings);
            var identity = new MeshCalibration
            {
                Columns = settings.GridColumns,
                Rows = settings.GridRows,
                SourcePoints = grid,
                ProjectorPoints = grid.ToArray(),
                DesiredCameraPoints = grid.ToArray()
            };
            BatchWarpResult result = BatchWarpService.WarpFolder(
                inputDirectory, outputDirectory, identity, BatchPlacementModes.Both,
                Point.Empty, recursive: true);
            Assert(result.DiscoveredFiles == 2 && result.SuccessfulFiles == 2 && result.OutputFiles == 4,
                $"批处理计数错误：{result}");
            Assert(result.Failures.Count == 0, "批处理出现意外失败。");
            string sample = Path.Combine(outputDirectory, "自动位置", "child", "two.png");
            using var output = new Bitmap(sample);
            Assert(output.Width == 1920 && output.Height == 1080, "批处理输出尺寸错误。");
            log("PASS 文件夹递归批处理：保留子目录、双方案输出、排除自身输出目录");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void TestCenteredContentBatch(Action<string> log)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "distortion-correction-centered-selftest-" + Guid.NewGuid().ToString("N"));
        string inputDirectory = Path.Combine(root, "input");
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(inputDirectory);
        try
        {
            using (var image = new Bitmap(1920, 1080))
            {
                using Graphics graphics = Graphics.FromImage(image);
                graphics.Clear(Color.Black);
                graphics.FillRectangle(Brushes.Red, (1920 - 1000) / 2, (1080 - 400) / 2, 1000, 400);
                image.Save(Path.Combine(inputDirectory, "centered.png"));
            }

            var settings = new AppSettings { GridColumns = 7, GridRows = 4 };
            PointF[] grid = PatternGenerator.CreateRegularGridPoints(settings);
            var identity = new MeshCalibration
            {
                Columns = settings.GridColumns,
                Rows = settings.GridRows,
                SourcePoints = grid,
                ProjectorPoints = grid.ToArray(),
                DesiredCameraPoints = grid.ToArray()
            };
            var options = new CenteredContentWarpOptions(1000, 400, 1600, 600);
            BatchWarpResult result = BatchWarpService.WarpCenteredContentFolder(
                inputDirectory, outputDirectory, identity, BatchPlacementModes.Fixed,
                new Point(100, 200), recursive: false, options);
            Assert(result.DiscoveredFiles == 1 && result.SuccessfulFiles == 1 &&
                   result.OutputFiles == 1 && result.Failures.Count == 0,
                $"居中矩形批处理计数错误：{result}");

            string outputPath = Path.Combine(
                outputDirectory, "居中矩形-固定位置", "centered.png");
            using var output = new Bitmap(outputPath);
            Rectangle actual = CalculateNonBlackBounds(output);
            Assert(output.Width == 1920 && output.Height == 1080,
                "居中矩形批处理输出画布不是 1920×1080。");
            Assert(actual == new Rectangle(100, 200, 1600, 600),
                $"居中矩形批处理实际像素外接框错误：{actual}。");
            log("PASS 居中矩形批处理：可配置输入裁取与输出外接尺寸、1920×1080 黑底输出");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void TestOpenCvLensCorrection(Action<string> log)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "distortion-correction-lens-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var calibration = new LensCalibration
            {
                ImageWidth = 96,
                ImageHeight = 64,
                CameraMatrix = [80, 0, 47.5, 0, 80, 31.5, 0, 0, 1],
                DistortionCoefficients = [0, 0, 0, 0, 0],
                RmsReprojectionError = 0.25,
                MeanReprojectionError = 0.2,
                UsedImageCount = 12,
                ChessboardColumns = 9,
                ChessboardRows = 6,
                ChessboardSquareSize = 25,
                CameraSerial = "SELFTEST"
            };
            string path = Path.Combine(root, "lens-calibration.json");
            calibration.Save(path);
            LensCalibration loaded = LensCalibration.Load(path);
            Assert(loaded.ImageWidth == 96 && loaded.CameraMatrix.SequenceEqual(calibration.CameraMatrix),
                "镜头标定 JSON 保存/加载不一致。");

            using var source = new Bitmap(96, 64);
            using (Graphics graphics = Graphics.FromImage(source))
            {
                graphics.Clear(Color.Black);
                graphics.FillRectangle(Brushes.White, 20, 12, 56, 40);
                graphics.DrawLine(Pens.Red, 0, 32, 95, 32);
            }
            using var corrector = new OpenCvLensCorrectionService();
            corrector.Configure(loaded, alpha: 0);
            using Bitmap output = corrector.Correct(source);
            Assert(output.Width == source.Width && output.Height == source.Height,
                "OpenCV 镜头矫正改变了相机帧尺寸。");
            Assert(output.GetPixel(48, 32).R > 180,
                "零畸变参数矫正后中心内容异常。");
            AssertThrows<InvalidDataException>(() =>
            {
                using var wrongSize = new Bitmap(95, 64);
                using Bitmap _ = corrector.Correct(wrongSize);
            }, "镜头标定尺寸与相机帧不一致时没有阻止处理。");
            log("PASS OpenCvSharp 镜头标定 JSON、Remap 输出尺寸及分辨率一致性保护");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void TestWarpedDotConsistency(Action<string> log)
    {
        var settings = new AppSettings
        {
            GridColumns = 13,
            GridRows = 6,
            ExpectedInverseMappedWidth = 1700,
            ExpectedInverseMappedHeight = 650
        };
        PointF[] source = PatternGenerator.CreateRegularGridPoints(settings);
        PointF[] projector = source.Select(point =>
        {
            double nx = point.X / 1919.0;
            double ny = point.Y / 1079.0;
            return new PointF(
                (float)(100 + 1700 * nx + 18 * Math.Sin(ny * Math.PI)),
                (float)(200 + 650 * ny + 55 * nx));
        }).ToArray();
        var calibration = new MeshCalibration
        {
            ExpectedInverseMappedWidth = 1700,
            ExpectedInverseMappedHeight = 650,
            Columns = settings.GridColumns,
            Rows = settings.GridRows,
            SourcePoints = source,
            ProjectorPoints = projector,
            DesiredCameraPoints = projector.ToArray()
        };
        using Bitmap warpedDots = PatternGenerator.CreateWarpedDotPattern(settings, calibration);
        PointGridDetectionResult detection = PointGridDetector.Detect(
            warpedDots, settings.GridColumns, settings.GridRows);
        double rms = CalculateRms(detection.OrderedPoints, projector);
        Assert(rms <= 1.25, $"点阵与批处理 MeshWarp 路径不一致，控制点 RMS={rms:F3}px。");
        log($"PASS 点阵/批处理统一 MeshWarp 路径：控制点 RMS={rms:F3}px");
    }

    private static void TestManualAdjustment(Action<string> log)
    {
        var settings = new AppSettings { GridColumns = 9, GridRows = 5 };
        PointF[] source = PatternGenerator.CreateRegularGridPoints(settings);
        PointF[] projector = source.Select(point => new PointF(
            100 + point.X / 1919f * 1650,
            210 + point.Y / 1079f * 620)).ToArray();
        var calibration = new MeshCalibration
        {
            ExpectedInverseMappedWidth = 1650,
            ExpectedInverseMappedHeight = 620,
            Columns = settings.GridColumns,
            Rows = settings.GridRows,
            SourcePoints = source,
            ProjectorPoints = projector,
            DesiredCameraPoints = projector.ToArray()
        };
        ManualAdjustmentResult identity = ManualCalibrationAdjustment.Apply(
            calibration, new ManualAdjustmentSettings());
        Assert(CalculateRms(identity.Calibration.ProjectorPoints, projector) < 0.001,
            "归零的人眼调整改变了标定网格。");

        var adjustment = new ManualAdjustmentSettings
        {
            RotationDegrees = 1.0,
            HorizontalPerspectivePercent = 3.0,
            VerticalPerspectivePercent = -2.0,
            HorizontalScalePercent = 98,
            VerticalScalePercent = 97,
            OffsetX = 5,
            OffsetY = -3
        };
        ManualAdjustmentResult adjusted = ManualCalibrationAdjustment.Apply(calibration, adjustment);
        Assert(CalculateRms(adjusted.Calibration.ProjectorPoints, projector) > 5,
            "人眼调整没有改变投影网格。");
        Assert(Math.Abs(adjusted.Geometry.Bounds.Width - 1650 * 0.98) <= 0.75 &&
               Math.Abs(adjusted.Geometry.Bounds.Height - 620 * 0.97) <= 0.75,
            $"人眼旋转/透视后实际外接尺寸未保持为宽高缩放目标；当前 " +
            $"{adjusted.Geometry.Bounds.Width:F1}×{adjusted.Geometry.Bounds.Height:F1}。");
        Assert(adjusted.Calibration.ExpectedInverseMappedWidth == 1617 &&
               adjusted.Calibration.ExpectedInverseMappedHeight == 601,
            "人眼调整标定中记录的目标外接尺寸不正确。");
        Assert(adjusted.Geometry.FoldedTriangleCount == 0 && adjusted.Geometry.OutsideCanvasPointCount == 0,
            "安全范围内的人眼调整产生了无效网格。");

        ManualAdjustmentResult rotatedOnly = ManualCalibrationAdjustment.Apply(calibration,
            new ManualAdjustmentSettings { RotationDegrees = -2 });
        Assert(Math.Abs(rotatedOnly.Geometry.Bounds.Width - 1650) <= 0.75 &&
               Math.Abs(rotatedOnly.Geometry.Bounds.Height - 620) <= 0.75,
            $"仅旋转时改变了目标外接尺寸；当前 " +
            $"{rotatedOnly.Geometry.Bounds.Width:F1}×{rotatedOnly.Geometry.Bounds.Height:F1}。");
        using (var solid = new Bitmap(1920, 1080))
        {
            using (Graphics graphics = Graphics.FromImage(solid)) graphics.Clear(Color.Blue);
            using Bitmap rendered = MeshWarp.WarpToRequiredCanvas(
                solid, rotatedOnly.Calibration, MeshWarp.PlacementMode.Fixed, new Point(100, 200));
            Rectangle actualPixels = CalculateNonBlackBounds(rendered);
            Assert(actualPixels.Width == 1650 && actualPixels.Height == 620,
                $"仅旋转后的实际非黑像素外接尺寸不是 1650×620；当前 " +
                $"{actualPixels.Width}×{actualPixels.Height}。");
        }
        AssertThrows<InvalidDataException>(() => ManualCalibrationAdjustment.Apply(calibration,
            new ManualAdjustmentSettings { OffsetX = 500 }),
            "人眼调整越界时没有阻止输出。");
        log("PASS 人眼微调：旋转、左右/上下透视、宽高缩放、平移及越界保护");
    }

    private static Rectangle CalculateNonBlackBounds(Bitmap bitmap)
    {
        byte[] pixels = BitmapTools.ReadBgra32(bitmap);
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int index = (y * bitmap.Width + x) * 4;
                if ((pixels[index] | pixels[index + 1] | pixels[index + 2]) == 0) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static double CalculateRms(IReadOnlyList<PointF> left, IReadOnlyList<PointF> right)
    {
        Assert(left.Count == right.Count, "RMS 点数不一致。");
        double sum = 0;
        for (int i = 0; i < left.Count; i++)
        {
            double dx = left[i].X - right[i].X;
            double dy = left[i].Y - right[i].Y;
            sum += dx * dx + dy * dy;
        }
        return Math.Sqrt(sum / left.Count);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SELF-TEST FAILED: " + message);
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("SELF-TEST FAILED: " + message);
    }
}
