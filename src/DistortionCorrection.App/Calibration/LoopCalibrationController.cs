using DistortionCorrection.Camera;
using DistortionCorrection.Imaging;
using DistortionCorrection.Models;
using DistortionCorrection.Projection;
using System.Drawing.Imaging;

namespace DistortionCorrection.Calibration;

public sealed record CalibrationProgress(
    string Stage,
    int Current,
    int Total,
    string Message,
    string? PreviewPath = null);

public sealed record CalibrationRunResult(
    string RunDirectory,
    string MeshCalibrationPath,
    string DenseMapPath,
    string FinalVerificationPatternPath,
    double FinalRmsPixels,
    int LoopIterations,
    int ValidDenseCells,
    int TotalDenseCells,
    RectangleF AutoInverseMappedBounds);

public sealed class LoopCalibrationController
{
    private readonly AppSettings _settings;
    private readonly ICameraService _camera;
    private readonly IDesktopDisplayService _display;
    private readonly IProgress<CalibrationProgress>? _progress;

    public LoopCalibrationController(
        AppSettings settings,
        ICameraService camera,
        IDesktopDisplayService display,
        IProgress<CalibrationProgress>? progress = null)
    {
        _settings = settings;
        _camera = camera;
        _display = display;
        _progress = progress;
    }

    public async Task<CalibrationRunResult> RunAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        _settings.Validate();
        if (!_camera.IsConnected) throw new InvalidOperationException("请先连接相机再开始回环标定。");
        string runDirectory = CreateRunDirectory(outputDirectory);
        string patternsDirectory = Path.Combine(runDirectory, "patterns");
        string capturesDirectory = Path.Combine(runDirectory, "captures");
        string overlaysDirectory = Path.Combine(runDirectory, "overlays");
        Directory.CreateDirectory(patternsDirectory);
        Directory.CreateDirectory(capturesDirectory);
        Directory.CreateDirectory(overlaysDirectory);
        _settings.Save(Path.Combine(runDirectory, "settings.json"));

        WallpaperSnapshot snapshot = _display.CaptureSnapshot();
        try
        {
            IReadOnlyList<PatternFile> patternFiles = GeneratePatternFiles(patternsDirectory);
            Report("Gray Code", 0, patternFiles.Count, $"已生成 {patternFiles.Count} 张图卡。");

            GrayImage black = await ProjectCaptureGrayAsync(patternFiles[0], capturesDirectory, 1,
                patternFiles.Count, cancellationToken);
            GrayImage white = await ProjectCaptureGrayAsync(patternFiles[1], capturesDirectory, 2,
                patternFiles.Count, cancellationToken);
            var accumulator = new GrayCodeAccumulator(_settings, black, white);

            for (int i = 2; i < patternFiles.Count; i += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatternFile normalFile = patternFiles[i];
                PatternFile inverseFile = patternFiles[i + 1];
                if (normalFile.Axis != inverseFile.Axis || normalFile.Bit != inverseFile.Bit ||
                    normalFile.Inverted || !inverseFile.Inverted)
                {
                    throw new InvalidDataException("Gray Code 正反图卡顺序损坏。");
                }
                GrayImage normal = await ProjectCaptureGrayAsync(normalFile, capturesDirectory, i + 1,
                    patternFiles.Count, cancellationToken);
                GrayImage inverse = await ProjectCaptureGrayAsync(inverseFile, capturesDirectory, i + 2,
                    patternFiles.Count, cancellationToken);
                accumulator.AddPair(normalFile.Axis, normalFile.Bit, normal, inverse);
            }

            Report("解码", 0, 1, "正在建立投影单元到相机像素的稠密映射……");
            DenseProjectorMap denseMap = accumulator.Complete();
            int validCells = denseMap.SamplesPerCell.Count(count => count > 0);
            int totalCells = denseMap.SamplesPerCell.Length;
            double coverage = validCells / (double)totalCells;
            if (coverage < 0.35)
            {
                throw new InvalidDataException(
                    $"Gray Code 有效覆盖率只有 {coverage:P1}，请提高曝光/增益、增大单元尺寸或降低最小对比度。");
            }
            string denseMapPath = Path.Combine(runDirectory, "projector-to-camera.dmap");
            CalibrationStorage.SaveDenseMap(denseMap, denseMapPath);
            // .dmap 是二进制映射文件，不可传给界面图片预览。
            Report("解码", 1, 1, $"稠密映射覆盖 {validCells}/{totalCells} ({coverage:P1})。");

            string cameraSerial = _camera.ConnectedCamera?.SerialNumber ?? _settings.PreferredCameraSerial;
            MeshCalibration mesh = DenseMapOperations.CreateInitialMesh(denseMap, _settings, cameraSerial);
            MeshGeometryDiagnostics initialGeometry = DenseMapOperations.ValidateProjectionMesh(mesh, _settings);
            mesh.DenseMapFile = Path.GetFileName(denseMapPath);
            string meshPath = Path.Combine(runDirectory, "calibration.json");
            CalibrationStorage.SaveMesh(mesh, meshPath);
            RectangleF initialBounds = MeshWarp.GetInverseMappedBounds(mesh);
            Report("逆映射", 0, 1,
                $"初始自然外接框：left={initialBounds.Left:F1}, top={initialBounds.Top:F1}, " +
                $"size={initialBounds.Width:F1}×{initialBounds.Height:F1}；" +
                $"预期 {_settings.ExpectedInverseMappedWidth}×{_settings.ExpectedInverseMappedHeight}。");

            double rms = double.PositiveInfinity;
            int completedIterations = 0;
            string finalPatternPath = string.Empty;
            for (int iteration = 0; iteration < _settings.MaximumLoopIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completedIterations = iteration + 1;
                string patternPath = Path.Combine(patternsDirectory, $"loop-{iteration:D2}-dot.png");
                using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(_settings, mesh))
                {
                    pattern.Save(patternPath, ImageFormat.Png);
                }
                finalPatternPath = patternPath;
                _display.SetWallpaper(patternPath);
                await Task.Delay(_settings.ProjectionSettleMilliseconds, cancellationToken);
                string capturePath = Path.Combine(capturesDirectory, $"loop-{iteration:D2}-dot.png");
                using Bitmap capture = await _camera.CaptureAsync(
                    _settings.CaptureTimeoutMilliseconds, cancellationToken);
                capture.Save(capturePath, ImageFormat.Png);
                PointGridDetectionResult detection = PointGridDetector.DetectNearExpected(
                    capture, mesh.DesiredCameraPoints, _settings.GridColumns, _settings.GridRows);
                string overlayPath = Path.Combine(overlaysDirectory, $"loop-{iteration:D2}-overlay.png");
                using (Bitmap overlay = PointGridDetector.DrawOverlay(capture, detection))
                {
                    overlay.Save(overlayPath, ImageFormat.Png);
                }
                rms = DenseMapOperations.UpdateMeshFromObservation(
                    mesh, denseMap, detection.OrderedPoints, _settings.LoopRelaxation);
                CalibrationStorage.SaveMesh(mesh, meshPath);
                Report("点阵回环", iteration + 1, _settings.MaximumLoopIterations,
                    $"第 {iteration + 1} 轮：RMS={rms:F3} camera px，识别阈值={detection.Threshold}。", overlayPath);
                if (rms <= _settings.TargetRmsPixels) break;
            }

