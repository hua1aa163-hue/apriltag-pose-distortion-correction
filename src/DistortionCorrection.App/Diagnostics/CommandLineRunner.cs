using DistortionCorrection.Calibration;
using DistortionCorrection.Camera;
using DistortionCorrection.Imaging;
using DistortionCorrection.Models;
using DistortionCorrection.Projection;
using System.Drawing.Imaging;

namespace DistortionCorrection.Diagnostics;

public static class CommandLineRunner
{
    public static int Run(string[] args)
    {
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--self-test" => SelfTests.Run(Console.WriteLine),
                "--analyze-sample" => AnalyzeSample(args),
                "--generate-patterns" => GeneratePatterns(args),
                "--enumerate-cameras" => EnumerateCameras(),
                "--capture" => Capture(args),
                "--project-capture" => ProjectAndCapture(args),
                "--set-wallpaper" => SetWallpaper(args),
                "--full-calibration" => FullCalibration(args),
                "--prepare-loop" => PrepareLoop(args),
                "--resume-loop" => ResumeLoop(args),
                "--warp" => Warp(args),
                "--warp-folder" => WarpFolder(args),
                "--manual-adjust" => ManualAdjust(args),
                "--verify-pattern-consistency" => VerifyPatternConsistency(args),
                _ => Usage($"未知参数：{args[0]}")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int AnalyzeSample(string[] args)
    {
        if (args.Length < 4) return Usage("--analyze-sample 需要投影原图、相机图和输出目录。");
        int threshold = args.Length >= 5 ? int.Parse(args[4]) : 0;
        AppSettings settings = AppSettings.Load();
        OfflineCalibrationResult result = OfflineSampleCalibrator.Analyze(
            args[1], args[2], args[3], settings, threshold);
        Console.WriteLine($"投影图识别：{result.ProjectorDetection.OrderedPoints.Length} 点，阈值 {result.ProjectorDetection.Threshold}，分配 RMS {result.ProjectorDetection.AssignmentRms:F4}");
        Console.WriteLine($"相机图识别：{result.CameraDetection.OrderedPoints.Length} 点，阈值 {result.CameraDetection.Threshold}，分配 RMS {result.CameraDetection.AssignmentRms:F4}");
        Console.WriteLine($"初始几何 RMS：{result.Calibration.LastRmsErrorPixels:F3} camera px");
        Console.WriteLine($"首轮矫正点阵：{result.CorrectedPatternPath}");
        Console.WriteLine($"标定文件：{result.CalibrationPath}");
        return 0;
    }

    private static int GeneratePatterns(string[] args)
    {
        if (args.Length < 2) return Usage("--generate-patterns 需要输出目录。");
        AppSettings settings = AppSettings.Load();
        string dotPath = Path.Combine(args[1],
            $"{settings.GridColumns}x{settings.GridRows}-dot-" +
            $"{settings.ExpectedInverseMappedWidth}x{settings.ExpectedInverseMappedHeight}-in-1920x1080.png");
        Directory.CreateDirectory(args[1]);
        using (Bitmap dot = PatternGenerator.CreateDotPattern(settings)) dot.Save(dotPath, ImageFormat.Png);
        IReadOnlyList<CalibrationPattern> gray = PatternGenerator.CreateGrayCodeSequence(settings);
        try { PatternGenerator.SavePatterns(gray, Path.Combine(args[1], "graycode")); }
        finally { foreach (CalibrationPattern pattern in gray) pattern.Image.Dispose(); }
        Console.WriteLine($"点阵：{dotPath}");
        Console.WriteLine($"Gray Code 图卡数：{gray.Count}");
        return 0;
    }

    private static int EnumerateCameras()
    {
        using var camera = new HikCameraService();
        IReadOnlyList<CameraDescriptor> devices = camera.Enumerate();
        Console.WriteLine($"发现 {devices.Count} 台 GigE 相机：");
        foreach (CameraDescriptor device in devices) Console.WriteLine(device);
        return devices.Count == 0 ? 2 : 0;
    }

    private static int Capture(string[] args)
    {
        if (args.Length < 2) return Usage("--capture 需要输出图路径。");
        AppSettings settings = AppSettings.Load();
        using var camera = new HikCameraService();
        IReadOnlyList<CameraDescriptor> devices = camera.Enumerate();
        CameraDescriptor selected = devices.FirstOrDefault(device =>
            string.Equals(device.SerialNumber, settings.PreferredCameraSerial, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault()
            ?? throw new InvalidOperationException("未发现相机。");
        camera.Connect(selected, settings);
        using Bitmap capture = camera.CaptureAsync(settings.CaptureTimeoutMilliseconds, CancellationToken.None)
            .GetAwaiter().GetResult();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        capture.Save(args[1], ImageFormat.Png);
        Console.WriteLine($"已从 {selected} 抓取 {capture.Width}×{capture.Height}：{args[1]}");
        return 0;
    }

    private static int ProjectAndCapture(string[] args)
    {
        if (args.Length < 3) return Usage("--project-capture 需要投影图路径和输出抓图路径。");
        AppSettings settings = AppSettings.Load();
        var display = new DesktopDisplayService();
        WallpaperSnapshot originalWallpaper = display.CaptureSnapshot();
        try
        {
            display.SetWallpaper(args[1]);
            Thread.Sleep(Math.Max(1200, settings.ProjectionSettleMilliseconds));
            using var camera = new HikCameraService();
            IReadOnlyList<CameraDescriptor> devices = camera.Enumerate();
            CameraDescriptor selected = devices.FirstOrDefault(device =>
                string.Equals(device.SerialNumber, settings.PreferredCameraSerial, StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault()
                ?? throw new InvalidOperationException("未发现相机。");
            camera.Connect(selected, settings);
            using Bitmap capture = camera.CaptureAsync(settings.CaptureTimeoutMilliseconds, CancellationToken.None)
                .GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
            capture.Save(args[2], ImageFormat.Png);
            Console.WriteLine($"已投图并抓取 {capture.Width}×{capture.Height}：{args[2]}");
            return 0;
        }
        finally
        {
            try { display.RestoreSnapshot(originalWallpaper); } catch { }
        }
    }

    private static int SetWallpaper(string[] args)
    {
        if (args.Length < 2) return Usage("--set-wallpaper 需要图片路径。");
        var display = new DesktopDisplayService();
        WallpaperSnapshot before = display.CaptureSnapshot();
        Console.WriteLine($"切换前 legacy：{before.LegacyWallpaper}");
        foreach (MonitorWallpaper monitor in before.Monitors) Console.WriteLine($"  {monitor.MonitorId} => {monitor.WallpaperPath}");
        display.SetWallpaper(args[1]);
        WallpaperSnapshot after = display.CaptureSnapshot();
        Console.WriteLine($"切换后 legacy：{after.LegacyWallpaper}");
        foreach (MonitorWallpaper monitor in after.Monitors) Console.WriteLine($"  {monitor.MonitorId} => {monitor.WallpaperPath}");
        return 0;
    }

    private static int FullCalibration(string[] args)
    {
        if (args.Length < 2) return Usage("--full-calibration 需要输出根目录。");
        AppSettings settings = AppSettings.Load();
        using var camera = new HikCameraService();
        IReadOnlyList<CameraDescriptor> devices = camera.Enumerate();
        CameraDescriptor selected = devices.FirstOrDefault(device =>
            string.Equals(device.SerialNumber, settings.PreferredCameraSerial, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault()
            ?? throw new InvalidOperationException("未发现相机。");
        camera.Connect(selected, settings);
        var progress = new Progress<CalibrationProgress>(item =>
            Console.WriteLine($"[{item.Stage}] {item.Current}/{item.Total} {item.Message}"));
        var controller = new LoopCalibrationController(settings, camera, new DesktopDisplayService(), progress);
        CalibrationRunResult result = controller.RunAsync(args[1], CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"完成：RMS={result.FinalRmsPixels:F3}px，回环={result.LoopIterations}，覆盖={result.ValidDenseCells}/{result.TotalDenseCells}");
        Console.WriteLine($"自动位置外接框：left={result.AutoInverseMappedBounds.Left:F1}, top={result.AutoInverseMappedBounds.Top:F1}, size={result.AutoInverseMappedBounds.Width:F1}×{result.AutoInverseMappedBounds.Height:F1}");
        Console.WriteLine($"标定：{result.MeshCalibrationPath}");
        Console.WriteLine($"最终点阵：{result.FinalVerificationPatternPath}");
        return 0;
    }

    private static int ResumeLoop(string[] args)
    {
        if (args.Length < 2) return Usage("--resume-loop 需要已有稠密映射的运行目录。");
        string runDirectory = Path.GetFullPath(args[1]);
        AppSettings settings = AppSettings.Load(Path.Combine(runDirectory, "settings.json"));
        using var camera = new HikCameraService();
        IReadOnlyList<CameraDescriptor> devices = camera.Enumerate();
        CameraDescriptor selected = devices.FirstOrDefault(device =>
            string.Equals(device.SerialNumber, settings.PreferredCameraSerial, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault()
            ?? throw new InvalidOperationException("未发现相机。");
        camera.Connect(selected, settings);
        var progress = new Progress<CalibrationProgress>(item =>
            Console.WriteLine($"[{item.Stage}] {item.Current}/{item.Total} {item.Message}"));
        var controller = new LoopOnlyController(settings, camera, new DesktopDisplayService(), progress);
        CalibrationRunResult result = controller.RunAsync(runDirectory, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"完成：RMS={result.FinalRmsPixels:F3}px，回环={result.LoopIterations}，覆盖={result.ValidDenseCells}/{result.TotalDenseCells}");
        Console.WriteLine($"自动位置外接框：left={result.AutoInverseMappedBounds.Left:F1}, top={result.AutoInverseMappedBounds.Top:F1}, size={result.AutoInverseMappedBounds.Width:F1}×{result.AutoInverseMappedBounds.Height:F1}");
        Console.WriteLine($"标定：{result.MeshCalibrationPath}");
        Console.WriteLine($"最终点阵：{result.FinalVerificationPatternPath}");
        return 0;
    }

    private static int PrepareLoop(string[] args)
    {
        if (args.Length < 2) return Usage("--prepare-loop 需要已有稠密映射的运行目录。");
        string runDirectory = Path.GetFullPath(args[1]);
        AppSettings settings = AppSettings.Load(Path.Combine(runDirectory, "settings.json"));
        string denseMapPath = Path.Combine(runDirectory, "projector-to-camera.dmap");
        DenseProjectorMap denseMap = CalibrationStorage.LoadDenseMap(denseMapPath);
        MeshCalibration mesh = DenseMapOperations.CreateInitialMesh(
            denseMap, settings, settings.PreferredCameraSerial);
        mesh.DenseMapFile = Path.GetFileName(denseMapPath);
        MeshGeometryDiagnostics geometry = DenseMapOperations.ValidateProjectionMesh(mesh, settings);

        string meshPath = Path.Combine(runDirectory, "calibration-prepared.json");
        CalibrationStorage.SaveMesh(mesh, meshPath);
        string patternPath = Path.Combine(runDirectory, "patterns", "prepared-loop-00-dot.png");
        Directory.CreateDirectory(Path.GetDirectoryName(patternPath)!);
        using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(settings, mesh))
        {
            pattern.Save(patternPath, ImageFormat.Png);
        }

        Console.WriteLine(
            $"准备完成：left={geometry.Bounds.Left:F1}, top={geometry.Bounds.Top:F1}, " +
            $"size={geometry.Bounds.Width:F1}×{geometry.Bounds.Height:F1}");
        Console.WriteLine(
            $"网格：triangles={geometry.TriangleCount}, folded={geometry.FoldedTriangleCount}, " +
            $"degenerate={geometry.DegenerateTriangleCount}, min-spacing={geometry.MinimumControlPointSpacing:F1}px");
        Console.WriteLine($"标定：{meshPath}");
        Console.WriteLine($"点阵：{patternPath}");
        return 0;
    }

    private static int Warp(string[] args)
    {
        if (args.Length < 4) return Usage("--warp 需要标定 JSON、输入图和输出目录。");
        MeshCalibration calibration = CalibrationStorage.LoadMesh(args[1]);
        Point? fixedTopLeft = args.Length >= 6
            ? new Point(int.Parse(args[4]), int.Parse(args[5]))
            : null;
        Directory.CreateDirectory(args[3]);
        using var input = new Bitmap(args[2]);
        string autoPath = Path.Combine(args[3], "inverse-auto-position.png");
        string fixedPath = Path.Combine(args[3], "inverse-fixed-position.png");
        using (Bitmap automatic = MeshWarp.WarpToRequiredCanvas(input, calibration, MeshWarp.PlacementMode.Auto))
        {
            automatic.Save(autoPath, ImageFormat.Png);
        }
        using (Bitmap fixedImage = MeshWarp.WarpToRequiredCanvas(
                   input, calibration, MeshWarp.PlacementMode.Fixed, fixedTopLeft))
        {
            fixedImage.Save(fixedPath, ImageFormat.Png);
        }
        RectangleF automaticBounds = MeshWarp.GetInverseMappedBounds(calibration, MeshWarp.PlacementMode.Auto);
        RectangleF fixedBounds = MeshWarp.GetInverseMappedBounds(
            calibration, MeshWarp.PlacementMode.Fixed, fixedTopLeft);
        Console.WriteLine($"自动版：{autoPath} | left={automaticBounds.Left:F1}, top={automaticBounds.Top:F1}, size={automaticBounds.Width:F1}×{automaticBounds.Height:F1}");
        Console.WriteLine($"固定版：{fixedPath} | left={fixedBounds.Left:F1}, top={fixedBounds.Top:F1}, size={fixedBounds.Width:F1}×{fixedBounds.Height:F1}");
        return 0;
    }

    private static int WarpFolder(string[] args)
    {
        if (args.Length < 4) return Usage("--warp-folder 需要标定 JSON、输入文件夹和输出文件夹。");
        MeshCalibration calibration = CalibrationStorage.LoadMesh(args[1]);
        Point fixedTopLeft = args.Length >= 6
            ? new Point(int.Parse(args[4]), int.Parse(args[5]))
            : new Point(
                (calibration.CanvasWidth - calibration.ExpectedInverseMappedWidth) / 2,
                (calibration.CanvasHeight - calibration.ExpectedInverseMappedHeight) / 2);
        BatchWarpResult result = BatchWarpService.WarpFolder(
            args[2], args[3], calibration, BatchPlacementModes.Both, fixedTopLeft,
            recursive: true,
            new Progress<BatchWarpProgress>(item =>
                Console.WriteLine($"{item.Current}/{item.Total} {item.SourcePath} {item.Message}")));
        Console.WriteLine(
            $"完成：输入={result.DiscoveredFiles}，成功={result.SuccessfulFiles}，" +
            $"输出={result.OutputFiles}，失败={result.Failures.Count}");
        foreach (BatchWarpFailure failure in result.Failures)
            Console.Error.WriteLine($"失败：{failure.SourcePath} | {failure.Error}");
        return result.Failures.Count == 0 ? 0 : 2;
    }

    private static int ManualAdjust(string[] args)
    {
        if (args.Length < 11)
            return Usage("--manual-adjust 需要基础标定、输出标定、输出点阵和 7 个调节参数。");
        MeshCalibration source = CalibrationStorage.LoadMesh(args[1]);
        var adjustment = new ManualAdjustmentSettings
        {
            RotationDegrees = double.Parse(args[4]),
            HorizontalPerspectivePercent = double.Parse(args[5]),
            VerticalPerspectivePercent = double.Parse(args[6]),
            HorizontalScalePercent = double.Parse(args[7]),
            VerticalScalePercent = double.Parse(args[8]),
            OffsetX = double.Parse(args[9]),
            OffsetY = double.Parse(args[10])
        };
        ManualAdjustmentResult result = ManualCalibrationAdjustment.Apply(source, adjustment);
        CalibrationStorage.SaveMesh(result.Calibration, args[2]);
        AppSettings settings = SettingsForCalibration(result.Calibration);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
        using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(settings, result.Calibration))
            pattern.Save(args[3], ImageFormat.Png);
        Console.WriteLine(
            $"人眼调整完成：left={result.Geometry.Bounds.Left:F1}, top={result.Geometry.Bounds.Top:F1}, " +
            $"size={result.Geometry.Bounds.Width:F1}×{result.Geometry.Bounds.Height:F1}");
        Console.WriteLine($"标定：{args[2]}");
        Console.WriteLine($"确认点阵：{args[3]}");
        return 0;
    }

    private static int VerifyPatternConsistency(string[] args)
    {
        if (args.Length < 3) return Usage("--verify-pattern-consistency 需要标定 JSON 和输出目录。");
        MeshCalibration calibration = CalibrationStorage.LoadMesh(args[1]);
        AppSettings settings = SettingsForCalibration(calibration);
        Directory.CreateDirectory(args[2]);
        string patternPath = Path.Combine(args[2], "meshwarp-consistency-pattern.png");
        string overlayPath = Path.Combine(args[2], "meshwarp-consistency-overlay.png");
        using Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(settings, calibration);
        pattern.Save(patternPath, ImageFormat.Png);
        PointGridDetectionResult detection = PointGridDetector.Detect(
            pattern, calibration.Columns, calibration.Rows);
        double rms = CalculateGridSymmetryRms(
            detection.OrderedPoints, calibration.ProjectorPoints, calibration.Columns, calibration.Rows);
        using (Bitmap overlay = PointGridDetector.DrawOverlay(pattern, detection))
            overlay.Save(overlayPath, ImageFormat.Png);
        Console.WriteLine($"点阵/批处理一致性 RMS={rms:F3} projector px");
        Console.WriteLine($"点阵：{patternPath}");
        Console.WriteLine($"叠加：{overlayPath}");
        return rms <= 1.5 ? 0 : 2;
    }

    private static AppSettings SettingsForCalibration(MeshCalibration calibration) => new()
    {
        GridColumns = calibration.Columns,
        GridRows = calibration.Rows,
        ExpectedInverseMappedWidth = calibration.ExpectedInverseMappedWidth,
        ExpectedInverseMappedHeight = calibration.ExpectedInverseMappedHeight
    };

    private static double CalculateRms(IReadOnlyList<PointF> left, IReadOnlyList<PointF> right)
    {
        if (left.Count != right.Count) throw new InvalidDataException("一致性检测点数不一致。");
        double sum = 0;
        for (int i = 0; i < left.Count; i++)
        {
            double dx = left[i].X - right[i].X;
            double dy = left[i].Y - right[i].Y;
            sum += dx * dx + dy * dy;
        }
        return Math.Sqrt(sum / left.Count);
    }

    private static double CalculateGridSymmetryRms(
        IReadOnlyList<PointF> detected,
        IReadOnlyList<PointF> expected,
        int columns,
        int rows)
    {
        double best = double.PositiveInfinity;
        foreach (bool flipRows in new[] { false, true })
        {
            foreach (bool flipColumns in new[] { false, true })
            {
                double sum = 0;
                for (int row = 0; row < rows; row++)
                {
                    int detectedRow = flipRows ? rows - 1 - row : row;
                    for (int column = 0; column < columns; column++)
                    {
                        int detectedColumn = flipColumns ? columns - 1 - column : column;
                        PointF actual = detected[detectedRow * columns + detectedColumn];
                        PointF target = expected[row * columns + column];
                        double dx = actual.X - target.X;
                        double dy = actual.Y - target.Y;
                        sum += dx * dx + dy * dy;
                    }
                }
                best = Math.Min(best, Math.Sqrt(sum / expected.Count));
            }
        }
        return best;
    }

    private static int Usage(string error)
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine("用法：--self-test | --analyze-sample <投影图> <相机图> <输出目录> [阈值] | --generate-patterns <输出目录> | --enumerate-cameras | --capture <输出图> | --project-capture <投影图> <输出抓图> | --set-wallpaper <图片> | --full-calibration <输出根目录> | --prepare-loop <运行目录> | --resume-loop <运行目录> | --warp <标定JSON> <输入图> <输出目录> [固定X 固定Y] | --warp-folder <标定JSON> <输入文件夹> <输出文件夹> [固定X 固定Y] | --manual-adjust <基础标定> <输出标定> <输出点阵> <旋转> <左右透视> <上下透视> <宽%> <高%> <X> <Y> | --verify-pattern-consistency <标定JSON> <输出目录>");
        return 64;
    }
}
