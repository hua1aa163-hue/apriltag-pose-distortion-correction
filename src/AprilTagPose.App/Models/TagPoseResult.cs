using OpenCvSharp;

namespace AprilTagPose.Models;

public sealed class TagPoseResult
{
    public int Id { get; init; }
    public Point2f[] Corners { get; init; } = [];
    public Point2f Center { get; init; }
    public double[] RotationVector { get; init; } = [];
    public double[] TranslationMillimeters { get; init; } = [];
    public double[,] RotationMatrix { get; init; } = new double[3, 3];
    public EulerAngles EulerDegrees { get; init; } = new();
    public QuaternionRotation Quaternion { get; init; } = new();
    public double DistanceMillimeters { get; init; }
    public double ReprojectionErrorPixels { get; init; }
    public double PerimeterPixels { get; init; }
    public double AreaPixels { get; init; }
    public bool PoseUsesApproximateIntrinsics { get; init; }
}

public sealed class EulerAngles
{
    public double Roll { get; init; }
    public double Pitch { get; init; }
    public double Yaw { get; init; }
}

public sealed class QuaternionRotation
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double W { get; init; } = 1d;
}

public sealed class DetectionFrameResult : IDisposable
{
    public required Mat AnnotatedImage { get; init; }
    public MarkerDictionaryKind Dictionary { get; init; } = MarkerDictionaryKind.ArUco4X4_250;
    public IReadOnlyList<TagPoseResult> Tags { get; init; } = [];
    public required CameraCalibration CalibrationUsed { get; init; }
    public bool UsesApproximateIntrinsics { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public int RejectedCandidateCount { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public void Dispose() => AnnotatedImage.Dispose();
}
