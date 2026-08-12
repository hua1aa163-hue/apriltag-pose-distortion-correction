using DistortionCorrection.Calibration;
using DistortionCorrection.Models;
using System.Drawing.Imaging;

namespace DistortionCorrection.Imaging;

[Flags]
public enum BatchPlacementModes
{
    Auto = 1,
    Fixed = 2,
    Both = Auto | Fixed
}

public sealed record BatchWarpProgress(int Current, int Total, string SourcePath, string Message);

public sealed record BatchWarpFailure(string SourcePath, string Error);

public sealed record CenteredContentWarpOptions(
    int InputContentWidth,
    int InputContentHeight,
    int OutputContentWidth,
    int OutputContentHeight)
{
    public void Validate(int canvasWidth, int canvasHeight)
    {
        if (InputContentWidth < 32 || InputContentWidth > canvasWidth ||
            InputContentHeight < 32 || InputContentHeight > canvasHeight)
        {
            throw new InvalidDataException(
                $"输入居中矩形必须在 32×32 到 {canvasWidth}×{canvasHeight} 之间。");
        }
        if (OutputContentWidth < 32 || OutputContentWidth > canvasWidth ||
            OutputContentHeight < 32 || OutputContentHeight > canvasHeight)
        {
            throw new InvalidDataException(
                $"输出目标外接尺寸必须在 32×32 到 {canvasWidth}×{canvasHeight} 之间。");
        }
    }
}

public sealed record BatchWarpResult(
    int DiscoveredFiles,
    int SuccessfulFiles,
    int OutputFiles,
    IReadOnlyList<BatchWarpFailure> Failures,
    string? LastOutputPath);

