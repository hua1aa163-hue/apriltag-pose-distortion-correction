namespace DistortionCorrection.UI;

public partial class BatchWarpPage : UserControl
{
    public BatchWarpPage()
    {
        InitializeComponent();
        placementComboBox.Items.AddRange(["自动位置", "固定位置", "自动 + 固定两种"]);
        placementComboBox.SelectedIndex = 2;
    }

    public TextBox CalibrationPath => calibrationPathTextBox;
    public TextBox InputFolder => inputFolderTextBox;
    public TextBox OutputFolder => outputFolderTextBox;
    public ComboBox Placement => placementComboBox;
    public CheckBox Recursive => recursiveCheckBox;
    public NumericUpDown FixedX => fixedXNumeric;
    public NumericUpDown FixedY => fixedYNumeric;
    public ProgressBar Progress => progressBar;
    public Label Status => statusLabel;
    public Button BrowseCalibrationButton => browseCalibrationButton;
    public Button BrowseInputButton => browseInputButton;
    public Button BrowseOutputButton => browseOutputButton;
    public Button StartButton => startButton;
    public NumericUpDown CenteredInputWidth => centeredInputWidthNumeric;
    public NumericUpDown CenteredInputHeight => centeredInputHeightNumeric;
    public NumericUpDown CenteredOutputWidth => centeredOutputWidthNumeric;
    public NumericUpDown CenteredOutputHeight => centeredOutputHeightNumeric;
    public Button CenteredContentStartButton => centeredContentStartButton;
    public IEnumerable<Button> ActionButtons =>
        [browseCalibrationButton, browseInputButton, browseOutputButton, startButton, centeredContentStartButton];
}
