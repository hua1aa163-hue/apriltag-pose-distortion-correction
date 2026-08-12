#nullable enable
using DistortionCorrection.UI;

namespace DistortionCorrection;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayoutPanel = null!;
    private SplitContainer mainSplitContainer = null!;
    private TabControl navigationTabControl = null!;
    private TabPage cameraTabPage = null!;
    private TabPage projectionTabPage = null!;
    private TabPage calibrationTabPage = null!;
    private TabPage manualAdjustmentTabPage = null!;
    private TabPage batchTabPage = null!;
    private CameraPage cameraPage = null!;
    private ProjectionPage projectionPage = null!;
    private CalibrationPage calibrationPage = null!;
    private ManualAdjustmentPage manualAdjustmentPage = null!;
    private BatchWarpPage batchWarpPage = null!;
    private SplitContainer rightSplitContainer = null!;
    private GroupBox previewGroupBox = null!;
    private PictureBox previewPictureBox = null!;
    private GroupBox logGroupBox = null!;
    private TextBox logTextBox = null!;
    private FlowLayoutPanel operationPanel = null!;
    private Label operationLabel = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayoutPanel = new TableLayoutPanel();
        mainSplitContainer = new SplitContainer();
        navigationTabControl = new TabControl();
        cameraTabPage = new TabPage();
        projectionTabPage = new TabPage();
        calibrationTabPage = new TabPage();
        manualAdjustmentTabPage = new TabPage();
        batchTabPage = new TabPage();
        cameraPage = new CameraPage();
        projectionPage = new ProjectionPage();
        calibrationPage = new CalibrationPage();
        manualAdjustmentPage = new ManualAdjustmentPage();
        batchWarpPage = new BatchWarpPage();
        rightSplitContainer = new SplitContainer();
        previewGroupBox = new GroupBox();
        previewPictureBox = new PictureBox();
        logGroupBox = new GroupBox();
        logTextBox = new TextBox();
        operationPanel = new FlowLayoutPanel();
        operationLabel = new Label();
        cancelButton = new Button();
        rootLayoutPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
        mainSplitContainer.Panel1.SuspendLayout();
        mainSplitContainer.Panel2.SuspendLayout();
        mainSplitContainer.SuspendLayout();
        navigationTabControl.SuspendLayout();
        cameraTabPage.SuspendLayout();
        projectionTabPage.SuspendLayout();
        calibrationTabPage.SuspendLayout();
        manualAdjustmentTabPage.SuspendLayout();
        batchTabPage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)rightSplitContainer).BeginInit();
        rightSplitContainer.Panel1.SuspendLayout();
        rightSplitContainer.Panel2.SuspendLayout();
        rightSplitContainer.SuspendLayout();
        previewGroupBox.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)previewPictureBox).BeginInit();
        logGroupBox.SuspendLayout();
        operationPanel.SuspendLayout();
        SuspendLayout();

        rootLayoutPanel.ColumnCount = 1;
        rootLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayoutPanel.Controls.Add(mainSplitContainer, 0, 0);
        rootLayoutPanel.Controls.Add(operationPanel, 0, 1);
        rootLayoutPanel.Dock = DockStyle.Fill;
        rootLayoutPanel.RowCount = 2;
        rootLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        mainSplitContainer.Dock = DockStyle.Fill;
        mainSplitContainer.FixedPanel = FixedPanel.Panel1;
        mainSplitContainer.Panel1MinSize = 560;
        mainSplitContainer.Panel2MinSize = 360;
        mainSplitContainer.Size = new Size(1480, 850);
        mainSplitContainer.SplitterDistance = 790;
        mainSplitContainer.Panel1.Controls.Add(navigationTabControl);
        mainSplitContainer.Panel2.Controls.Add(rightSplitContainer);

        navigationTabControl.Controls.Add(cameraTabPage);
        navigationTabControl.Controls.Add(projectionTabPage);
        navigationTabControl.Controls.Add(calibrationTabPage);
        navigationTabControl.Controls.Add(manualAdjustmentTabPage);
        navigationTabControl.Controls.Add(batchTabPage);
        navigationTabControl.Dock = DockStyle.Fill;
        navigationTabControl.Padding = new Point(18, 7);

        cameraTabPage.Controls.Add(cameraPage);
        cameraTabPage.Text = "1  相机采集";
        cameraTabPage.UseVisualStyleBackColor = true;
        cameraPage.Dock = DockStyle.Fill;

        projectionTabPage.Controls.Add(projectionPage);
        projectionTabPage.Text = "2  桌面投图";
        projectionTabPage.UseVisualStyleBackColor = true;
        projectionPage.Dock = DockStyle.Fill;

        calibrationTabPage.Controls.Add(calibrationPage);
        calibrationTabPage.Text = "3  参数与回环标定";
        calibrationTabPage.UseVisualStyleBackColor = true;
        calibrationPage.Dock = DockStyle.Fill;

        manualAdjustmentTabPage.Controls.Add(manualAdjustmentPage);
        manualAdjustmentTabPage.Text = "4  人眼确认与微调";
        manualAdjustmentTabPage.UseVisualStyleBackColor = true;
        manualAdjustmentPage.Dock = DockStyle.Fill;

        batchTabPage.Controls.Add(batchWarpPage);
        batchTabPage.Text = "5  文件夹批量矫正";
        batchTabPage.UseVisualStyleBackColor = true;
        batchWarpPage.Dock = DockStyle.Fill;

        rightSplitContainer.Dock = DockStyle.Fill;
        rightSplitContainer.Orientation = Orientation.Horizontal;
        rightSplitContainer.Panel1MinSize = 300;
        rightSplitContainer.Panel2MinSize = 160;
        rightSplitContainer.Size = new Size(686, 850);
        rightSplitContainer.SplitterDistance = 470;
        rightSplitContainer.Panel1.Controls.Add(previewGroupBox);
        rightSplitContainer.Panel2.Controls.Add(logGroupBox);

        previewGroupBox.Controls.Add(previewPictureBox);
        previewGroupBox.Dock = DockStyle.Fill;
        previewGroupBox.Padding = new Padding(8);
        previewGroupBox.Text = "抓图 / 图卡 / 输出预览";
        previewPictureBox.BackColor = Color.Black;
        previewPictureBox.Dock = DockStyle.Fill;
        previewPictureBox.SizeMode = PictureBoxSizeMode.Zoom;

        logGroupBox.Controls.Add(logTextBox);
        logGroupBox.Dock = DockStyle.Fill;
        logGroupBox.Padding = new Padding(8);
        logGroupBox.Text = "运行日志";
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Font = new Font(FontFamily.GenericMonospace, 9F);
        logTextBox.Multiline = true;
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Both;
        logTextBox.WordWrap = false;

        operationPanel.AutoSize = true;
        operationPanel.Controls.Add(operationLabel);
        operationPanel.Controls.Add(cancelButton);
        operationPanel.Dock = DockStyle.Fill;
        operationPanel.FlowDirection = FlowDirection.RightToLeft;
        operationPanel.Padding = new Padding(8, 4, 8, 4);
        operationPanel.WrapContents = false;
        operationLabel.Anchor = AnchorStyles.Left;
        operationLabel.AutoSize = true;
        operationLabel.Text = "就绪";
        operationLabel.Margin = new Padding(10, 8, 8, 3);
        cancelButton.AutoSize = true;
        cancelButton.Enabled = false;
        cancelButton.Text = "取消当前任务";

        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1480, 900);
        Controls.Add(rootLayoutPanel);
        Font = new Font("Microsoft YaHei UI", 9F);
        MinimumSize = new Size(1180, 760);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "反射投影回环畸变矫正 — 1920×1080 黑底输出";
        rootLayoutPanel.ResumeLayout(false);
        rootLayoutPanel.PerformLayout();
        mainSplitContainer.Panel1.ResumeLayout(false);
        mainSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
        mainSplitContainer.ResumeLayout(false);
        navigationTabControl.ResumeLayout(false);
        cameraTabPage.ResumeLayout(false);
        projectionTabPage.ResumeLayout(false);
        calibrationTabPage.ResumeLayout(false);
        manualAdjustmentTabPage.ResumeLayout(false);
        batchTabPage.ResumeLayout(false);
        rightSplitContainer.Panel1.ResumeLayout(false);
        rightSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)rightSplitContainer).EndInit();
        rightSplitContainer.ResumeLayout(false);
        previewGroupBox.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)previewPictureBox).EndInit();
        logGroupBox.ResumeLayout(false);
        logGroupBox.PerformLayout();
        operationPanel.ResumeLayout(false);
        operationPanel.PerformLayout();
        ResumeLayout(false);
    }
}
