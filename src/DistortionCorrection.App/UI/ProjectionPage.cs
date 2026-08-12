namespace DistortionCorrection.UI;

public partial class ProjectionPage : UserControl
{
    public ProjectionPage() => InitializeComponent();

    public TextBox ImagePath => imagePathTextBox;
    public Button BrowseButton => browseButton;
    public Button ProjectButton => projectButton;
    public Button RestoreButton => restoreButton;
    public IEnumerable<Button> ActionButtons => [browseButton, projectButton, restoreButton];
}
