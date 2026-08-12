namespace DistortionCorrection.UI;

public partial class CameraPage : UserControl
{
    public CameraPage() => InitializeComponent();

    public ComboBox CameraList => cameraListComboBox;
    public CheckBox SetExposure => setExposureCheckBox;
    public NumericUpDown Exposure => exposureNumeric;
    public CheckBox SetGain => setGainCheckBox;
    public NumericUpDown Gain => gainNumeric;
    public CheckBox LensCorrectionEnabled => lensCorrectionEnabledCheckBox;
    public TextBox LensCalibrationPath => lensCalibrationPathTextBox;
    public Button BrowseLensCalibrationButton => browseLensCalibrationButton;
    public TextBox ChessboardFolder => chessboardFolderTextBox;
    public Button BrowseChessboardFolderButton => browseChessboardFolderButton;
    public NumericUpDown ChessboardColumns => chessboardColumnsNumeric;
    public NumericUpDown ChessboardRows => chessboardRowsNumeric;
    public NumericUpDown ChessboardSquareSize => chessboardSquareSizeNumeric;
    public NumericUpDown LensAlpha => lensAlphaNumeric;
    public Button CalibrateLensButton => calibrateLensButton;
    public Label LensStatus => lensStatusLabel;
    public Button RefreshButton => refreshButton;
    public Button ConnectButton => connectButton;
    public Button CaptureButton => captureButton;
    public IEnumerable<Button> ActionButtons =>
        [refreshButton, browseLensCalibrationButton, browseChessboardFolderButton,
            calibrateLensButton, connectButton, captureButton];
}
