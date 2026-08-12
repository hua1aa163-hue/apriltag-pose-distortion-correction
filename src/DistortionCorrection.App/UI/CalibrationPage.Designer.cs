#nullable enable
namespace DistortionCorrection.UI;

partial class CalibrationPage
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel layoutPanel = null!;
    private Label runRootLabel = null!;
    private TextBox runRootTextBox = null!;
    private Button browseButton = null!;
    private Label gridLabel = null!;
    private FlowLayoutPanel gridPanel = null!;
    private NumericUpDown gridColumnsNumeric = null!;
    private Label gridTimesLabel = null!;
    private NumericUpDown gridRowsNumeric = null!;
    private Label targetLabel = null!;
    private FlowLayoutPanel targetPanel = null!;
    private NumericUpDown targetWidthNumeric = null!;
    private Label targetTimesLabel = null!;
    private NumericUpDown targetHeightNumeric = null!;
    private Label dotLabel = null!;
    private NumericUpDown dotDiameterNumeric = null!;
    private Label summaryLabel = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button saveParametersButton = null!;
    private Button fullCalibrationButton = null!;
    private Button resumeCalibrationButton = null!;
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
        runRootLabel = new Label();
        runRootTextBox = new TextBox();
        browseButton = new Button();
        gridLabel = new Label();
        gridPanel = new FlowLayoutPanel();
        gridColumnsNumeric = new NumericUpDown();
        gridTimesLabel = new Label();
        gridRowsNumeric = new NumericUpDown();
        targetLabel = new Label();
        targetPanel = new FlowLayoutPanel();
        targetWidthNumeric = new NumericUpDown();
        targetTimesLabel = new Label();
        targetHeightNumeric = new NumericUpDown();
        dotLabel = new Label();
        dotDiameterNumeric = new NumericUpDown();
        summaryLabel = new Label();
        buttonPanel = new FlowLayoutPanel();
        saveParametersButton = new Button();
        fullCalibrationButton = new Button();
        resumeCalibrationButton = new Button();
        helpLabel = new Label();
        layoutPanel.SuspendLayout();
        gridPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridColumnsNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridRowsNumeric).BeginInit();
        targetPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)targetWidthNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)targetHeightNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)dotDiameterNumeric).BeginInit();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        layoutPanel.AutoScroll = true;
        layoutPanel.ColumnCount = 3;
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.Controls.Add(runRootLabel, 0, 0);
        layoutPanel.Controls.Add(runRootTextBox, 1, 0);
        layoutPanel.Controls.Add(browseButton, 2, 0);
        layoutPanel.Controls.Add(gridLabel, 0, 1);
        layoutPanel.Controls.Add(gridPanel, 1, 1);
        layoutPanel.Controls.Add(targetLabel, 0, 2);
        layoutPanel.Controls.Add(targetPanel, 1, 2);
        layoutPanel.Controls.Add(dotLabel, 0, 3);
        layoutPanel.Controls.Add(dotDiameterNumeric, 1, 3);
        layoutPanel.Controls.Add(summaryLabel, 0, 4);
        layoutPanel.Controls.Add(buttonPanel, 0, 5);
        layoutPanel.Controls.Add(helpLabel, 0, 6);
        layoutPanel.Dock = DockStyle.Fill;
        layoutPanel.Padding = new Padding(18);
        layoutPanel.RowCount = 7;
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        runRootLabel.Anchor = AnchorStyles.Left;
        runRootLabel.AutoSize = true;
        runRootLabel.Text = "运行根目录：";
        runRootLabel.Margin = new Padding(3, 10, 12, 10);

        runRootTextBox.Dock = DockStyle.Fill;
        runRootTextBox.Margin = new Padding(3, 7, 8, 7);

        browseButton.AutoSize = true;
        browseButton.Text = "选择…";
        browseButton.Margin = new Padding(3, 5, 3, 5);

        gridLabel.Anchor = AnchorStyles.Left;
        gridLabel.AutoSize = true;
        gridLabel.Text = "点阵（列 × 行）：";
        gridLabel.Margin = new Padding(3, 10, 12, 10);

        gridPanel.AutoSize = true;
        gridPanel.Controls.Add(gridColumnsNumeric);
        gridPanel.Controls.Add(gridTimesLabel);
        gridPanel.Controls.Add(gridRowsNumeric);
        gridPanel.Dock = DockStyle.Fill;
        gridPanel.WrapContents = false;

        gridColumnsNumeric.Maximum = 100;
        gridColumnsNumeric.Minimum = 3;
        gridColumnsNumeric.Value = 27;
        gridColumnsNumeric.Width = 100;
        gridTimesLabel.Anchor = AnchorStyles.Left;
        gridTimesLabel.AutoSize = true;
        gridTimesLabel.Text = "×";
        gridTimesLabel.Margin = new Padding(8, 7, 8, 3);
        gridRowsNumeric.Maximum = 100;
        gridRowsNumeric.Minimum = 3;
        gridRowsNumeric.Value = 7;
        gridRowsNumeric.Width = 100;

        targetLabel.Anchor = AnchorStyles.Left;
        targetLabel.AutoSize = true;
        targetLabel.Text = "目标外接（宽 × 高）：";
        targetLabel.Margin = new Padding(3, 10, 12, 10);

        targetPanel.AutoSize = true;
        targetPanel.Controls.Add(targetWidthNumeric);
        targetPanel.Controls.Add(targetTimesLabel);
        targetPanel.Controls.Add(targetHeightNumeric);
        targetPanel.Dock = DockStyle.Fill;
        targetPanel.WrapContents = false;

        targetWidthNumeric.Maximum = 1920;
        targetWidthNumeric.Minimum = 32;
        targetWidthNumeric.Value = 1777;
        targetWidthNumeric.Width = 100;
        targetTimesLabel.Anchor = AnchorStyles.Left;
        targetTimesLabel.AutoSize = true;
        targetTimesLabel.Text = "×";
        targetTimesLabel.Margin = new Padding(8, 7, 8, 3);
        targetHeightNumeric.Maximum = 1080;
        targetHeightNumeric.Minimum = 32;
        targetHeightNumeric.Value = 687;
        targetHeightNumeric.Width = 100;

        dotLabel.Anchor = AnchorStyles.Left;
        dotLabel.AutoSize = true;
        dotLabel.Text = "点直径（像素）：";
        dotLabel.Margin = new Padding(3, 10, 12, 10);

        dotDiameterNumeric.Anchor = AnchorStyles.Left;
        dotDiameterNumeric.Maximum = 199;
        dotDiameterNumeric.Minimum = 3;
        dotDiameterNumeric.Value = 13;
        dotDiameterNumeric.Width = 100;

        layoutPanel.SetColumnSpan(summaryLabel, 3);
        summaryLabel.AutoSize = true;
        summaryLabel.Dock = DockStyle.Top;
        summaryLabel.ForeColor = Color.DarkGreen;
        summaryLabel.Margin = new Padding(3, 12, 3, 6);
        summaryLabel.Text = "点阵与目标尺寸可填写；最终文件始终为 1920×1080 黑底。";

        layoutPanel.SetColumnSpan(buttonPanel, 3);
        buttonPanel.AutoSize = true;
        buttonPanel.Controls.Add(saveParametersButton);
        buttonPanel.Controls.Add(fullCalibrationButton);
        buttonPanel.Controls.Add(resumeCalibrationButton);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.Margin = new Padding(0, 8, 0, 8);

        saveParametersButton.AutoSize = true;
        saveParametersButton.Text = "保存参数";
        saveParametersButton.Margin = new Padding(3, 3, 10, 3);
        fullCalibrationButton.AutoSize = true;
        fullCalibrationButton.Text = "完整 Gray Code + 点阵回环";
        fullCalibrationButton.Margin = new Padding(3, 3, 10, 3);
        resumeCalibrationButton.AutoSize = true;
        resumeCalibrationButton.Text = "从已有 .dmap 继续回环";

        layoutPanel.SetColumnSpan(helpLabel, 3);
        helpLabel.AutoSize = true;
        helpLabel.Dock = DockStyle.Top;
        helpLabel.ForeColor = Color.DimGray;
        helpLabel.Padding = new Padding(0, 8, 0, 0);
        helpLabel.Text = "目标宽高是预扭曲形状的自然外接尺寸，不是输入裁剪区域。完整输入始终按 1920×1080 逆映射。";

        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layoutPanel);
        Name = "CalibrationPage";
        Size = new Size(780, 620);
        layoutPanel.ResumeLayout(false);
        layoutPanel.PerformLayout();
        gridPanel.ResumeLayout(false);
        gridPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)gridColumnsNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridRowsNumeric).EndInit();
        targetPanel.ResumeLayout(false);
        targetPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)targetWidthNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)targetHeightNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)dotDiameterNumeric).EndInit();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        ResumeLayout(false);
    }
}