            string corrected = Path.Combine(patternsDirectory,
                $"final-corrected-{_settings.GridColumns}x{_settings.GridRows}.png");
            using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(_settings, mesh))
            {
                pattern.Save(corrected, ImageFormat.Png);
            }
            finalPatternPath = corrected;
            CalibrationStorage.SaveMesh(mesh, meshPath);
            RectangleF finalBounds = MeshWarp.GetInverseMappedBounds(mesh);
            return new CalibrationRunResult(runDirectory, meshPath, denseMapPath, finalPatternPath,
                rms, completedIterations, validCells, totalCells, finalBounds);
        }
        finally
        {
            try { _display.RestoreSnapshot(snapshot); } catch { }
        }
    }

    private IReadOnlyList<PatternFile> GeneratePatternFiles(string directory)
    {
        IReadOnlyList<CalibrationPattern> patterns = PatternGenerator.CreateGrayCodeSequence(_settings);
        var result = new List<PatternFile>(patterns.Count);
        try
        {
            foreach (CalibrationPattern pattern in patterns)
            {
                string path = Path.Combine(directory, pattern.Name + ".png");
                pattern.Image.Save(path, ImageFormat.Png);
                result.Add(new PatternFile(path, pattern.Axis, pattern.Bit, pattern.Inverted));
            }
        }
        finally
        {
            foreach (CalibrationPattern pattern in patterns) pattern.Image.Dispose();
        }
        return result;
    }

    private async Task<GrayImage> ProjectCaptureGrayAsync(
        PatternFile pattern,
        string capturesDirectory,
        int current,
        int total,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _display.SetWallpaper(pattern.Path);
        await Task.Delay(_settings.ProjectionSettleMilliseconds, cancellationToken);
        using Bitmap capture = await _camera.CaptureAsync(_settings.CaptureTimeoutMilliseconds, cancellationToken);
        string capturePath = Path.Combine(capturesDirectory, Path.GetFileName(pattern.Path));
        capture.Save(capturePath, ImageFormat.Png);
        Report("Gray Code", current, total, $"投图并采集：{Path.GetFileName(pattern.Path)}", capturePath);
        return BitmapTools.ToGrayscale(capture);
    }

    private void Report(string stage, int current, int total, string message, string? preview = null) =>
        _progress?.Report(new CalibrationProgress(stage, current, total, message, preview));

    private static string CreateRunDirectory(string outputDirectory)
    {
        string directory = Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record PatternFile(string Path, GrayCodeAxis Axis, int Bit, bool Inverted);
}
