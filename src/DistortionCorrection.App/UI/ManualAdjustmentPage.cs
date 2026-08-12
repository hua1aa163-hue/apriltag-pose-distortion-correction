namespace DistortionCorrection.UI;

public partial class ManualAdjustmentPage : UserControl
{
    public ManualAdjustmentPage() => InitializeComponent();

    public TextBox BaseCalibrationPath => baseCalibrationTextBox;
    public TextBox OutputCalibrationPath => outputCalibrationTextBox;
    public NumericUpDown Rotation => rotationNumeric;
    public NumericUpDown HorizontalPerspective => horizontalPerspectiveNumeric;
    public NumericUpDown VerticalPerspective => verticalPerspectiveNumeric;
    public NumericUpDown HorizontalScale => horizontalScaleNumeric;
    public NumericUpDown VerticalScale => verticalScaleNumeric;
    public NumericUpDown OffsetX => offsetXNumeric;
    public NumericUpDown OffsetY => offsetYNumeric;
    public Label Status => statusLabel;
    public Button BrowseBaseButton => browseBaseButton;
    public Button BrowseOutputButton => browseOutputButton;
    public Button ResetButton => resetButton;
    public Button PreviewButton => previewButton;
    public Button ConfirmButton => confirmButton;
    public Button RestoreDesktopButton => restoreDesktopButton;
    public IEnumerable<Button> ActionButtons =>
        [browseBaseButton, browseOutputButton, resetButton, previewButton, confirmButton, restoreDesktopButton];
}
