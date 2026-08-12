namespace DistortionCorrection.UI;

public partial class CalibrationPage : UserControl
{
    public CalibrationPage() => InitializeComponent();

    public TextBox RunRoot => runRootTextBox;
    public NumericUpDown GridColumns => gridColumnsNumeric;
    public NumericUpDown GridRows => gridRowsNumeric;
    public NumericUpDown TargetWidth => targetWidthNumeric;
    public NumericUpDown TargetHeight => targetHeightNumeric;
    public NumericUpDown DotDiameter => dotDiameterNumeric;
    public Label Summary => summaryLabel;
    public Button BrowseButton => browseButton;
    public Button SaveParametersButton => saveParametersButton;
    public Button FullCalibrationButton => fullCalibrationButton;
    public Button ResumeCalibrationButton => resumeCalibrationButton;
    public IEnumerable<Button> ActionButtons =>
        [browseButton, saveParametersButton, fullCalibrationButton, resumeCalibrationButton];
}