/// <summary>把一个文件夹中的常见图片批量预扭曲为 1920×1080 PNG 黑底画布。</summary>
public static class BatchWarpService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
    };

    public static BatchWarpResult WarpFolder(
        string inputDirectory,
        string outputDirectory,
        MeshCalibration calibration,
        BatchPlacementModes modes,
        Point fixedTopLeft,
        bool recursive,
        IProgress<BatchWarpProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => WarpFolderCore(inputDirectory, outputDirectory, calibration, modes, fixedTopLeft,
            recursive, centeredOptions: null, progress, cancellationToken);

    /// <summary>
    /// 从每张 1920×1080 输入图中提取居中矩形，使其完整占用标定输入域，
    /// 再按指定目标外接宽高输出到 1920×1080 黑底画布。
    /// </summary>
    public static BatchWarpResult WarpCenteredContentFolder(
        string inputDirectory,
        string outputDirectory,
        MeshCalibration calibration,
        BatchPlacementModes modes,
        Point fixedTopLeft,
        bool recursive,
        CenteredContentWarpOptions options,
        IProgress<BatchWarpProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => WarpFolderCore(inputDirectory, outputDirectory, calibration, modes, fixedTopLeft,
            recursive, options, progress, cancellationToken);

    private static BatchWarpResult WarpFolderCore(
        string inputDirectory,
        string outputDirectory,
        MeshCalibration calibration,
        BatchPlacementModes modes,
        Point fixedTopLeft,
        bool recursive,
        CenteredContentWarpOptions? centeredOptions,
        IProgress<BatchWarpProgress>? progress,
        CancellationToken cancellationToken)
    {
        calibration.Validate();
        centeredOptions?.Validate(calibration.CanvasWidth, calibration.CanvasHeight);
        if (modes == 0 || (modes & ~BatchPlacementModes.Both) != 0)
            throw new ArgumentOutOfRangeException(nameof(modes));

        MeshCalibration renderCalibration = centeredOptions is null ||
                                            (centeredOptions.OutputContentWidth == calibration.ExpectedInverseMappedWidth &&
                                             centeredOptions.OutputContentHeight == calibration.ExpectedInverseMappedHeight)
            ? calibration
            : ManualCalibrationAdjustment.ResizeBounds(
                calibration,
                centeredOptions.OutputContentWidth,
                centeredOptions.OutputContentHeight).Calibration;
        string automaticDirectory = centeredOptions is null ? "自动位置" : "居中矩形-自动位置";
        string fixedDirectory = centeredOptions is null ? "固定位置" : "居中矩形-固定位置";

        string inputRoot = Path.GetFullPath(inputDirectory);
        string outputRoot = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(inputRoot)) throw new DirectoryNotFoundException($"输入文件夹不存在：{inputRoot}");
        if (PathsEqual(inputRoot, outputRoot))
            throw new InvalidOperationException("输出文件夹不能与输入文件夹相同。");

        Directory.CreateDirectory(outputRoot);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string outputPrefix = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;

        // 先拍下文件列表，且排除位于输入目录内部的输出树，避免一边生成一边重复处理自身输出。
        string[] files = Directory.EnumerateFiles(inputRoot, "*", searchOption)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFullPath)
            .Where(path => !path.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var failures = new List<BatchWarpFailure>();
        int successful = 0;
        int outputs = 0;
        string? lastOutput = null;
        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = files[index];
            progress?.Report(new BatchWarpProgress(index, files.Length, sourcePath, "正在读取"));
            try
            {
                string relative = Path.GetRelativePath(inputRoot, sourcePath);
                string? relativeDirectory = Path.GetDirectoryName(relative);
                string outputName = Path.GetFileNameWithoutExtension(sourcePath) + ".png";
                using var input = new Bitmap(sourcePath);
                using Bitmap? centeredInput = PrepareCenteredInput(input, calibration, centeredOptions);
                Image warpInput = centeredInput ?? input;

                if (modes.HasFlag(BatchPlacementModes.Auto))
                {
                    string path = BuildOutputPath(outputRoot, automaticDirectory, relativeDirectory, outputName);
                    using Bitmap warped = MeshWarp.WarpToRequiredCanvas(
                        warpInput, renderCalibration, MeshWarp.PlacementMode.Auto);
                    warped.Save(path, ImageFormat.Png);
                    lastOutput = path;
                    outputs++;
                }

                if (modes.HasFlag(BatchPlacementModes.Fixed))
                {
                    string path = BuildOutputPath(outputRoot, fixedDirectory, relativeDirectory, outputName);
                    using Bitmap warped = MeshWarp.WarpToRequiredCanvas(
                        warpInput, renderCalibration, MeshWarp.PlacementMode.Fixed, fixedTopLeft);
                    warped.Save(path, ImageFormat.Png);
                    lastOutput = path;
                    outputs++;
                }

                successful++;
                progress?.Report(new BatchWarpProgress(index + 1, files.Length, sourcePath, "完成"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new BatchWarpFailure(sourcePath, exception.Message));
                progress?.Report(new BatchWarpProgress(index + 1, files.Length, sourcePath, "失败：" + exception.Message));
            }
        }

        return new BatchWarpResult(files.Length, successful, outputs, failures, lastOutput);
    }

    private static Bitmap? PrepareCenteredInput(
        Bitmap input,
        MeshCalibration calibration,
        CenteredContentWarpOptions? options)
    {
        if (options is null) return null;
        if (input.Width != calibration.CanvasWidth || input.Height != calibration.CanvasHeight)
        {
            throw new InvalidDataException(
                $"居中矩形模式要求输入图片为 {calibration.CanvasWidth}×{calibration.CanvasHeight}；" +
                $"当前为 {input.Width}×{input.Height}。");
        }
        return BitmapTools.ExtractCenteredAndStretch(
            input,
            options.InputContentWidth,
            options.InputContentHeight,
            calibration.SourceContentWidth,
            calibration.SourceContentHeight);
    }

    private static string BuildOutputPath(
        string outputRoot,
        string modeDirectory,
        string? relativeDirectory,
        string fileName)
    {
        string directory = string.IsNullOrEmpty(relativeDirectory)
            ? Path.Combine(outputRoot, modeDirectory)
            : Path.Combine(outputRoot, modeDirectory, relativeDirectory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
