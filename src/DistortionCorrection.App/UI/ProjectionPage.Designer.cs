#nullable enable
namespace DistortionCorrection.UI;

partial class ProjectionPage
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel layoutPanel = null!;
    private Label imageLabel = null!;
    private TextBox imagePathTextBox = null!;
    private Button browseButton = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button projectButton = null!;
    private Button restoreButton = null!;
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
        imageLabel = new Label();
        imagePathTextBox = new TextBox();
        browseButton = new Button();
        buttonPanel = new FlowLayoutPanel();
        projectButton = new Button();
        restoreButton = new Button();
        helpLabel = new Label();
        layoutPanel.SuspendLayout();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        layoutPanel.AutoScroll = true;
        layoutPanel.ColumnCount = 3;
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPanel.Controls.Add(imageLabel, 0, 0);
        layoutPanel.Controls.Add(imagePathTextBox, 1, 0);
        layoutPanel.Controls.Add(browseButton, 2, 0);
        layoutPanel.Controls.Add(buttonPanel, 0, 1);
        layoutPanel.Controls.Add(helpLabel, 0, 2);
        layoutPanel.Dock = DockStyle.Fill;
        layoutPanel.Padding = new Padding(18);
        layoutPanel.RowCount = 3;
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        imageLabel.Anchor = AnchorStyles.Left;
        imageLabel.AutoSize = true;
        imageLabel.Text = "投影图片：";
        imageLabel.Margin = new Padding(3, 10, 12, 10);

        imagePathTextBox.Dock = DockStyle.Fill;
        imagePathTextBox.Margin = new Padding(3, 7, 8, 7);

        browseButton.AutoSize = true;
        browseButton.Text = "浏览…";
        browseButton.Margin = new Padding(3, 5, 3, 5);

        layoutPanel.SetColumnSpan(buttonPanel, 3);
        buttonPanel.AutoSize = true;
        buttonPanel.Controls.Add(projectButton);
        buttonPanel.Controls.Add(restoreButton);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.Margin = new Padding(0, 12, 0, 8);

        projectButton.AutoSize = true;
        projectButton.Text = "切换为桌面（适应）";
        projectButton.Margin = new Padding(3, 3, 10, 3);

        restoreButton.AutoSize = true;
        restoreButton.Text = "恢复原桌面";

        layoutPanel.SetColumnSpan(helpLabel, 3);
        helpLabel.AutoSize = true;
        helpLabel.Dock = DockStyle.Top;
        helpLabel.ForeColor = Color.DimGray;
        helpLabel.Padding = new Padding(0, 8, 0, 0);
        helpLabel.Text = "使用 Windows 扩展桌面和“适应”模式；切图前保存桌面状态，恢复按钮可还原。";

        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layoutPanel);
        Name = "ProjectionPage";
        Size = new Size(780, 620);
        layoutPanel.ResumeLayout(false);
        layoutPanel.PerformLayout();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        ResumeLayout(false);
    }
}
