using DistortionCorrection.Calibration;
using DistortionCorrection.Camera;
using DistortionCorrection.Imaging;
using DistortionCorrection.Models;
using DistortionCorrection.Projection;
using System.Drawing.Imaging;

namespace DistortionCorrection;

/// <summary>主窗口只负责协调四个可由 WinForms Designer 单独编辑的子界面。</summary>
public partial class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly HikCameraService _camera = new();
    private readonly DesktopDisplayService _display = new();
    private WallpaperSnapshot? _manualWallpaperSnapshot;
    private CancellationTokenSource? _operationCancellation;
    private string? _manualPreviewSignature;
    private string? _cameraConfigurationSignature;

    public MainForm()
    {
        try { _settings = AppSettings.Load(); }
        catch { _settings = new AppSettings(); }

        InitializeComponent();
        InitializeValues();
        WireEvents();
        Shown += async (_, _) => await RunBusyAsync("枚举相机", _ => EnumerateCamerasAsync());
    }

    private void InitializeValues()
    {
        cameraPage.SetExposure.Checked = _settings.ExposureTimeMicroseconds.HasValue;
        cameraPage.Exposure.Value = ClampDecimal(_settings.ExposureTimeMicroseconds ?? 10000,
            cameraPage.Exposure.Minimum, cameraPage.Exposure.Maximum);
        cameraPage.SetGain.Checked = _settings.Gain.HasValue;
        cameraPage.Gain.Value = ClampDecimal(_settings.Gain ?? 0,
            cameraPage.Gain.Minimum, cameraPage.Gain.Maximum);
        cameraPage.LensCorrectionEnabled.Checked = _settings.LensCorrectionEnabled;
        cameraPage.LensCalibrationPath.Text = _settings.LensCalibrationPath;
        cameraPage.ChessboardFolder.Text = _settings.LensChessboardImageDirectory;
        cameraPage.ChessboardColumns.Value = Math.Clamp(_settings.LensChessboardColumns,
            (int)cameraPage.ChessboardColumns.Minimum, (int)cameraPage.ChessboardColumns.Maximum);
        cameraPage.ChessboardRows.Value = Math.Clamp(_settings.LensChessboardRows,
            (int)cameraPage.ChessboardRows.Minimum, (int)cameraPage.ChessboardRows.Maximum);
        cameraPage.ChessboardSquareSize.Value = ClampDecimal(_settings.LensChessboardSquareSize,
            cameraPage.ChessboardSquareSize.Minimum, cameraPage.ChessboardSquareSize.Maximum);
        cameraPage.LensAlpha.Value = ClampDecimal(_settings.LensCorrectionAlpha,
            cameraPage.LensAlpha.Minimum, cameraPage.LensAlpha.Maximum);
        calibrationPage.RunRoot.Text = _settings.OutputDirectory;
        calibrationPage.GridColumns.Value = _settings.GridColumns;
        calibrationPage.GridRows.Value = _settings.GridRows;
        calibrationPage.TargetWidth.Value = _settings.ExpectedInverseMappedWidth;
        calibrationPage.TargetHeight.Value = _settings.ExpectedInverseMappedHeight;
        calibrationPage.DotDiameter.Value = _settings.DotDiameter;
        batchWarpPage.OutputFolder.Text = Path.Combine(_settings.OutputDirectory, "Warped");
        UpdateLensStatus();
        UpdateCalibrationSummary();
    }

    private void WireEvents()
    {
        cameraPage.RefreshButton.Click += async (_, _) =>
            await RunBusyAsync("刷新设备", _ => EnumerateCamerasAsync());
        cameraPage.ConnectButton.Click += async (_, _) =>
            await RunBusyAsync("连接相机", _ => { EnsureCameraConnected(); return Task.CompletedTask; });
        cameraPage.CaptureButton.Click += async (_, _) =>
            await RunBusyAsync("抓取单帧", CaptureSingleAsync);
        cameraPage.BrowseLensCalibrationButton.Click += (_, _) =>
        {
            BrowseFile(cameraPage.LensCalibrationPath, "镜头标定 JSON|*.json|所有文件|*.*");
            _cameraConfigurationSignature = null;
            UpdateLensStatus();
        };
        cameraPage.BrowseChessboardFolderButton.Click += (_, _) => BrowseFolder(cameraPage.ChessboardFolder);
        cameraPage.CalibrateLensButton.Click += async (_, _) =>
            await RunBusyAsync("OpenCV 棋盘格镜头标定", CalibrateLensAsync);
        cameraPage.LensCorrectionEnabled.CheckedChanged += (_, _) =>
        {
            _cameraConfigurationSignature = null;
            UpdateLensStatus();
        };
        cameraPage.LensCalibrationPath.TextChanged += (_, _) => _cameraConfigurationSignature = null;
        cameraPage.LensAlpha.ValueChanged += (_, _) => _cameraConfigurationSignature = null;

        projectionPage.BrowseButton.Click += (_, _) => BrowseImage(projectionPage.ImagePath);
        projectionPage.ProjectButton.Click += async (_, _) =>
            await RunBusyAsync("切换投影图片", ProjectSelectedAsync);
        projectionPage.RestoreButton.Click += async (_, _) =>
            await RunBusyAsync("恢复桌面", _ => RestoreWallpaperAsync());

        calibrationPage.BrowseButton.Click += (_, _) => BrowseFolder(calibrationPage.RunRoot);
        calibrationPage.SaveParametersButton.Click += async (_, _) =>
            await RunBusyAsync("保存参数", _ => { ApplyCalibrationParameters(save: true); return Task.CompletedTask; });
        calibrationPage.FullCalibrationButton.Click += async (_, _) =>
            await RunBusyAsync("完整回环标定", FullCalibrationAsync);
        calibrationPage.ResumeCalibrationButton.Click += async (_, _) =>
            await RunBusyAsync("继续点阵回环", ResumeCalibrationAsync);
        calibrationPage.GridColumns.ValueChanged += (_, _) => UpdateCalibrationSummary();
        calibrationPage.GridRows.ValueChanged += (_, _) => UpdateCalibrationSummary();
        calibrationPage.TargetWidth.ValueChanged += (_, _) => UpdateCalibrationSummary();
        calibrationPage.TargetHeight.ValueChanged += (_, _) => UpdateCalibrationSummary();

        manualAdjustmentPage.BrowseBaseButton.Click += (_, _) =>
        {
            BrowseFile(manualAdjustmentPage.BaseCalibrationPath, "标定 JSON|*.json|所有文件|*.*");
            if (File.Exists(manualAdjustmentPage.BaseCalibrationPath.Text))
                manualAdjustmentPage.OutputCalibrationPath.Text = SuggestedManualCalibrationPath(
                    manualAdjustmentPage.BaseCalibrationPath.Text);
            _manualPreviewSignature = null;
        };
        manualAdjustmentPage.BrowseOutputButton.Click += (_, _) =>
            BrowseSaveFile(manualAdjustmentPage.OutputCalibrationPath, "标定 JSON|*.json", "calibration-manual.json");
        manualAdjustmentPage.ResetButton.Click += async (_, _) =>
            await RunBusyAsync("微调参数归零", _ =>
            {
                ResetManualAdjustmentControls();
                return Task.CompletedTask;
            });
        manualAdjustmentPage.PreviewButton.Click += async (_, _) =>
            await RunBusyAsync("生成并投影人眼确认图", PreviewManualAdjustmentAsync);
        manualAdjustmentPage.ConfirmButton.Click += async (_, _) =>
            await RunBusyAsync("确认人眼微调", ConfirmManualAdjustmentAsync);
        manualAdjustmentPage.RestoreDesktopButton.Click += async (_, _) =>
            await RunBusyAsync("恢复桌面", _ => RestoreWallpaperAsync());

        batchWarpPage.BrowseCalibrationButton.Click += (_, _) =>
            BrowseFile(batchWarpPage.CalibrationPath, "标定 JSON|*.json|所有文件|*.*");
        batchWarpPage.BrowseInputButton.Click += (_, _) => BrowseFolder(batchWarpPage.InputFolder);
        batchWarpPage.BrowseOutputButton.Click += (_, _) => BrowseFolder(batchWarpPage.OutputFolder);
        batchWarpPage.StartButton.Click += async (_, _) =>
            await RunBusyAsync("批量畸变矫正", BatchWarpAsync);
        batchWarpPage.CenteredContentStartButton.Click += async (_, _) =>
            await RunBusyAsync("居中矩形内容批量畸变矫正", BatchWarpCenteredContentAsync);

        cancelButton.Click += (_, _) => _operationCancellation?.Cancel();
    }

    private async Task EnumerateCamerasAsync()
    {
        IReadOnlyList<CameraDescriptor> devices = _camera.Enumerate();
        cameraPage.CameraList.Items.Clear();
        foreach (CameraDescriptor descriptor in devices) cameraPage.CameraList.Items.Add(descriptor);
        if (devices.Count > 0)
        {
            int preferred = devices.ToList().FindIndex(device =>
                string.Equals(device.SerialNumber, _settings.PreferredCameraSerial, StringComparison.OrdinalIgnoreCase));
            cameraPage.CameraList.SelectedIndex = preferred >= 0 ? preferred : 0;
        }
        Log($"发现 {devices.Count} 台相机。" +
            (devices.Count > 0 ? $" 当前：{cameraPage.CameraList.SelectedItem}" : string.Empty));
        await Task.CompletedTask;
    }

    private void EnsureCameraConnected()
    {
        if (cameraPage.CameraList.SelectedItem is not CameraDescriptor descriptor)
            throw new InvalidOperationException("未选择可用相机，请先刷新设备列表。");

        ApplyCameraParameters(save: false);
        string signature = CurrentCameraConfigurationSignature(descriptor);
        if (_camera.IsConnected && string.Equals(signature, _cameraConfigurationSignature,
            StringComparison.Ordinal)) return;
        if (_camera.IsConnected)
        {
            _camera.Disconnect();
            Log("相机或取帧参数已改变，正在重新连接以应用新配置。");
        }
        _camera.Connect(descriptor, _settings);
        _cameraConfigurationSignature = signature;
        Log($"相机已连接：{descriptor}；镜头矫正：" +
            (_settings.LensCorrectionEnabled
                ? $"已启用，Alpha={_settings.LensCorrectionAlpha:F2}"
                : "未启用"));
    }

    private void ApplyCameraParameters(bool save)
    {
        _settings.ExposureTimeMicroseconds = cameraPage.SetExposure.Checked
            ? (double)cameraPage.Exposure.Value : null;
        _settings.Gain = cameraPage.SetGain.Checked ? (double)cameraPage.Gain.Value : null;
        _settings.LensCorrectionEnabled = cameraPage.LensCorrectionEnabled.Checked;
        _settings.LensCalibrationPath = string.IsNullOrWhiteSpace(cameraPage.LensCalibrationPath.Text)
            ? string.Empty : Path.GetFullPath(cameraPage.LensCalibrationPath.Text);
        _settings.LensChessboardImageDirectory = string.IsNullOrWhiteSpace(cameraPage.ChessboardFolder.Text)
            ? string.Empty : Path.GetFullPath(cameraPage.ChessboardFolder.Text);
        _settings.LensChessboardColumns = (int)cameraPage.ChessboardColumns.Value;
        _settings.LensChessboardRows = (int)cameraPage.ChessboardRows.Value;
        _settings.LensChessboardSquareSize = (double)cameraPage.ChessboardSquareSize.Value;
        _settings.LensCorrectionAlpha = (double)cameraPage.LensAlpha.Value;
        _settings.Validate();
        if (_settings.LensCorrectionEnabled) RequireFile(_settings.LensCalibrationPath, "镜头标定 JSON");
        if (save) _settings.Save();
    }

    private string CurrentCameraConfigurationSignature(CameraDescriptor descriptor)
    {
        DateTime calibrationWriteTime = File.Exists(_settings.LensCalibrationPath)
            ? File.GetLastWriteTimeUtc(_settings.LensCalibrationPath) : DateTime.MinValue;
        return FormattableString.Invariant(
            $"{descriptor.Id}|{_settings.ExposureTimeMicroseconds}|{_settings.Gain}|{_settings.LensCorrectionEnabled}|{_settings.LensCalibrationPath}|{calibrationWriteTime.Ticks}|{_settings.LensCorrectionAlpha}");
    }

    private async Task CalibrateLensAsync(CancellationToken cancellationToken)
    {
        RequireDirectory(cameraPage.ChessboardFolder.Text, "棋盘格图片文件夹");
        string imageDirectory = Path.GetFullPath(cameraPage.ChessboardFolder.Text);
        string suggestedPath = string.IsNullOrWhiteSpace(cameraPage.LensCalibrationPath.Text)
            ? Path.Combine(imageDirectory, "lens-calibration.json")
            : Path.GetFullPath(cameraPage.LensCalibrationPath.Text);
        using var dialog = new SaveFileDialog
        {
            Filter = "镜头标定 JSON|*.json",
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        int columns = (int)cameraPage.ChessboardColumns.Value;
        int rows = (int)cameraPage.ChessboardRows.Value;
        double squareSize = (double)cameraPage.ChessboardSquareSize.Value;
        string serial = cameraPage.CameraList.SelectedItem is CameraDescriptor camera
            ? camera.SerialNumber : _settings.PreferredCameraSerial;
        cameraPage.LensStatus.Text = "正在用 OpenCV 识别棋盘格并计算内参……";

        LensCalibrationResult result = await Task.Run(() =>
            OpenCvLensCorrectionService.CalibrateFromChessboardFolder(
                imageDirectory, columns, rows, squareSize, serial, cancellationToken), cancellationToken);
        result.Calibration.Save(dialog.FileName);

        cameraPage.LensCalibrationPath.Text = dialog.FileName;
        cameraPage.LensCorrectionEnabled.Checked = true;
        _cameraConfigurationSignature = null;
        ApplyCameraParameters(save: true);
        cameraPage.LensStatus.Text =
            $"标定完成：{result.Calibration.ImageWidth}×{result.Calibration.ImageHeight}，" +
            $"有效 {result.AcceptedImages.Count} 张，拒绝 {result.RejectedImages.Count} 张，" +
            $"RMS={result.Calibration.RmsReprojectionError:F3}px。下一次取帧自动应用。";
        Log(cameraPage.LensStatus.Text);
        foreach (string rejected in result.RejectedImages.Take(10)) Log("棋盘格图片跳过：" + rejected);
        if (result.RejectedImages.Count > 10)
            Log($"另有 {result.RejectedImages.Count - 10} 张被拒绝的图片未展开。");
        Log("镜头矫正已启用。相机像素坐标已经改变，必须重新执行完整 Gray Code + 点阵回环标定，不能续用旧运行目录。");
    }

    private void UpdateLensStatus()
    {
        if (!cameraPage.LensCorrectionEnabled.Checked)
        {
            cameraPage.LensStatus.Text = "未启用。建议准备 12–20 张不同角度、不同位置的棋盘格照片。";
            return;
        }

        try
        {
            LensCalibration calibration = LensCalibration.Load(cameraPage.LensCalibrationPath.Text);
            cameraPage.LensStatus.Text =
                $"已选择：{calibration.ImageWidth}×{calibration.ImageHeight}，" +
                $"{calibration.UsedImageCount} 张，RMS={calibration.RmsReprojectionError:F3}px；" +
                "修改后将在下一次取帧时自动重连。";
        }
        catch (Exception exception)
        {
            cameraPage.LensStatus.Text = "无法启用：" + exception.Message;
        }
    }

    private async Task CaptureSingleAsync(CancellationToken cancellationToken)
    {
        EnsureCameraConnected();
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using Bitmap capture = await _camera.CaptureAsync(_settings.CaptureTimeoutMilliseconds, cancellationToken);
        capture.Save(dialog.FileName, ImageFormat.Png);
        ShowPreview(dialog.FileName);
        Log($"已抓取 {capture.Width}×{capture.Height}：{dialog.FileName}");
    }

    private async Task ProjectSelectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireFile(projectionPage.ImagePath.Text, "投图图片");
        _manualWallpaperSnapshot ??= _display.CaptureSnapshot();
        _display.SetWallpaper(projectionPage.ImagePath.Text);
        ShowPreview(projectionPage.ImagePath.Text);
        Log($"已切换桌面图：{projectionPage.ImagePath.Text}");
        await Task.CompletedTask;
    }

    private async Task RestoreWallpaperAsync()
    {
        if (_manualWallpaperSnapshot is null) return;
        _display.RestoreSnapshot(_manualWallpaperSnapshot);
        _manualWallpaperSnapshot = null;
        Log("已恢复手动切图前的桌面。");
        await Task.CompletedTask;
    }

    private async Task FullCalibrationAsync(CancellationToken cancellationToken)
    {
        ApplyCalibrationParameters(save: true);
        EnsureCameraConnected();
        Directory.CreateDirectory(calibrationPage.RunRoot.Text);
        var progress = new Progress<CalibrationProgress>(ReportProgress);
        var controller = new LoopCalibrationController(_settings, _camera, _display, progress);
        CalibrationRunResult result = await controller.RunAsync(
            calibrationPage.RunRoot.Text, cancellationToken);
        AcceptCalibrationResult(result);
    }

    private async Task ResumeCalibrationAsync(CancellationToken cancellationToken)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 projector-to-camera.dmap 的运行目录",
            InitialDirectory = Directory.Exists(calibrationPage.RunRoot.Text)
                ? calibrationPage.RunRoot.Text : string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ApplyCalibrationParameters(save: false);
        string settingsPath = Path.Combine(dialog.SelectedPath, "settings.json");
        AppSettings runSettings = File.Exists(settingsPath) ? AppSettings.Load(settingsPath) : new AppSettings();
        if (!SameLensCorrectionConfiguration(runSettings, _settings))
        {
            throw new InvalidOperationException(
                "当前镜头矫正配置与该回环目录不一致，旧相机坐标不能继续使用。" +
                "请保持原镜头配置，或重新执行“完整回环标定”。");
        }
        EnsureCameraConnected();
        // 稠密 .dmap 与点阵行列/目标外接尺寸无关，继续回环时采用界面上当前填写的参数。
        runSettings.GridColumns = _settings.GridColumns;
        runSettings.GridRows = _settings.GridRows;
        runSettings.ExpectedInverseMappedWidth = _settings.ExpectedInverseMappedWidth;
        runSettings.ExpectedInverseMappedHeight = _settings.ExpectedInverseMappedHeight;
        runSettings.DotDiameter = _settings.DotDiameter;
        runSettings.Validate();
        runSettings.Save(settingsPath);

        var progress = new Progress<CalibrationProgress>(ReportProgress);
        var controller = new LoopOnlyController(runSettings, _camera, _display, progress);
        CalibrationRunResult result = await controller.RunAsync(dialog.SelectedPath, cancellationToken);
        AcceptCalibrationResult(result);
    }

    private async Task BatchWarpAsync(CancellationToken cancellationToken)
    {
        RequireFile(batchWarpPage.CalibrationPath.Text, "标定 JSON");
        RequireDirectory(batchWarpPage.InputFolder.Text, "输入文件夹");
        if (string.IsNullOrWhiteSpace(batchWarpPage.OutputFolder.Text))
            throw new InvalidOperationException("请选择输出文件夹。");

        string calibrationPath = Path.GetFullPath(batchWarpPage.CalibrationPath.Text);
        string inputFolder = Path.GetFullPath(batchWarpPage.InputFolder.Text);
        string outputFolder = Path.GetFullPath(batchWarpPage.OutputFolder.Text);
        Point fixedTopLeft = new((int)batchWarpPage.FixedX.Value, (int)batchWarpPage.FixedY.Value);
        BatchPlacementModes modes = batchWarpPage.Placement.SelectedIndex switch
        {
            0 => BatchPlacementModes.Auto,
            1 => BatchPlacementModes.Fixed,
            _ => BatchPlacementModes.Both
        };
        batchWarpPage.Progress.Value = 0;
        batchWarpPage.Status.Text = "正在读取文件列表……";
        var progress = new Progress<BatchWarpProgress>(item =>
        {
            batchWarpPage.Progress.Maximum = Math.Max(1, item.Total);
            batchWarpPage.Progress.Value = Math.Clamp(item.Current, 0, batchWarpPage.Progress.Maximum);
            batchWarpPage.Status.Text = $"{item.Current}/{item.Total}  {Path.GetFileName(item.SourcePath)}  {item.Message}";
        });

        BatchWarpResult result = await Task.Run(() =>
        {
            MeshCalibration calibration = CalibrationStorage.LoadMesh(calibrationPath);
            return BatchWarpService.WarpFolder(
                inputFolder, outputFolder, calibration, modes, fixedTopLeft,
                batchWarpPage.Recursive.Checked, progress, cancellationToken);
        }, cancellationToken);

        batchWarpPage.Progress.Maximum = Math.Max(1, result.DiscoveredFiles);
        batchWarpPage.Progress.Value = result.DiscoveredFiles;
        batchWarpPage.Status.Text =
            $"完成：输入 {result.DiscoveredFiles}，成功 {result.SuccessfulFiles}，输出 {result.OutputFiles}，失败 {result.Failures.Count}";
        if (result.LastOutputPath is not null)
        {
            ShowPreview(result.LastOutputPath);
            projectionPage.ImagePath.Text = result.LastOutputPath;
        }
        Log(batchWarpPage.Status.Text);
        foreach (BatchWarpFailure failure in result.Failures.Take(20))
            Log($"批处理失败：{failure.SourcePath} | {failure.Error}");
        if (result.Failures.Count > 20) Log($"另有 {result.Failures.Count - 20} 个失败项未在日志展开。");
    }

    private async Task BatchWarpCenteredContentAsync(CancellationToken cancellationToken)
    {
        RequireFile(batchWarpPage.CalibrationPath.Text, "标定 JSON");
        RequireDirectory(batchWarpPage.InputFolder.Text, "输入文件夹");
        if (string.IsNullOrWhiteSpace(batchWarpPage.OutputFolder.Text))
            throw new InvalidOperationException("请选择输出文件夹。");

        string calibrationPath = Path.GetFullPath(batchWarpPage.CalibrationPath.Text);
        string inputFolder = Path.GetFullPath(batchWarpPage.InputFolder.Text);
        string outputFolder = Path.GetFullPath(batchWarpPage.OutputFolder.Text);
        Point fixedTopLeft = new((int)batchWarpPage.FixedX.Value, (int)batchWarpPage.FixedY.Value);
        BatchPlacementModes modes = batchWarpPage.Placement.SelectedIndex switch
        {
            0 => BatchPlacementModes.Auto,
            1 => BatchPlacementModes.Fixed,
            _ => BatchPlacementModes.Both
        };
        var options = new CenteredContentWarpOptions(
            (int)batchWarpPage.CenteredInputWidth.Value,
            (int)batchWarpPage.CenteredInputHeight.Value,
            (int)batchWarpPage.CenteredOutputWidth.Value,
            (int)batchWarpPage.CenteredOutputHeight.Value);

        batchWarpPage.Progress.Value = 0;
        batchWarpPage.Status.Text = "正在读取居中矩形图片列表……";
        var progress = new Progress<BatchWarpProgress>(item =>
        {
            batchWarpPage.Progress.Maximum = Math.Max(1, item.Total);
            batchWarpPage.Progress.Value = Math.Clamp(item.Current, 0, batchWarpPage.Progress.Maximum);
            batchWarpPage.Status.Text =
                $"{item.Current}/{item.Total}  {Path.GetFileName(item.SourcePath)}  {item.Message}";
        });

        BatchWarpResult result = await Task.Run(() =>
        {
            MeshCalibration calibration = CalibrationStorage.LoadMesh(calibrationPath);
            return BatchWarpService.WarpCenteredContentFolder(
                inputFolder, outputFolder, calibration, modes, fixedTopLeft,
                batchWarpPage.Recursive.Checked, options, progress, cancellationToken);
        }, cancellationToken);

        batchWarpPage.Progress.Maximum = Math.Max(1, result.DiscoveredFiles);
        batchWarpPage.Progress.Value = result.DiscoveredFiles;
        batchWarpPage.Status.Text =
            $"居中矩形完成：输入 {result.DiscoveredFiles}，成功 {result.SuccessfulFiles}，" +
            $"输出 {result.OutputFiles}，失败 {result.Failures.Count}";
        if (result.LastOutputPath is not null)
        {
            ShowPreview(result.LastOutputPath);
            projectionPage.ImagePath.Text = result.LastOutputPath;
        }
        Log($"{batchWarpPage.Status.Text}；输入中心 {options.InputContentWidth}×{options.InputContentHeight}，" +
            $"输出外接 {options.OutputContentWidth}×{options.OutputContentHeight}");
        foreach (BatchWarpFailure failure in result.Failures.Take(20))
            Log($"居中矩形批处理失败：{failure.SourcePath} | {failure.Error}");
        if (result.Failures.Count > 20) Log($"另有 {result.Failures.Count - 20} 个失败项未在日志展开。");
    }

    private async Task PreviewManualAdjustmentAsync(CancellationToken cancellationToken)
    {
        RequireFile(manualAdjustmentPage.BaseCalibrationPath.Text, "基础标定 JSON");
        ManualAdjustmentResult adjusted = await Task.Run(CreateManualAdjustment, cancellationToken);
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(
            manualAdjustmentPage.BaseCalibrationPath.Text))!;
        string previewDirectory = Path.Combine(baseDirectory, "manual-preview");
        Directory.CreateDirectory(previewDirectory);
        string calibrationPath = Path.Combine(previewDirectory, "calibration-manual-preview.json");
        string patternPath = Path.Combine(previewDirectory,
            $"manual-confirmation-{adjusted.Calibration.Columns}x{adjusted.Calibration.Rows}.png");
        CalibrationStorage.SaveMesh(adjusted.Calibration, calibrationPath);
        AppSettings patternSettings = SettingsForCalibration(adjusted.Calibration);
        using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(patternSettings, adjusted.Calibration))
            pattern.Save(patternPath, ImageFormat.Png);

        cancellationToken.ThrowIfCancellationRequested();
        _manualWallpaperSnapshot ??= _display.CaptureSnapshot();
        _display.SetWallpaper(patternPath);
        projectionPage.ImagePath.Text = patternPath;
        ShowPreview(patternPath);
        _manualPreviewSignature = CurrentManualAdjustmentSignature();
        manualAdjustmentPage.Status.Text =
            $"正在投影确认图：left={adjusted.Geometry.Bounds.Left:F1}, top={adjusted.Geometry.Bounds.Top:F1}, " +
            $"size={adjusted.Geometry.Bounds.Width:F1}×{adjusted.Geometry.Bounds.Height:F1}。" +
            "观察后可继续修改参数并重新投影；满意后点击“人眼确认并用于批处理”。";
        Log(manualAdjustmentPage.Status.Text);
    }

    private async Task ConfirmManualAdjustmentAsync(CancellationToken cancellationToken)
    {
        RequireFile(manualAdjustmentPage.BaseCalibrationPath.Text, "基础标定 JSON");
        if (_manualPreviewSignature != CurrentManualAdjustmentSignature())
            throw new InvalidOperationException("当前参数尚未投影确认，或投影后又发生了变化。请先重新生成并投影确认图。");
        if (string.IsNullOrWhiteSpace(manualAdjustmentPage.OutputCalibrationPath.Text))
            throw new InvalidOperationException("请选择确认后标定文件的保存路径。");
        string outputPath = Path.GetFullPath(manualAdjustmentPage.OutputCalibrationPath.Text);
        if (string.Equals(outputPath, Path.GetFullPath(manualAdjustmentPage.BaseCalibrationPath.Text),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("确认后标定不能覆盖基础标定，请另存为新文件。");
        }

        ManualAdjustmentResult adjusted = await Task.Run(CreateManualAdjustment, cancellationToken);
        CalibrationStorage.SaveMesh(adjusted.Calibration, outputPath);
        string patternPath = Path.Combine(Path.GetDirectoryName(outputPath)!,
            Path.GetFileNameWithoutExtension(outputPath) + "-confirmation.png");
        AppSettings patternSettings = SettingsForCalibration(adjusted.Calibration);
        using (Bitmap pattern = PatternGenerator.CreateWarpedDotPattern(patternSettings, adjusted.Calibration))
            pattern.Save(patternPath, ImageFormat.Png);

        batchWarpPage.CalibrationPath.Text = outputPath;
        projectionPage.ImagePath.Text = patternPath;
        ShowPreview(patternPath);
        manualAdjustmentPage.Status.Text =
            $"已人眼确认并保存：{outputPath}。文件夹批处理已自动选用此标定。";
        Log(manualAdjustmentPage.Status.Text);
        await Task.CompletedTask;
    }

    private ManualAdjustmentResult CreateManualAdjustment()
    {
        MeshCalibration source = CalibrationStorage.LoadMesh(manualAdjustmentPage.BaseCalibrationPath.Text);
        return ManualCalibrationAdjustment.Apply(source, ReadManualAdjustment());
    }

    private ManualAdjustmentSettings ReadManualAdjustment() => new()
    {
        RotationDegrees = (double)manualAdjustmentPage.Rotation.Value,
        HorizontalPerspectivePercent = (double)manualAdjustmentPage.HorizontalPerspective.Value,
        VerticalPerspectivePercent = (double)manualAdjustmentPage.VerticalPerspective.Value,
        HorizontalScalePercent = (double)manualAdjustmentPage.HorizontalScale.Value,
        VerticalScalePercent = (double)manualAdjustmentPage.VerticalScale.Value,
        OffsetX = (double)manualAdjustmentPage.OffsetX.Value,
        OffsetY = (double)manualAdjustmentPage.OffsetY.Value
    };

    private void ResetManualAdjustmentControls()
    {
        manualAdjustmentPage.Rotation.Value = 0;
        manualAdjustmentPage.HorizontalPerspective.Value = 0;
        manualAdjustmentPage.VerticalPerspective.Value = 0;
        manualAdjustmentPage.HorizontalScale.Value = 100;
        manualAdjustmentPage.VerticalScale.Value = 100;
        manualAdjustmentPage.OffsetX.Value = 0;
        manualAdjustmentPage.OffsetY.Value = 0;
        manualAdjustmentPage.Status.Text = "参数已归零。请生成并投影确认图。";
        _manualPreviewSignature = null;
    }

    private string CurrentManualAdjustmentSignature() => string.Join("|",
        Path.GetFullPath(manualAdjustmentPage.BaseCalibrationPath.Text),
        manualAdjustmentPage.Rotation.Value,
        manualAdjustmentPage.HorizontalPerspective.Value,
        manualAdjustmentPage.VerticalPerspective.Value,
        manualAdjustmentPage.HorizontalScale.Value,
        manualAdjustmentPage.VerticalScale.Value,
        manualAdjustmentPage.OffsetX.Value,
        manualAdjustmentPage.OffsetY.Value);

    private AppSettings SettingsForCalibration(MeshCalibration calibration) => new()
    {
        GridColumns = calibration.Columns,
        GridRows = calibration.Rows,
        DotDiameter = _settings.DotDiameter,
        ExpectedInverseMappedWidth = calibration.ExpectedInverseMappedWidth,
        ExpectedInverseMappedHeight = calibration.ExpectedInverseMappedHeight
    };

    private void ApplyCalibrationParameters(bool save)
    {
        ApplyCameraParameters(save: false);
        _settings.OutputDirectory = Path.GetFullPath(calibrationPage.RunRoot.Text);
        _settings.GridColumns = (int)calibrationPage.GridColumns.Value;
        _settings.GridRows = (int)calibrationPage.GridRows.Value;
        _settings.ExpectedInverseMappedWidth = (int)calibrationPage.TargetWidth.Value;
        _settings.ExpectedInverseMappedHeight = (int)calibrationPage.TargetHeight.Value;
        _settings.DotDiameter = (int)calibrationPage.DotDiameter.Value;
        _settings.Validate();
        if (save)
        {
            _settings.Save();
            Log("参数已保存。");
        }
        UpdateCalibrationSummary();
    }

    private static bool SameLensCorrectionConfiguration(AppSettings left, AppSettings right)
    {
        if (left.LensCorrectionEnabled != right.LensCorrectionEnabled) return false;
        if (!left.LensCorrectionEnabled) return true;
        string leftPath = Path.GetFullPath(left.LensCalibrationPath);
        string rightPath = Path.GetFullPath(right.LensCalibrationPath);
        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase) &&
               Math.Abs(left.LensCorrectionAlpha - right.LensCorrectionAlpha) < 1e-9;
    }

    private void UpdateCalibrationSummary()
    {
        int columns = (int)calibrationPage.GridColumns.Value;
        int rows = (int)calibrationPage.GridRows.Value;
        calibrationPage.Summary.Text =
            $"点阵 {columns}×{rows}（{columns * rows} 点）；目标外接 " +
            $"{calibrationPage.TargetWidth.Value}×{calibrationPage.TargetHeight.Value}；" +
            "输出画布固定 1920×1080，边缘填黑。";
    }

    private void AcceptCalibrationResult(CalibrationRunResult result)
    {
        manualAdjustmentPage.BaseCalibrationPath.Text = result.MeshCalibrationPath;
        manualAdjustmentPage.OutputCalibrationPath.Text = SuggestedManualCalibrationPath(result.MeshCalibrationPath);
        batchWarpPage.CalibrationPath.Clear();
        ResetManualAdjustmentControls();
        projectionPage.ImagePath.Text = result.FinalVerificationPatternPath;
        calibrationPage.RunRoot.Text = Path.GetDirectoryName(result.RunDirectory) ?? calibrationPage.RunRoot.Text;
        ShowPreview(result.FinalVerificationPatternPath);
        Log($"标定完成：RMS={result.FinalRmsPixels:F3}px，回环={result.LoopIterations}，" +
            $"自然外接框={result.AutoInverseMappedBounds.Left:F1},{result.AutoInverseMappedBounds.Top:F1} " +
            $"{result.AutoInverseMappedBounds.Width:F1}×{result.AutoInverseMappedBounds.Height:F1}");
        Log($"标定文件：{result.MeshCalibrationPath}");
        navigationTabControl.SelectedTab = manualAdjustmentTabPage;
    }

    private void ReportProgress(CalibrationProgress progress)
    {
        Log($"[{progress.Stage}] {progress.Current}/{progress.Total} {progress.Message}");
        if (!string.IsNullOrWhiteSpace(progress.PreviewPath)) ShowPreview(progress.PreviewPath);
    }

    private async Task RunBusyAsync(string name, Func<CancellationToken, Task> action)
    {
        if (_operationCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetBusy(true, name);
        try
        {
            Log($"--- {name} ---");
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Log("操作已取消。");
        }
        catch (Exception exception)
        {
            Log("错误：" + exception.Message);
            MessageBox.Show(this, exception.ToString(), name + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operationCancellation = null;
            SetBusy(false, "就绪");
        }
    }

    private IEnumerable<Button> ActionButtons =>
        cameraPage.ActionButtons
            .Concat(projectionPage.ActionButtons)
            .Concat(calibrationPage.ActionButtons)
            .Concat(manualAdjustmentPage.ActionButtons)
            .Concat(batchWarpPage.ActionButtons);

    private void SetBusy(bool busy, string operation)
    {
        foreach (Button button in ActionButtons) button.Enabled = !busy;
        cancelButton.Enabled = busy;
        operationLabel.Text = busy ? "正在执行：" + operation : operation;
        UseWaitCursor = busy;
    }

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(message))); return; }
        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void ShowPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        string extension = Path.GetExtension(path);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            Log($"跳过非图片预览：{Path.GetFileName(path)}");
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using Image loaded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            var next = new Bitmap(loaded);
            Image? old = previewPictureBox.Image;
            previewPictureBox.Image = next;
            old?.Dispose();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            // 预览失败不能中断标定或批处理主流程。
            Log($"图片预览失败，已跳过：{path} | {exception.Message}");
        }
    }

    private void BrowseImage(TextBox target) =>
        BrowseFile(target, "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*");

    private void BrowseFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (File.Exists(target.Text)) dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void BrowseSaveFile(TextBox target, string filter, string defaultName)
    {
        using var dialog = new SaveFileDialog { Filter = filter, FileName = defaultName };
        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            string fullPath = Path.GetFullPath(target.Text);
            dialog.InitialDirectory = Path.GetDirectoryName(fullPath);
            dialog.FileName = Path.GetFileName(fullPath);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private static string SuggestedManualCalibrationPath(string baseCalibrationPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseCalibrationPath))!, "calibration-manual.json");

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(target.Text) ? target.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private static void RequireFile(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(name + "不存在。", path);
    }

    private static void RequireDirectory(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException(name + "不存在：" + path);
    }

    private static decimal ClampDecimal(double value, decimal minimum, decimal maximum) =>
        Math.Clamp((decimal)value, minimum, maximum);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _operationCancellation?.Cancel();
        try
        {
            if (_manualWallpaperSnapshot is not null) _display.RestoreSnapshot(_manualWallpaperSnapshot);
        }
        catch { }
        previewPictureBox.Image?.Dispose();
        _camera.Dispose();
        base.OnFormClosed(e);
    }
}
