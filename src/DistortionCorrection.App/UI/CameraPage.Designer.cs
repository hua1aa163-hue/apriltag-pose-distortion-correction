#nullable enable
namespace DistortionCorrection.UI;

partial class CameraPage
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel layoutPanel = null!;
    private Label deviceLabel = null!;
    private ComboBox cameraListComboBox = null!;
    private Button refreshButton = null!;
    private CheckBox setExposureCheckBox = null!;
    private NumericUpDown exposureNumeric = null!;
    private CheckBox setGainCheckBox = null!;
    private NumericUpDown gainNumeric = null!;
    private GroupBox lensCorrectionGroupBox = null!;
    private TableLayoutPanel lensLayoutPanel = null!;
    private CheckBox lensCorrectionEnabledCheckBox = null!;
    private Label lensCalibrationPathLabel = null!;
    private TextBox lensCalibrationPathTextBox = null!;
    private Button browseLensCalibrationButton = null!;
    private Label chessboardFolderLabel = null!;
    private TextBox chessboardFolderTextBox = null!;
    private Button browseChessboardFolderButton = null!;
    private FlowLayoutPanel lensParameterPanel = null!;
    private Label chessboardColumnsLabel = null!;
    private NumericUpDown chessboardColumnsNumeric = null!;
    private Label chessboardRowsLabel = null!;
    private NumericUpDown chessboardRowsNumeric = null!;
    private Label chessboardSquareSizeLabel = null!;
    private NumericUpDown chessboardSquareSizeNumeric = null!;
    private Label lensAlphaLabel = null!;
    private NumericUpDown lensAlphaNumeric = null!;
    private Button calibrateLensButton = null!;
    private Label lensStatusLabel = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button connectButton = null!;
    private Button captureButton = null!;
    private Label helpLabel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layoutPanel = new TableLayoutPanel();
        deviceLabel = new Label();
        cameraListComboBox = new ComboBox();
        refreshButton = new Button();
        setExposureCheckBox = new CheckBox();
        exposureNumeric = new NumericUpDown();
        setGainCheckBox = new CheckBox();
        gainNumeric = new NumericUpDown();
        lensCorrectionGroupBox = new GroupBox();
        lensLayoutPanel = new TableLayoutPanel();
        lensCorrectionEnabledCheckBox = new CheckBox();
        lensCalibrationPathLabel = new Label();
        lensCalibrationPathTextBox = new TextBox();
        browseLensCalibrationButton = new Button();
        chessboardFolderLabel = new Label();
        chessboardFolderTextBox = new TextBox();
        browseChessboardFolderButton = new Button();
        lensParameterPanel = new FlowLayoutPanel();
        chessboardColumnsLabel = new Label();
        chessboardColumnsNumeric = new NumericUpDown();
        chessboardRowsLabel = new Label();
        chessboardRowsNumeric = new NumericUpDown();
        chessboardSquareSizeLabel = new Label();
        chessboardSquareSizeNumeric = new NumericUpDown();
        lensAlphaLabel = new Label();
        lensAlphaNumeric = new NumericUpDown();
        calibrateLensButton = new Button();
        lensStatusLabel = new Label();
        buttonPanel = new FlowLayoutPanel();
        connectButton = new Button();
        captureButton = new Button();
        helpLabel = new Label();
        layoutPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)exposureNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gainNumeric).BeginInit();
        lensCorrectionGroupBox.SuspendLayout();
        lensLayoutPanel.SuspendLayout();
        lensParameterPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)chessboardColumnsNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)chessboardRowsNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)chessboardSquareSizeNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)lensAlphaNumeric).BeginInit();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        layoutPanel.AutoScroll = true;
        layoutPanel.ColumnCount = 3;
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.Controls.Add(deviceLabel, 0, 0);
        layoutPanel.Controls.Add(cameraListComboBox, 1, 0);
        layoutPanel.Controls.Add(refreshButton, 2, 0);
        layoutPanel.Controls.Add(setExposureCheckBox, 0, 1);
        layoutPanel.Controls.Add(exposureNumeric, 1, 1);
        layoutPanel.Controls.Add(setGainCheckBox, 0, 2);
        layoutPanel.Controls.Add(gainNumeric, 1, 2);
        layoutPanel.Controls.Add(lensCorrectionGroupBox, 0, 3);
        layoutPanel.Controls.Add(buttonPanel, 0, 4);
        layoutPanel.Controls.Add(helpLabel, 0, 5);
        layoutPanel.Dock = DockStyle.Fill;
        layoutPanel.Padding = new Padding(18);
        layoutPanel.RowCount = 6;
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        deviceLabel.Anchor = AnchorStyles.Left;
        deviceLabel.AutoSize = true;
        deviceLabel.Text = "海康 GigE 设备：";
        deviceLabel.Margin = new Padding(3, 10, 12, 10);

        cameraListComboBox.Dock = DockStyle.Fill;
        cameraListComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        cameraListComboBox.Margin = new Padding(3, 7, 8, 7);

        refreshButton.AutoSize = true;
        refreshButton.Text = "刷新设备";
        refreshButton.Margin = new Padding(3, 5, 3, 5);

        setExposureCheckBox.Anchor = AnchorStyles.Left;
        setExposureCheckBox.AutoSize = true;
        setExposureCheckBox.Text = "固定曝光 (μs)";
        setExposureCheckBox.Margin = new Padding(3, 10, 12, 10);

        exposureNumeric.Anchor = AnchorStyles.Left;
        exposureNumeric.Maximum = 10000000;
        exposureNumeric.Minimum = 10;
        exposureNumeric.Value = 10000;
        exposureNumeric.Width = 150;

        setGainCheckBox.Anchor = AnchorStyles.Left;
        setGainCheckBox.AutoSize = true;
        setGainCheckBox.Text = "固定增益";
        setGainCheckBox.Margin = new Padding(3, 10, 12, 10);

        gainNumeric.Anchor = AnchorStyles.Left;
        gainNumeric.DecimalPlaces = 1;
        gainNumeric.Increment = 0.5M;
        gainNumeric.Maximum = 48;
        gainNumeric.Width = 150;

        layoutPanel.SetColumnSpan(lensCorrectionGroupBox, 3);
        lensCorrectionGroupBox.AutoSize = true;
        lensCorrectionGroupBox.Controls.Add(lensLayoutPanel);
        lensCorrectionGroupBox.Dock = DockStyle.Top;
        lensCorrectionGroupBox.Margin = new Padding(0, 12, 0, 0);
        lensCorrectionGroupBox.Padding = new Padding(12, 8, 12, 12);
        lensCorrectionGroupBox.Text = "OpenCV 镜头畸变矫正";

        lensLayoutPanel.AutoSize = true;
        lensLayoutPanel.ColumnCount = 3;
        lensLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lensLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        lensLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lensLayoutPanel.Controls.Add(lensCorrectionEnabledCheckBox, 0, 0);
        lensLayoutPanel.Controls.Add(lensCalibrationPathLabel, 0, 1);
        lensLayoutPanel.Controls.Add(lensCalibrationPathTextBox, 1, 1);
        lensLayoutPanel.Controls.Add(browseLensCalibrationButton, 2, 1);
        lensLayoutPanel.Controls.Add(chessboardFolderLabel, 0, 2);
        lensLayoutPanel.Controls.Add(chessboardFolderTextBox, 1, 2);
        lensLayoutPanel.Controls.Add(browseChessboardFolderButton, 2, 2);
        lensLayoutPanel.Controls.Add(lensParameterPanel, 0, 3);
        lensLayoutPanel.Controls.Add(calibrateLensButton, 0, 4);
        lensLayoutPanel.Controls.Add(lensStatusLabel, 1, 4);
        lensLayoutPanel.Dock = DockStyle.Fill;
        lensLayoutPanel.RowCount = 5;
        lensLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lensLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lensLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lensLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lensLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lensLayoutPanel.SetColumnSpan(lensCorrectionEnabledCheckBox, 3);
        lensCorrectionEnabledCheckBox.AutoSize = true;
        lensCorrectionEnabledCheckBox.Margin = new Padding(3, 7, 3, 9);
        lensCorrectionEnabledCheckBox.Text = "启用镜头矫正（所有相机取帧先执行 OpenCV Remap）";

        lensCalibrationPathLabel.Anchor = AnchorStyles.Left;
        lensCalibrationPathLabel.AutoSize = true;
        lensCalibrationPathLabel.Margin = new Padding(3, 9, 12, 9);
        lensCalibrationPathLabel.Text = "标定 JSON：";

        lensCalibrationPathTextBox.Dock = DockStyle.Fill;
        lensCalibrationPathTextBox.Margin = new Padding(3, 5, 8, 5);

        browseLensCalibrationButton.AutoSize = true;
        browseLensCalibrationButton.Margin = new Padding(3, 3, 3, 5);
        browseLensCalibrationButton.Text = "浏览";

        chessboardFolderLabel.Anchor = AnchorStyles.Left;
        chessboardFolderLabel.AutoSize = true;
        chessboardFolderLabel.Margin = new Padding(3, 9, 12, 9);
        chessboardFolderLabel.Text = "棋盘格图片：";

        chessboardFolderTextBox.Dock = DockStyle.Fill;
        chessboardFolderTextBox.Margin = new Padding(3, 5, 8, 5);

        browseChessboardFolderButton.AutoSize = true;
        browseChessboardFolderButton.Margin = new Padding(3, 3, 3, 5);
        browseChessboardFolderButton.Text = "浏览文件夹";

        lensLayoutPanel.SetColumnSpan(lensParameterPanel, 3);
        lensParameterPanel.AutoSize = true;
        lensParameterPanel.Controls.Add(chessboardColumnsLabel);
        lensParameterPanel.Controls.Add(chessboardColumnsNumeric);
        lensParameterPanel.Controls.Add(chessboardRowsLabel);
        lensParameterPanel.Controls.Add(chessboardRowsNumeric);
        lensParameterPanel.Controls.Add(chessboardSquareSizeLabel);
        lensParameterPanel.Controls.Add(chessboardSquareSizeNumeric);
        lensParameterPanel.Controls.Add(lensAlphaLabel);
        lensParameterPanel.Controls.Add(lensAlphaNumeric);
        lensParameterPanel.Dock = DockStyle.Fill;
        lensParameterPanel.Margin = new Padding(0, 5, 0, 5);
        lensParameterPanel.WrapContents = true;

        chessboardColumnsLabel.Anchor = AnchorStyles.Left;
        chessboardColumnsLabel.AutoSize = true;
        chessboardColumnsLabel.Margin = new Padding(3, 8, 5, 3);
        chessboardColumnsLabel.Text = "内角点列";

        chessboardColumnsNumeric.Maximum = 30;
        chessboardColumnsNumeric.Minimum = 3;
        chessboardColumnsNumeric.Value = 9;
        chessboardColumnsNumeric.Width = 58;

        chessboardRowsLabel.Anchor = AnchorStyles.Left;
        chessboardRowsLabel.AutoSize = true;
        chessboardRowsLabel.Margin = new Padding(16, 8, 5, 3);
        chessboardRowsLabel.Text = "行";

        chessboardRowsNumeric.Maximum = 30;
        chessboardRowsNumeric.Minimum = 3;
        chessboardRowsNumeric.Value = 6;
        chessboardRowsNumeric.Width = 58;

        chessboardSquareSizeLabel.Anchor = AnchorStyles.Left;
        chessboardSquareSizeLabel.AutoSize = true;
        chessboardSquareSizeLabel.Margin = new Padding(16, 8, 5, 3);
        chessboardSquareSizeLabel.Text = "方格边长";

        chessboardSquareSizeNumeric.DecimalPlaces = 2;
        chessboardSquareSizeNumeric.Maximum = 1000;
        chessboardSquareSizeNumeric.Minimum = 0.01M;
        chessboardSquareSizeNumeric.Value = 25;
        chessboardSquareSizeNumeric.Width = 78;

        lensAlphaLabel.Anchor = AnchorStyles.Left;
        lensAlphaLabel.AutoSize = true;
        lensAlphaLabel.Margin = new Padding(16, 8, 5, 3);
        lensAlphaLabel.Text = "Alpha";

        lensAlphaNumeric.DecimalPlaces = 2;
        lensAlphaNumeric.Increment = 0.05M;
        lensAlphaNumeric.Maximum = 1;
        lensAlphaNumeric.Width = 68;

        calibrateLensButton.AutoSize = true;
        calibrateLensButton.Margin = new Padding(3, 7, 12, 3);
        calibrateLensButton.Text = "棋盘格标定并保存";

        lensStatusLabel.Anchor = AnchorStyles.Left;
        lensStatusLabel.AutoSize = true;
        lensLayoutPanel.SetColumnSpan(lensStatusLabel, 2);
        lensStatusLabel.ForeColor = Color.DimGray;
        lensStatusLabel.Margin = new Padding(3, 10, 3, 3);
        lensStatusLabel.Text = "未启用。建议准备 12–20 张不同角度、不同位置的棋盘格照片。";

        layoutPanel.SetColumnSpan(buttonPanel, 3);
        buttonPanel.AutoSize = true;
        buttonPanel.Controls.Add(connectButton);
        buttonPanel.Controls.Add(captureButton);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.Margin = new Padding(0, 12, 0, 8);

        connectButton.AutoSize = true;
        connectButton.Text = "连接相机";
        connectButton.Margin = new Padding(3, 3, 10, 3);

        captureButton.AutoSize = true;
        captureButton.Text = "抓取并保存单帧";

        layoutPanel.SetColumnSpan(helpLabel, 3);
        helpLabel.AutoSize = true;
        helpLabel.Dock = DockStyle.Top;
        helpLabel.ForeColor = Color.DimGray;
        helpLabel.Padding = new Padding(0, 8, 0, 0);
        helpLabel.Text = "建议标定时固定曝光与增益，避免 Gray Code 各帧亮度漂移。镜头 Alpha=0 尽量无黑边；Alpha=1 保留全部视场，边缘可能填黑。";

        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layoutPanel);
        Name = "CameraPage";
        Size = new Size(780, 620);
        layoutPanel.ResumeLayout(false);
        layoutPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)exposureNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)gainNumeric).EndInit();
        lensCorrectionGroupBox.ResumeLayout(false);
        lensCorrectionGroupBox.PerformLayout();
        lensLayoutPanel.ResumeLayout(false);
        lensLayoutPanel.PerformLayout();
        lensParameterPanel.ResumeLayout(false);
        lensParameterPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)chessboardColumnsNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)chessboardRowsNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)chessboardSquareSizeNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)lensAlphaNumeric).EndInit();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        ResumeLayout(false);
    }
}
