using DistortionCorrection.Models;
using System.Text.Json;

namespace DistortionCorrection.Calibration;

public static class CalibrationStorage
{
    public static void SaveMesh(MeshCalibration calibration, string path)
    {
        calibration.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(calibration, JsonOptions()));
    }

    public static MeshCalibration LoadMesh(string path)
    {
        var calibration = JsonSerializer.Deserialize<MeshCalibration>(File.ReadAllText(path), JsonOptions())
            ?? throw new InvalidDataException("标定 JSON 内容为空。");
        calibration.Validate();
        return calibration;
    }

    public static void SaveDenseMap(DenseProjectorMap map, string path)
    {
        if (map.CameraAtProjectorCell.Length != map.CellColumns * map.CellRows ||
            map.SamplesPerCell.Length != map.CameraAtProjectorCell.Length)
        {
            throw new InvalidDataException("稠密映射尺寸不一致。");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(DenseProjectorMap.FileMagic);
        writer.Write(1);
        writer.Write(map.CanvasWidth);
        writer.Write(map.CanvasHeight);
        writer.Write(map.CellSize);
        writer.Write(map.CellColumns);
        writer.Write(map.CellRows);
        writer.Write(map.CameraWidth);
        writer.Write(map.CameraHeight);
        writer.Write(map.CameraAtProjectorCell.Length);
        for (int i = 0; i < map.CameraAtProjectorCell.Length; i++)
        {
            writer.Write(map.CameraAtProjectorCell[i].X);
            writer.Write(map.CameraAtProjectorCell[i].Y);
            writer.Write(map.SamplesPerCell[i]);
        }
    }

    public static DenseProjectorMap LoadDenseMap(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != DenseProjectorMap.FileMagic) throw new InvalidDataException("不是有效的稠密映射文件。");
        int version = reader.ReadInt32();
        if (version != 1) throw new InvalidDataException($"不支持的稠密映射版本：{version}");
        int canvasWidth = reader.ReadInt32();
        int canvasHeight = reader.ReadInt32();
        int cellSize = reader.ReadInt32();
        int columns = reader.ReadInt32();
        int rows = reader.ReadInt32();
        int cameraWidth = reader.ReadInt32();
        int cameraHeight = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (count != checked(columns * rows) || count < 1 || count > 10_000_000) throw new InvalidDataException("稠密映射单元数无效。");
        var points = new PointF[count];
        var samples = new int[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new PointF(reader.ReadSingle(), reader.ReadSingle());
            samples[i] = reader.ReadInt32();
        }
        return new DenseProjectorMap
        {
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            CellSize = cellSize,
            CellColumns = columns,
            CellRows = rows,
            CameraWidth = cameraWidth,
            CameraHeight = cameraHeight,
            CameraAtProjectorCell = points,
            SamplesPerCell = samples
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
