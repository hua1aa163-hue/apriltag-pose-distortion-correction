using DistortionCorrection.Camera;
using DistortionCorrection.Imaging;
using DistortionCorrection.Models;
using DistortionCorrection.Projection;
using System.Drawing.Imaging;

namespace DistortionCorrection.Calibration;

/// <summary>从已经成功保存的 Gray Code 稠密映射继续点阵回环，支持故障恢复。</summary>
public sealed class LoopOnlyController
{
    private readonly AppSettings _settings;
    private readonly ICameraService _camera;
    private readonly IDesktopDisplayService _display;
    private readonly IProgress<CalibrationProgress>? _progress;

    public LoopOnlyController(
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

    public async Task<CalibrationRunResult> RunAsync(string runDirectory, CancellationToken cancellationToken)
    {
        _settings.Validate();
        if (!_camera.IsConnected) throw new InvalidOperationException("请先连接相机。");
        string denseMapPath = Path.Combine(runDirectory, "projector-to-camera.dmap");
        DenseProjectorMap denseMap = CalibrationStorage.LoadDenseMap(denseMapPath);
        int validCells = denseMap.SamplesPerCell.Count(count => count > 0);
        int totalCells = denseMap.SamplesPerCell.Length;
        string patternsDirectory = Path.Combine(runDirectory, "patterns");
        string capturesDirectory = Path.Combine(runDirectory, "captures");
        string overlaysDirectory = Path.Combine(runDirectory, "overlays");
        Directory.CreateDirectory(patternsDirectory);
        Directory.CreateDirectory(capturesDirectory);
        Directory.CreateDirectory(overlaysDirectory);

        string cameraSerial = _camera.ConnectedCamera?.SerialNumber ?? _settings.PreferredCameraSerial;
        MeshCalibration mesh = DenseMapOperations.CreateInitialMesh(denseMap, _settings, cameraSerial);
        MeshGeometryDiagnostics initialGeometry = DenseMapOperations.ValidateProjectionMesh(mesh, _settings);
        mesh.DenseMapFile = Path.GetFileName(denseMapPath);
        string meshPath = Path.Combine(runDirectory, "calibration.json");
        CalibrationStorage.SaveMesh(mesh, meshPath);
        RectangleF initialBounds = MeshWarp.GetInverseMappedBounds(mesh);
        _progress?.Report(new CalibrationProgress("逆映射", 0, 1,
            $"初始自然外接框：left={initialBounds.Left:F1}, top={initialBounds.Top:F1}, " +
            $"size={initialBounds.Width:F1}×{initialBounds.Height:F1}；" +
            $"预期 {_settings.ExpectedInverseMappedWidth}×{_settings.ExpectedInverseMappedHeight}。"));

        WallpaperSnapshot snapshot = _display.CaptureSnapshot();
        try
        {
            double rms = -1;
            int completed = 0;
            for (int iteration = 0; iteration < _settings.MaximumLoopIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed = iteration + 1;
                string patternPath = Path.Combine(patternsDirectory, $"loop-{iteration:D2}-dot.png");
                using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(_settings, mesh))
                {
                    pattern.Save(patternPath, ImageFormat.Png);
                }
                _display.SetWallpaper(patternPath);
                await Task.Delay(_settings.ProjectionSettleMilliseconds, cancellationToken);
                string capturePath = Path.Combine(capturesDirectory, $"loop-{iteration:D2}-dot.png");
                using Bitmap capture = await _camera.CaptureAsync(_settings.CaptureTimeoutMilliseconds, cancellationToken);
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
                _progress?.Report(new CalibrationProgress("点阵回环", iteration + 1,
                    _settings.MaximumLoopIterations,
                    $"第 {iteration + 1} 轮：RMS={rms:F3} camera px，识别阈值={detection.Threshold}。", overlayPath));
                if (rms <= _settings.TargetRmsPixels) break;
            }

            string finalPattern = Path.Combine(patternsDirectory,
                $"final-corrected-{_settings.GridColumns}x{_settings.GridRows}.png");
            using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(_settings, mesh))
            {
                pattern.Save(finalPattern, ImageFormat.Png);
            }
            CalibrationStorage.SaveMesh(mesh, meshPath);
            RectangleF finalBounds = MeshWarp.GetInverseMappedBounds(mesh);
            return new CalibrationRunResult(runDirectory, meshPath, denseMapPath, finalPattern,
                rms, completed, validCells, totalCells, finalBounds);
        }
        finally
        {
            try { _display.RestoreSnapshot(snapshot); } catch { }
        }
    }
}
