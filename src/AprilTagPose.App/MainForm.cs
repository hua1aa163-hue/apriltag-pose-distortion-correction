using System.Diagnostics;
using System.Globalization;
using System.Text;
using AprilTagPose.Camera;
using AprilTagPose.Imaging;
using AprilTagPose.Models;
using AprilTagPose.Services;
using OpenCvSharp;
using DrawingSize = System.Drawing.Size;

namespace AprilTagPose;

public sealed partial class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IReadOnlyDictionary<MarkerDictionaryKind, AprilTagPoseDetector> _detectors;
    private readonly HikCameraService _camera;
    private readonly object _processingOptionsSync = new();

    private ToolStripComboBox _cameraCombo = null!;
    private ToolStripButton _refreshCameraButton = null!;
    private ToolStripButton _connectButton = null!;
    private ToolStripButton _disconnectButton = null!;
    private ToolStripButton _startLiveButton = null!;
    private ToolStripButton _stopLiveButton = null!;
    private ToolStripButton _openImageButton = null!;
    private ToolStripButton _saveOverlayButton = null!;
    private ToolStripButton _exportJsonButton = null!;
    private ToolStripButton _exportCsvButton = null!;
    private PictureBox _pictureBox = null!;
    private DataGridView _resultGrid = null!;
    private RichTextBox _detailBox = null!;
    private ToolStripStatusLabel _messageStatus = null!;
    private ToolStripStatusLabel _processingStatus = null!;
    private ToolStripStatusLabel _frameStatus = null!;
    private ToolStripStatusLabel _intrinsicsStatus = null!;

    private TextBox _fxText = null!;
    private TextBox _fyText = null!;
    private TextBox _cxText = null!;
    private TextBox _cyText = null!;
    private TextBox _k1Text = null!;
    private TextBox _k2Text = null!;
    private TextBox _p1Text = null!;
    private TextBox _p2Text = null!;
    private TextBox _k3Text = null!;
    private TextBox _imageWidthText = null!;
    private TextBox _imageHeightText = null!;
    private TextBox _tagSizeText = null!;
    private ComboBox _dictionaryCombo = null!;

    private CameraCalibration? _processingCalibration;
    private double _processingTagSize = 50d;
    private MarkerDictionaryKind _processingDictionary = AprilTagPoseDetector.DefaultDictionary;
    private DetectionFrameResult? _currentResult;
    private string? _currentSourceName;
    private Task? _liveProcessingTask;
    private Task? _staticProcessingTask;
    private long _lastAcceptedFrameTimestamp;
    private int _liveFrameInProgress;
    private int _staticAnalysisGeneration;
    private int _closing;
    private bool _cameraOperationInProgress;
    private bool _calibrationIsApproximate;
    private bool _updatingCalibrationFields;
    private bool _shutdownStarted;
    private bool _shutdownComplete;

    public MainForm()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _settings.Calibration ??= new CameraCalibration();
        if (!Enum.IsDefined(_settings.MarkerDictionary))
            _settings.MarkerDictionary = AprilTagPoseDetector.DefaultDictionary;
        _detectors = new Dictionary<MarkerDictionaryKind, AprilTagPoseDetector>
        {
            [MarkerDictionaryKind.ArUco4X4_250] = new(MarkerDictionaryKind.ArUco4X4_250),
            [MarkerDictionaryKind.AprilTag16h5] = new(MarkerDictionaryKind.AprilTag16h5)
        };
        _camera = new HikCameraService();
        _camera.FrameDeliveryIntervalMilliseconds =
            Math.Clamp(_settings.LiveProcessingIntervalMilliseconds, 0, 10_000);

        InitializeWindow();
        SelectDictionary(_settings.MarkerDictionary);
        SetCalibrationFields(_settings.Calibration);
        _tagSizeText.Text = FormatEditable(
            double.IsFinite(_settings.TagSizeMillimeters) && _settings.TagSizeMillimeters > 0
                ? _settings.TagSizeMillimeters
                : 50d);
        UpdateProcessingOptionsFromFields(markAsCalibrated: false, persist: false, showErrors: false);

        _camera.FrameReceived += CameraOnFrameReceived;
        _camera.ErrorOccurred += CameraOnErrorOccurred;
        Shown += MainFormShown;
        UpdateCameraButtons();
        UpdateResultButtons();
        UpdateIntrinsicsStatus();
    }

    private void InitializeWindow()
    {
        SuspendLayout();
        Text = "ArUco / AprilTag 位姿识别（海康 MVS）";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 950;
        MinimumSize = new DrawingSize(1100, 720);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 285F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildMainArea(), 0, 1);
        root.Controls.Add(BuildResultsArea(), 0, 2);
        root.Controls.Add(BuildStatusStrip(), 0, 3);
        Controls.Add(root);
        ResumeLayout(true);
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 4, 6, 4),
            RenderMode = ToolStripRenderMode.System,
            AutoSize = false
        };

        _refreshCameraButton = CreateToolButton("刷新相机", RefreshCameraButtonClicked, "重新枚举 GigE、USB3 和 GenTL 相机");
        _cameraCombo = new ToolStripComboBox
        {
            Name = "cameraCombo",
            DropDownStyle = ComboBoxStyle.DropDownList,
            AutoSize = false,
            Width = 330,
            ToolTipText = "选择海康工业相机"
        };
        _cameraCombo.SelectedIndexChanged += (_, _) => UpdateCameraButtons();
        _connectButton = CreateToolButton("连接", ConnectButtonClicked, "打开选中的相机");
        _disconnectButton = CreateToolButton("断开", DisconnectButtonClicked, "停止取流并关闭相机");
        _startLiveButton = CreateToolButton("开始实时", StartLiveButtonClicked, "连续取流并识别所选方形标记字典");
        _stopLiveButton = CreateToolButton("停止实时", StopLiveButtonClicked, "停止连续取流");
        _openImageButton = CreateToolButton("打开图片", OpenImageButtonClicked, "打开本地图像并识别");
        _saveOverlayButton = CreateToolButton("保存叠加图", SaveOverlayButtonClicked, "保存当前带轮廓和坐标轴的图像");
        _exportJsonButton = CreateToolButton("导出 JSON", ExportJsonButtonClicked, "导出全部位姿和标定信息");
        _exportCsvButton = CreateToolButton("导出 CSV", ExportCsvButtonClicked, "导出表格位姿结果");

        toolbar.Items.Add(_refreshCameraButton);
        toolbar.Items.Add(new ToolStripLabel("相机："));
        toolbar.Items.Add(_cameraCombo);
        toolbar.Items.Add(_connectButton);
        toolbar.Items.Add(_disconnectButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_startLiveButton);
        toolbar.Items.Add(_stopLiveButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_openImageButton);
        toolbar.Items.Add(_saveOverlayButton);
        toolbar.Items.Add(_exportJsonButton);
        toolbar.Items.Add(_exportCsvButton);
        return toolbar;
    }

    private static ToolStripButton CreateToolButton(string text, EventHandler handler, string toolTip)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = toolTip,
            AutoSize = true
        };
        button.Click += handler;
        return button;
    }

    private Control BuildMainArea()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new DrawingSize(1488, 590),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 330,
            SplitterWidth = 6,
            SplitterDistance = 1080
        };

        var imageGroup = new GroupBox
        {
            Text = "图像（滚轮缩放由 Zoom 模式自动适配窗口）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };
        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 27, 31),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };
        imageGroup.Controls.Add(_pictureBox);
        split.Panel1.Padding = new Padding(6, 3, 3, 3);
        split.Panel1.Controls.Add(imageGroup);
        split.Panel2.Padding = new Padding(3, 3, 6, 3);
        split.Panel2.Controls.Add(BuildParameterPanel());
        return split;
    }

    private Control BuildParameterPanel()
    {
        var group = new GroupBox
        {
            Text = "相机内参与标签参数",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Padding = new Padding(3)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _fxText = AddParameterRow(table, "fx（像素）", "水平方向焦距");
        _fyText = AddParameterRow(table, "fy（像素）", "垂直方向焦距");
        _cxText = AddParameterRow(table, "cx（像素）", "主点横坐标");
        _cyText = AddParameterRow(table, "cy（像素）", "主点纵坐标");
        _k1Text = AddParameterRow(table, "k1", "径向畸变系数 k1");
        _k2Text = AddParameterRow(table, "k2", "径向畸变系数 k2");
        _p1Text = AddParameterRow(table, "p1", "切向畸变系数 p1");
        _p2Text = AddParameterRow(table, "p2", "切向畸变系数 p2");
        _k3Text = AddParameterRow(table, "k3", "径向畸变系数 k3");
        _imageWidthText = AddParameterRow(table, "标定图像宽度", "内参对应的图像宽度（像素）");
        _imageHeightText = AddParameterRow(table, "标定图像高度", "内参对应的图像高度（像素）");
        _dictionaryCombo = AddComboParameterRow(table, "识别字典", "选择现场标记对应的码族；默认使用 DICT_4X4_250");
        _dictionaryCombo.Items.Add(new MarkerDictionaryOption(
            MarkerDictionaryKind.ArUco4X4_250,
            MarkerDictionaryKind.ArUco4X4_250.DisplayName()));
        _dictionaryCombo.Items.Add(new MarkerDictionaryOption(
            MarkerDictionaryKind.AprilTag16h5,
            MarkerDictionaryKind.AprilTag16h5.DisplayName()));
        _dictionaryCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_dictionaryCombo.SelectedItem is MarkerDictionaryOption option)
                Text = $"{option.Kind.DisplayName()} 位姿识别（海康 MVS）";
        };
        _tagSizeText = AddParameterRow(table, "标签黑框边长（mm）", "方形标记外侧黑色正方形的实际边长");
        foreach (TextBox input in new[]
                 {
                     _fxText, _fyText, _cxText, _cyText, _k1Text, _k2Text,
                     _p1Text, _p2Text, _k3Text, _imageWidthText, _imageHeightText
                 })
        {
            input.TextChanged += CalibrationFieldEdited;
        }

        var parameterButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        parameterButtons.Controls.Add(CreateButton("应用参数", ApplyParametersButtonClicked));
        parameterButtons.Controls.Add(CreateButton("近似内参", ApproximateIntrinsicsButtonClicked));
        parameterButtons.Controls.Add(CreateButton("加载标定", LoadCalibrationButtonClicked));
        parameterButtons.Controls.Add(CreateButton("保存标定", SaveCalibrationButtonClicked));
        int buttonRow = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(parameterButtons, 0, buttonRow);
        table.SetColumnSpan(parameterButtons, 2);

        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new DrawingSize(310, 0),
            ForeColor = Color.FromArgb(170, 90, 0),
            Text = "提示：近似内参只适合确认标签和粗略位姿。精确测量必须加载与当前分辨率匹配的真实标定参数。",
            Margin = new Padding(3, 10, 3, 3)
        };
        int warningRow = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(warning, 0, warningRow);
        table.SetColumnSpan(warning, 2);

        scroll.Controls.Add(table);
        group.Controls.Add(scroll);
        return group;
    }

    private static TextBox AddParameterRow(TableLayoutPanel table, string labelText, string toolTip)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 8, 5)
        };
        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 3),
            AccessibleDescription = toolTip
        };
        var tip = new ToolTip();
        tip.SetToolTip(label, toolTip);
        tip.SetToolTip(textBox, toolTip);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(textBox, 1, row);
        return textBox;
    }

    private static ComboBox AddComboParameterRow(TableLayoutPanel table, string labelText, string toolTip)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 8, 5)
        };
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3),
            AccessibleDescription = toolTip
        };
        var tip = new ToolTip();
        tip.SetToolTip(label, toolTip);
        tip.SetToolTip(comboBox, toolTip);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(comboBox, 1, row);
        return comboBox;
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new DrawingSize(88, 30),
            Margin = new Padding(3)
        };
        button.Click += handler;
        return button;
    }

    private Control BuildResultsArea()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new DrawingSize(1488, 279),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 330,
            SplitterWidth = 6,
            SplitterDistance = 1080
        };

        var gridGroup = new GroupBox { Text = "识别与位姿结果", Dock = DockStyle.Fill, Padding = new Padding(6) };
        _resultGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        AddResultColumn("ID", "ID", 48);
        AddResultColumn("CenterX", "中心 X(px)", 86);
        AddResultColumn("CenterY", "中心 Y(px)", 86);
        AddResultColumn("Tx", "tx(mm)", 82);
        AddResultColumn("Ty", "ty(mm)", 82);
        AddResultColumn("Tz", "tz(mm)", 82);
        AddResultColumn("Distance", "距离(mm)", 90);
        AddResultColumn("Roll", "Roll(°)", 78);
        AddResultColumn("Pitch", "Pitch(°)", 78);
        AddResultColumn("Yaw", "Yaw(°)", 78);
        AddResultColumn("Error", "重投影误差(px)", 120, DataGridViewAutoSizeColumnMode.Fill);
        _resultGrid.SelectionChanged += (_, _) => UpdateSelectedTagDetails();
        gridGroup.Controls.Add(_resultGrid);

        var detailGroup = new GroupBox { Text = "选中标签详细信息", Dock = DockStyle.Fill, Padding = new Padding(6) };
        _detailBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            DetectUrls = false,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "识别后选择表格中的标签查看四角、旋转向量、旋转矩阵与四元数。"
        };
        detailGroup.Controls.Add(_detailBox);

        split.Panel1.Padding = new Padding(6, 3, 3, 3);
        split.Panel1.Controls.Add(gridGroup);
        split.Panel2.Padding = new Padding(3, 3, 6, 3);
        split.Panel2.Controls.Add(detailGroup);
        return split;
    }

    private void AddResultColumn(
        string name,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.None)
    {
        _resultGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            MinimumWidth = Math.Min(width, 45),
            AutoSizeMode = autoSizeMode,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
    }

    private StatusStrip BuildStatusStrip()
    {
        var strip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
        _messageStatus = new ToolStripStatusLabel("就绪")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _processingStatus = CreateStatusLabel("耗时：—");
        _frameStatus = CreateStatusLabel("帧号：—");
        _intrinsicsStatus = CreateStatusLabel("内参：未设置");
        strip.Items.AddRange([_messageStatus, _processingStatus, _frameStatus, _intrinsicsStatus]);
        return strip;
    }

    private static ToolStripStatusLabel CreateStatusLabel(string text) => new(text)
    {
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        BorderStyle = Border3DStyle.Etched,
        Padding = new Padding(8, 0, 4, 0)
    };

    private async void MainFormShown(object? sender, EventArgs e)
    {
        await RefreshCamerasAsync().ConfigureAwait(true);
        if (Volatile.Read(ref _closing) != 0) return;

        if (_settings.AutoLoadLastImage &&
            !string.IsNullOrWhiteSpace(_settings.LastImagePath) &&
            File.Exists(_settings.LastImagePath))
        {
            await AnalyzeImageAsync(_settings.LastImagePath, showErrors: true).ConfigureAwait(true);
        }
    }

    private async void RefreshCameraButtonClicked(object? sender, EventArgs e) =>
        await RefreshCamerasAsync().ConfigureAwait(true);

    private async Task RefreshCamerasAsync()
    {
        if (_cameraOperationInProgress || Volatile.Read(ref _closing) != 0) return;
        _cameraOperationInProgress = true;
        UpdateCameraButtons();
        SetMessage("正在枚举相机……");
        try
        {
            IReadOnlyList<CameraDescriptor> cameras = await Task.Run(_camera.EnumerateDevices).ConfigureAwait(true);
            _cameraCombo.Items.Clear();
            foreach (CameraDescriptor camera in cameras) _cameraCombo.Items.Add(camera);
            if (_cameraCombo.Items.Count > 0) _cameraCombo.SelectedIndex = 0;
            SetMessage(cameras.Count == 0
                ? $"未发现相机（MVS SDK {_camera.SdkVersion}）"
                : $"发现 {cameras.Count} 台相机（MVS SDK {_camera.SdkVersion}）");
        }
        catch (Exception exception)
        {
            ShowOperationError("枚举相机失败", exception);
        }
        finally
        {
            _cameraOperationInProgress = false;
            UpdateCameraButtons();
        }
    }

    private async void ConnectButtonClicked(object? sender, EventArgs e)
    {
        if (_cameraCombo.SelectedItem is not CameraDescriptor descriptor) return;
        await RunCameraOperationAsync(
            () => _camera.OpenAsync(descriptor),
            $"正在连接 {descriptor.DisplayName}……",
            $"已连接 {descriptor.DisplayName}").ConfigureAwait(true);
    }

    private async void DisconnectButtonClicked(object? sender, EventArgs e)
    {
        await RunCameraOperationAsync(
            _camera.CloseAsync,
            "正在断开相机……",
            "相机已断开").ConfigureAwait(true);
    }

    private async void StartLiveButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            ProcessingOptions options = CaptureProcessingOptionsFromFields(updateSettings: true);
            string calibrationSerial = options.Calibration?.CameraSerialNumber ?? string.Empty;
            string currentSerial = _camera.CurrentCamera?.SerialNumber ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(calibrationSerial) &&
                !string.IsNullOrWhiteSpace(currentSerial) &&
                !string.Equals(calibrationSerial, currentSerial, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"当前标定属于相机 {calibrationSerial}，已连接相机为 {currentSerial}。请加载匹配的标定文件。");
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("参数无效", exception);
            return;
        }

        await RunCameraOperationAsync(
            () => _camera.StartGrabbingAsync(),
            "正在启动实时取流……",
            "实时识别已开始").ConfigureAwait(true);
    }

    private async void StopLiveButtonClicked(object? sender, EventArgs e)
    {
        await RunCameraOperationAsync(
            _camera.StopGrabbingAsync,
            "正在停止实时取流……",
            "实时识别已停止").ConfigureAwait(true);
    }

    private async Task RunCameraOperationAsync(Func<Task> operation, string runningMessage, string successMessage)
    {
        if (_cameraOperationInProgress || Volatile.Read(ref _closing) != 0) return;
        _cameraOperationInProgress = true;
        UpdateCameraButtons();
        SetMessage(runningMessage);
        try
        {
            await operation().ConfigureAwait(true);
            SetMessage(successMessage);
        }
        catch (Exception exception)
        {
            ShowOperationError("相机操作失败", exception);
        }
        finally
        {
            _cameraOperationInProgress = false;
            UpdateCameraButtons();
        }
    }

    private async void OpenImageButtonClicked(object? sender, EventArgs e)
    {
        if (_camera.IsGrabbing)
        {
            await RunCameraOperationAsync(_camera.StopGrabbingAsync, "正在停止实时取流……", "实时识别已停止")
                .ConfigureAwait(true);
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"选择包含 {SelectedDictionary().DisplayName()} 标记的图像",
            Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|BMP 图像|*.bmp|PNG 图像|*.png|JPEG 图像|*.jpg;*.jpeg|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        string? initialDirectory = GetExistingDirectory(_settings.LastImagePath);
        if (!string.IsNullOrWhiteSpace(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.LastImagePath = dialog.FileName;
        SaveSettingsQuietly();
        await AnalyzeImageAsync(dialog.FileName, showErrors: true).ConfigureAwait(true);
    }

    private async Task AnalyzeImageAsync(string path, bool showErrors)
    {
        if (Volatile.Read(ref _closing) != 0) return;
        ProcessingOptions options;
        try
        {
            options = CaptureProcessingOptionsFromFields(updateSettings: true);
        }
        catch (Exception exception)
        {
            if (showErrors) ShowOperationError("参数无效", exception);
            return;
        }

        int generation = Interlocked.Increment(ref _staticAnalysisGeneration);
        SetMessage($"正在分析 {Path.GetFileName(path)}……");
        _openImageButton.Enabled = false;
        Task<DetectionUiPayload> processingTask = Task.Run(() => ProcessImageFile(path, options));
        _staticProcessingTask = processingTask;
        try
        {
            DetectionUiPayload payload = await processingTask.ConfigureAwait(true);
            if (Volatile.Read(ref _closing) != 0 || generation != Volatile.Read(ref _staticAnalysisGeneration))
            {
                payload.Dispose();
                return;
            }

            ApplyDetectionPayload(payload);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _closing) == 0)
            {
                SetMessage($"分析失败：{exception.Message}");
                if (showErrors) ShowOperationError("图像分析失败", exception);
            }
        }
        finally
        {
            if (ReferenceEquals(_staticProcessingTask, processingTask)) _staticProcessingTask = null;
            if (Volatile.Read(ref _closing) == 0) _openImageButton.Enabled = true;
        }
    }

    private DetectionUiPayload ProcessImageFile(string path, ProcessingOptions options)
    {
        using var bitmap = new Bitmap(path);
        using Mat source = BitmapMatConverter.ToMat(bitmap);
        DetectionFrameResult? result = null;
        Bitmap? display = null;
        try
        {
            result = DetectorFor(options.Dictionary).Detect(
                source, options.Calibration, options.TagSizeMillimeters, File.GetLastWriteTime(path));
            display = BitmapMatConverter.ToBitmap(result.AnnotatedImage);
            return new DetectionUiPayload(result, display, path, null);
        }
        catch
        {
            result?.Dispose();
            display?.Dispose();
            throw;
        }
    }

    private void CameraOnFrameReceived(object? sender, CameraFrameEventArgs frame)
    {
        if (Volatile.Read(ref _closing) != 0 || !_camera.IsGrabbing)
        {
            frame.Dispose();
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long last = Volatile.Read(ref _lastAcceptedFrameTimestamp);
        int intervalMilliseconds = Math.Clamp(_settings.LiveProcessingIntervalMilliseconds, 0, 10_000);
        long requiredTicks = intervalMilliseconds * Stopwatch.Frequency / 1000L;
        if (last != 0 && now - last < requiredTicks)
        {
            frame.Dispose();
            return;
        }

        if (Interlocked.CompareExchange(ref _liveFrameInProgress, 1, 0) != 0)
        {
            frame.Dispose();
            return;
        }

        Volatile.Write(ref _lastAcceptedFrameTimestamp, now);
        ProcessingOptions options;
        lock (_processingOptionsSync)
        {
            options = new ProcessingOptions(
                _processingCalibration?.Clone(),
                _processingTagSize,
                _processingDictionary);
        }

        _liveProcessingTask = Task.Run(() => ProcessLiveFrame(frame, options));
    }

    private void ProcessLiveFrame(CameraFrameEventArgs frame, ProcessingOptions options)
    {
        using (frame)
        {
            try
            {
                using Mat source = BitmapMatConverter.ToMat(frame.Image);
                DetectionFrameResult? result = null;
                Bitmap? display = null;
                try
                {
                    result = DetectorFor(options.Dictionary).Detect(
                        source, options.Calibration, options.TagSizeMillimeters, frame.Metadata.CapturedAt);
                    display = BitmapMatConverter.ToBitmap(result.AnnotatedImage);
                    var payload = new DetectionUiPayload(
                        result,
                        display,
                        BuildLiveSourceName(),
                        frame.Metadata.FrameNumber);
                    result = null;
                    display = null;
                    QueueLivePayload(payload);
                }
                finally
                {
                    result?.Dispose();
                    display?.Dispose();
                }
            }
            catch (Exception exception)
            {
                QueueLiveFailure(exception);
            }
        }
    }

    private string BuildLiveSourceName()
    {
        CameraDescriptor? camera = _camera.CurrentCamera;
        if (camera is null) return "HikCamera";
        return string.IsNullOrWhiteSpace(camera.SerialNumber)
            ? camera.DisplayName
            : $"HikCamera_{camera.SerialNumber}";
    }

    private void QueueLivePayload(DetectionUiPayload payload)
    {
        if (Volatile.Read(ref _closing) != 0 || IsDisposed || !IsHandleCreated)
        {
            payload.Dispose();
            Interlocked.Exchange(ref _liveFrameInProgress, 0);
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                try
                {
                    if (Volatile.Read(ref _closing) != 0) payload.Dispose();
                    else ApplyDetectionPayload(payload);
                }
                finally
                {
                    Interlocked.Exchange(ref _liveFrameInProgress, 0);
                }
            }));
        }
        catch
        {
            payload.Dispose();
            Interlocked.Exchange(ref _liveFrameInProgress, 0);
        }
    }

    private void QueueLiveFailure(Exception exception)
    {
        if (Volatile.Read(ref _closing) != 0 || IsDisposed || !IsHandleCreated)
        {
            Interlocked.Exchange(ref _liveFrameInProgress, 0);
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                SetMessage($"实时帧处理失败：{exception.Message}");
                Interlocked.Exchange(ref _liveFrameInProgress, 0);
            }));
        }
        catch
        {
            Interlocked.Exchange(ref _liveFrameInProgress, 0);
        }
    }

    private void ApplyDetectionPayload(DetectionUiPayload payload)
    {
        DetectionFrameResult result = payload.TakeResult();
        Bitmap image = payload.TakeDisplayImage();
        payload.Dispose();

        DetectionFrameResult? oldResult = _currentResult;
        Image? oldImage = _pictureBox.Image;
        _currentResult = result;
        _currentSourceName = payload.SourceName;
        _pictureBox.Image = image;
        oldImage?.Dispose();
        oldResult?.Dispose();

        PopulateResultGrid(result.Tags, result.Dictionary);
        _processingStatus.Text = $"耗时：{result.ProcessingTime.TotalMilliseconds:F1} ms";
        _frameStatus.Text = payload.FrameNumber.HasValue ? $"帧号：{payload.FrameNumber.Value}" : "帧号：—";
        _intrinsicsStatus.Text = result.UsesApproximateIntrinsics ? "内参：近似" : "内参：已标定";
        _intrinsicsStatus.ForeColor = result.UsesApproximateIntrinsics
            ? Color.FromArgb(190, 100, 0)
            : Color.FromArgb(0, 115, 55);
        if (result.Tags.Count == 0 && result.RejectedCandidateCount > 0)
        {
            SetMessage($"没有有效 {result.Dictionary.ShortName()}；{result.RejectedCandidateCount} 个候选未通过码字校验");
        }
        else
        {
            SetMessage($"识别到 {result.Tags.Count} 个 {result.Dictionary.ShortName()} 标记，拒绝候选 {result.RejectedCandidateCount} 个");
        }

        if (result.UsesApproximateIntrinsics)
        {
            _calibrationIsApproximate = true;
            SetCalibrationFields(result.CalibrationUsed);
            lock (_processingOptionsSync) _processingCalibration = null;
        }

        UpdateResultButtons();
    }

    private void PopulateResultGrid(
        IReadOnlyList<TagPoseResult> tags,
        MarkerDictionaryKind dictionary)
    {
        _resultGrid.SuspendLayout();
        try
        {
            _resultGrid.Rows.Clear();
            foreach (TagPoseResult tag in tags)
            {
                double[] translation = tag.TranslationMillimeters;
                bool hasPose = HasPose(tag);
                int rowIndex = _resultGrid.Rows.Add(
                    tag.Id.ToString(CultureInfo.InvariantCulture),
                    FormatValue(tag.Center.X),
                    FormatValue(tag.Center.Y),
                    FormatValue(VectorElement(translation, 0)),
                    FormatValue(VectorElement(translation, 1)),
                    FormatValue(VectorElement(translation, 2)),
                    FormatValue(tag.DistanceMillimeters),
                    FormatValue(hasPose ? tag.EulerDegrees.Roll : double.NaN),
                    FormatValue(hasPose ? tag.EulerDegrees.Pitch : double.NaN),
                    FormatValue(hasPose ? tag.EulerDegrees.Yaw : double.NaN),
                    FormatValue(tag.ReprojectionErrorPixels));
                _resultGrid.Rows[rowIndex].Tag = tag;
            }
        }
        finally
        {
            _resultGrid.ResumeLayout();
        }

        if (_resultGrid.Rows.Count > 0)
        {
            _resultGrid.ClearSelection();
            _resultGrid.Rows[0].Selected = true;
            _resultGrid.CurrentCell = _resultGrid.Rows[0].Cells[0];
        }
        else
        {
            _detailBox.Text = $"当前图像未识别到 {dictionary.DisplayName()} 标记。";
        }
    }

    private void UpdateSelectedTagDetails()
    {
        if (_resultGrid.CurrentRow?.Tag is not TagPoseResult tag)
        {
            if (_resultGrid.Rows.Count > 0) return;
            _detailBox.Text = "当前没有可显示的标签。";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"ID: {tag.Id}");
        builder.AppendLine($"中心(px): ({FormatValue(tag.Center.X, "0.###")}, {FormatValue(tag.Center.Y, "0.###")})");
        builder.AppendLine("四角(px，检测顺序):");
        for (int index = 0; index < tag.Corners.Length; index++)
        {
            builder.AppendLine($"  P{index}: ({FormatValue(tag.Corners[index].X, "0.###")}, {FormatValue(tag.Corners[index].Y, "0.###")})");
        }

        builder.AppendLine($"平移向量(mm): {FormatVector(tag.TranslationMillimeters)}");
        builder.AppendLine($"距离(mm): {FormatValue(tag.DistanceMillimeters, "0.###")}");
        builder.AppendLine($"旋转向量 rvec(rad): {FormatVector(tag.RotationVector, "0.######")}");
        if (!HasPose(tag))
        {
            if (double.IsFinite(tag.ReprojectionErrorPixels) &&
                tag.ReprojectionErrorPixels > AprilTagPoseDetector.MaximumAcceptedReprojectionErrorPixels)
            {
                builder.AppendLine(
                    $"位姿求解: 已拒绝（重投影误差 {FormatValue(tag.ReprojectionErrorPixels, "0.###")} px，阈值 {AprilTagPoseDetector.MaximumAcceptedReprojectionErrorPixels:0.#} px）");
            }
            else
            {
                builder.AppendLine("位姿求解: 失败（仅保留标签像素坐标）");
            }
            builder.AppendLine($"重投影误差(px): {FormatValue(tag.ReprojectionErrorPixels, "0.######")}");
            builder.AppendLine($"周长(px): {FormatValue(tag.PerimeterPixels)}");
            builder.AppendLine($"面积(px²): {FormatValue(tag.AreaPixels)}");
            builder.AppendLine($"内参类型: {(tag.PoseUsesApproximateIntrinsics ? "近似（仅供预览）" : "已标定")}");
            _detailBox.Text = builder.ToString();
            return;
        }

        builder.AppendLine("旋转矩阵:");
        for (int row = 0; row < 3; row++)
        {
            builder.AppendLine($"  [{FormatValue(tag.RotationMatrix[row, 0], "0.######"),11} {FormatValue(tag.RotationMatrix[row, 1], "0.######"),11} {FormatValue(tag.RotationMatrix[row, 2], "0.######"),11}]");
        }

        builder.AppendLine($"欧拉角 ZYX(°): Roll={FormatValue(tag.EulerDegrees.Roll)}, Pitch={FormatValue(tag.EulerDegrees.Pitch)}, Yaw={FormatValue(tag.EulerDegrees.Yaw)}");
        builder.AppendLine($"四元数 XYZW: ({FormatValue(tag.Quaternion.X, "0.######")}, {FormatValue(tag.Quaternion.Y, "0.######")}, {FormatValue(tag.Quaternion.Z, "0.######")}, {FormatValue(tag.Quaternion.W, "0.######")})");
        builder.AppendLine($"重投影误差(px): {FormatValue(tag.ReprojectionErrorPixels, "0.######")}");
        builder.AppendLine($"周长(px): {FormatValue(tag.PerimeterPixels)}");
        builder.AppendLine($"面积(px²): {FormatValue(tag.AreaPixels)}");
        builder.AppendLine($"内参类型: {(tag.PoseUsesApproximateIntrinsics ? "近似（仅供预览）" : "已标定")}");
        _detailBox.Text = builder.ToString();
    }

    private void ApplyParametersButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            UpdateProcessingOptionsFromFields(markAsCalibrated: true, persist: true, showErrors: true);
            SetMessage("参数已应用并保存");
        }
        catch (Exception exception)
        {
            ShowOperationError("参数无效", exception);
        }
    }

    private void ApproximateIntrinsicsButtonClicked(object? sender, EventArgs e)
    {
        int width = _pictureBox.Image?.Width ?? ParsePositiveInt(_imageWidthText.Text);
        int height = _pictureBox.Image?.Height ?? ParsePositiveInt(_imageHeightText.Text);
        if (width <= 0) width = 1920;
        if (height <= 0) height = 1080;

        CameraCalibration approximate = CameraCalibration.CreateApproximate(width, height);
        SetCalibrationFields(approximate);
        _calibrationIsApproximate = true;
        lock (_processingOptionsSync)
        {
            _processingCalibration = null;
            if (TryParseFiniteDouble(_tagSizeText.Text, out double tagSize) && tagSize > 0)
                _processingTagSize = tagSize;
        }

        _settings.Calibration = new CameraCalibration();
        SaveSettingsQuietly();
        UpdateIntrinsicsStatus();
        SetMessage($"已按 {width} × {height} 生成近似内参；精确位姿请加载真实标定");
    }

    private void LoadCalibrationButtonClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "加载相机标定 JSON",
            Filter = "JSON 标定文件|*.json|所有文件|*.*",
            CheckFileExists = true,
            RestoreDirectory = true
        };
        string? directory = GetPreferredExportDirectory();
        if (!string.IsNullOrWhiteSpace(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CameraCalibration calibration = SettingsStore.LoadCalibration(dialog.FileName);
            if (calibration.CalibratedAt is null) calibration.CalibratedAt = DateTimeOffset.Now;
            SetCalibrationFields(calibration);
            _calibrationIsApproximate = false;
            _settings.Calibration = calibration.Clone();
            _settings.LastExportDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            UpdateProcessingOptionsFromFields(markAsCalibrated: false, persist: true, showErrors: true);
            SetMessage($"已加载标定：{Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            ShowOperationError("加载标定失败", exception);
        }
    }

    private void SaveCalibrationButtonClicked(object? sender, EventArgs e)
    {
        CameraCalibration calibration;
        try
        {
            calibration = ReadCalibrationFromFields();
            if (!calibration.IsValid) throw new InvalidDataException("fx、fy 必须大于 0，cx、cy 必须是有限数字。");
            calibration.CameraSerialNumber = _camera.CurrentCamera?.SerialNumber ?? calibration.CameraSerialNumber;
            calibration.CalibratedAt ??= DateTimeOffset.Now;
        }
        catch (Exception exception)
        {
            ShowOperationError("标定参数无效", exception);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "保存相机标定 JSON",
            Filter = "JSON 标定文件|*.json",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "camera-calibration.json",
            RestoreDirectory = true
        };
        string? directory = GetPreferredExportDirectory();
        if (!string.IsNullOrWhiteSpace(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SettingsStore.SaveCalibration(dialog.FileName, calibration);
            _settings.LastExportDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            SaveSettingsQuietly();
            SetMessage($"标定已保存：{dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowOperationError("保存标定失败", exception);
        }
    }

    private void SaveOverlayButtonClicked(object? sender, EventArgs e)
    {
        if (_pictureBox.Image is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "保存检测叠加图",
            Filter = "PNG 图像|*.png|BMP 图像|*.bmp|JPEG 图像|*.jpg",
            DefaultExt = "png",
            AddExtension = true,
            FileName = BuildDefaultExportName("annotated", "png"),
            RestoreDirectory = true
        };
        string? directory = GetPreferredExportDirectory();
        if (!string.IsNullOrWhiteSpace(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var copy = new Bitmap(_pictureBox.Image);
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            System.Drawing.Imaging.ImageFormat format = extension switch
            {
                ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                _ => System.Drawing.Imaging.ImageFormat.Png
            };
            copy.Save(dialog.FileName, format);
            RememberExportDirectory(dialog.FileName);
            SetMessage($"叠加图已保存：{dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowOperationError("保存叠加图失败", exception);
        }
    }

    private void ExportJsonButtonClicked(object? sender, EventArgs e) => ExportDetection(json: true);

    private void ExportCsvButtonClicked(object? sender, EventArgs e) => ExportDetection(json: false);

    private void ExportDetection(bool json)
    {
        DetectionFrameResult? result = _currentResult;
        if (result is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = json ? "导出检测结果 JSON" : "导出检测结果 CSV",
            Filter = json ? "JSON 文件|*.json" : "CSV 文件|*.csv",
            DefaultExt = json ? "json" : "csv",
            AddExtension = true,
            FileName = BuildDefaultExportName("poses", json ? "json" : "csv"),
            RestoreDirectory = true
        };
        string? directory = GetPreferredExportDirectory();
        if (!string.IsNullOrWhiteSpace(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            double tagSize = TryParseFiniteDouble(_tagSizeText.Text, out double parsed) && parsed > 0
                ? parsed
                : _settings.TagSizeMillimeters;
            if (json) DetectionExporter.SaveJson(dialog.FileName, result, _currentSourceName, tagSize);
            else DetectionExporter.SaveCsv(dialog.FileName, result);
            RememberExportDirectory(dialog.FileName);
            SetMessage($"结果已导出：{dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowOperationError("导出失败", exception);
        }
    }

    private ProcessingOptions CaptureProcessingOptionsFromFields(bool updateSettings)
    {
        CameraCalibration calibration = ReadCalibrationFromFields();
        if (!TryParseFiniteDouble(_tagSizeText.Text, out double tagSize) || tagSize <= 0)
            throw new InvalidDataException("标签黑框边长必须是大于 0 的数字（单位 mm）。");
        MarkerDictionaryKind dictionary = SelectedDictionary();

        CameraCalibration? configured = !_calibrationIsApproximate && calibration.IsValid
            ? calibration
            : null;
        lock (_processingOptionsSync)
        {
            _processingCalibration = configured?.Clone();
            _processingTagSize = tagSize;
            _processingDictionary = dictionary;
        }

        if (updateSettings)
        {
            _settings.TagSizeMillimeters = tagSize;
            _settings.MarkerDictionary = dictionary;
            _settings.Calibration = configured?.Clone() ?? new CameraCalibration();
            SaveSettingsQuietly();
        }

        UpdateIntrinsicsStatus();
        return new ProcessingOptions(configured, tagSize, dictionary);
    }

    private void UpdateProcessingOptionsFromFields(bool markAsCalibrated, bool persist, bool showErrors)
    {
        try
        {
            CameraCalibration calibration = ReadCalibrationFromFields();
            if (!calibration.IsValid)
                throw new InvalidDataException("fx、fy 必须大于 0，cx、cy 必须是有限数字。");
            if (!TryParseFiniteDouble(_tagSizeText.Text, out double tagSize) || tagSize <= 0)
                throw new InvalidDataException("标签黑框边长必须是大于 0 的数字（单位 mm）。");
            MarkerDictionaryKind dictionary = SelectedDictionary();

            if (markAsCalibrated)
            {
                calibration.CalibratedAt ??= DateTimeOffset.Now;
                calibration.CameraSerialNumber = _camera.CurrentCamera?.SerialNumber ?? calibration.CameraSerialNumber;
                _calibrationIsApproximate = false;
            }

            CameraCalibration? configured = _calibrationIsApproximate ? null : calibration;
            lock (_processingOptionsSync)
            {
                _processingCalibration = configured?.Clone();
                _processingTagSize = tagSize;
                _processingDictionary = dictionary;
            }

            _settings.TagSizeMillimeters = tagSize;
            _settings.MarkerDictionary = dictionary;
            _settings.Calibration = configured?.Clone() ?? new CameraCalibration();
            if (persist) SaveSettingsQuietly();
            UpdateIntrinsicsStatus();
        }
        catch
        {
            lock (_processingOptionsSync)
            {
                _processingCalibration = null;
                _processingTagSize = double.IsFinite(_settings.TagSizeMillimeters) && _settings.TagSizeMillimeters > 0
                    ? _settings.TagSizeMillimeters
                    : 50d;
                _processingDictionary = Enum.IsDefined(_settings.MarkerDictionary)
                    ? _settings.MarkerDictionary
                    : AprilTagPoseDetector.DefaultDictionary;
            }

            UpdateIntrinsicsStatus();
            if (showErrors) throw;
        }
    }

    private CameraCalibration ReadCalibrationFromFields()
    {
        return new CameraCalibration
        {
            Fx = ParseFiniteDouble(_fxText.Text, "fx"),
            Fy = ParseFiniteDouble(_fyText.Text, "fy"),
            Cx = ParseFiniteDouble(_cxText.Text, "cx"),
            Cy = ParseFiniteDouble(_cyText.Text, "cy"),
            K1 = ParseFiniteDouble(_k1Text.Text, "k1"),
            K2 = ParseFiniteDouble(_k2Text.Text, "k2"),
            P1 = ParseFiniteDouble(_p1Text.Text, "p1"),
            P2 = ParseFiniteDouble(_p2Text.Text, "p2"),
            K3 = ParseFiniteDouble(_k3Text.Text, "k3"),
            ImageWidth = ParseNonNegativeInt(_imageWidthText.Text, "标定图像宽度"),
            ImageHeight = ParseNonNegativeInt(_imageHeightText.Text, "标定图像高度"),
            CameraSerialNumber = _settings.Calibration.CameraSerialNumber,
            CalibratedAt = _settings.Calibration.CalibratedAt
        };
    }

    private void SetCalibrationFields(CameraCalibration calibration)
    {
        _updatingCalibrationFields = true;
        try
        {
            _fxText.Text = FormatEditable(calibration.Fx);
            _fyText.Text = FormatEditable(calibration.Fy);
            _cxText.Text = FormatEditable(calibration.Cx);
            _cyText.Text = FormatEditable(calibration.Cy);
            _k1Text.Text = FormatEditable(calibration.K1);
            _k2Text.Text = FormatEditable(calibration.K2);
            _p1Text.Text = FormatEditable(calibration.P1);
            _p2Text.Text = FormatEditable(calibration.P2);
            _k3Text.Text = FormatEditable(calibration.K3);
            _imageWidthText.Text = calibration.ImageWidth.ToString(CultureInfo.InvariantCulture);
            _imageHeightText.Text = calibration.ImageHeight.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingCalibrationFields = false;
        }
    }

    private void CalibrationFieldEdited(object? sender, EventArgs e)
    {
        if (_updatingCalibrationFields) return;
        _calibrationIsApproximate = false;
        UpdateIntrinsicsStatus();
    }

    private void UpdateIntrinsicsStatus()
    {
        bool valid = false;
        try { valid = ReadCalibrationFromFields().IsValid; }
        catch { }

        if (_calibrationIsApproximate || !valid)
        {
            _intrinsicsStatus.Text = valid ? "内参：近似" : "内参：自动近似";
            _intrinsicsStatus.ForeColor = Color.FromArgb(190, 100, 0);
        }
        else
        {
            _intrinsicsStatus.Text = "内参：已设置";
            _intrinsicsStatus.ForeColor = Color.FromArgb(0, 115, 55);
        }
    }

    private void UpdateCameraButtons()
    {
        if (_cameraCombo is null) return;
        bool open = _camera.IsOpen;
        bool grabbing = _camera.IsGrabbing;
        bool available = !_cameraOperationInProgress && Volatile.Read(ref _closing) == 0;
        _refreshCameraButton.Enabled = available && !open;
        _cameraCombo.Enabled = available && !open;
        _connectButton.Enabled = available && !open && _cameraCombo.SelectedItem is CameraDescriptor;
        _disconnectButton.Enabled = available && open;
        _startLiveButton.Enabled = available && open && !grabbing;
        _stopLiveButton.Enabled = available && open && grabbing;
    }

    private void UpdateResultButtons()
    {
        bool hasResult = _currentResult is not null;
        _saveOverlayButton.Enabled = _pictureBox.Image is not null;
        _exportJsonButton.Enabled = hasResult;
        _exportCsvButton.Enabled = hasResult;
    }

    private void CameraOnErrorOccurred(object? sender, CameraErrorEventArgs e)
    {
        if (Volatile.Read(ref _closing) != 0 || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                SetMessage(e.Message);
                UpdateCameraButtons();
                if (e.IsFatal)
                {
                    MessageBox.Show(this, e.Message, "相机错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
        }
        catch
        {
            // 窗体关闭期间忽略迟到的 SDK 错误事件。
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownComplete)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnFormClosing(e);
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _ = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        Interlocked.Exchange(ref _closing, 1);
        Interlocked.Increment(ref _staticAnalysisGeneration);
        Enabled = false;
        SetMessage("正在安全停止相机并保存设置……");
        SaveSettingsQuietly();

        try
        {
            if (_camera.IsGrabbing) await _camera.StopGrabbingAsync().ConfigureAwait(true);
            if (_camera.IsOpen) await _camera.CloseAsync().ConfigureAwait(true);
        }
        catch
        {
            // 关闭路径继续释放其余资源。
        }

        Task? liveTask = _liveProcessingTask;
        Task? staticTask = _staticProcessingTask;
        try
        {
            if (liveTask is not null) await liveTask.ConfigureAwait(true);
            if (staticTask is not null) await staticTask.ConfigureAwait(true);
        }
        catch
        {
            // 处理任务异常已由各自 UI 路径报告。
        }

        _camera.FrameReceived -= CameraOnFrameReceived;
        _camera.ErrorOccurred -= CameraOnErrorOccurred;
        try { await _camera.DisposeAsync().ConfigureAwait(true); }
        catch { }
        foreach (AprilTagPoseDetector detector in _detectors.Values) detector.Dispose();
        _currentResult?.Dispose();
        _currentResult = null;
        Image? image = _pictureBox.Image;
        _pictureBox.Image = null;
        image?.Dispose();

        _shutdownComplete = true;
        Enabled = true;
        Close();
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            if (TryParseFiniteDouble(_tagSizeText.Text, out double tagSize) && tagSize > 0)
                _settings.TagSizeMillimeters = tagSize;
            _settings.MarkerDictionary = SelectedDictionary();

            CameraCalibration calibration = ReadCalibrationFromFields();
            _settings.Calibration = !_calibrationIsApproximate && calibration.IsValid
                ? calibration
                : new CameraCalibration();
            _settingsStore.Save(_settings);
        }
        catch
        {
            // 设置写入失败不应中断识别或关闭流程。
        }
    }

    private void RememberExportDirectory(string path)
    {
        _settings.LastExportDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        SaveSettingsQuietly();
    }

    private string? GetPreferredExportDirectory()
    {
        if (Directory.Exists(_settings.LastExportDirectory)) return _settings.LastExportDirectory;
        return GetExistingDirectory(_settings.LastImagePath);
    }

    private static string? GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string? directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
    }

    private string BuildDefaultExportName(string suffix, string extension)
    {
        string source = string.IsNullOrWhiteSpace(_currentSourceName)
            ? SelectedDictionary().CommandLineName()
            : Path.GetFileNameWithoutExtension(_currentSourceName);
        foreach (char invalid in Path.GetInvalidFileNameChars()) source = source.Replace(invalid, '_');
        return $"{source}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
    }

    private void SetMessage(string text) => _messageStatus.Text = text;

    private void ShowOperationError(string title, Exception exception)
    {
        SetMessage($"{title}：{exception.Message}");
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static double ParseFiniteDouble(string text, string name)
    {
        if (!TryParseFiniteDouble(text, out double value))
            throw new InvalidDataException($"{name} 必须是有限数字。");
        return value;
    }

    private static bool TryParseFiniteDouble(string? text, out double value)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value);
    }

    private static int ParseNonNegativeInt(string text, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < 0)
            throw new InvalidDataException($"{name} 必须是大于或等于 0 的整数。");
        return value;
    }

    private static int ParsePositiveInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) && value > 0 ? value : 0;

    private static string FormatEditable(double value) =>
        double.IsFinite(value) ? value.ToString("0.###############", CultureInfo.InvariantCulture) : "0";

    private static string FormatValue(double value, string format = "0.###") =>
        double.IsFinite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : "—";

    private static string FormatVector(double[] values, string format = "0.###") => values.Length >= 3
        ? $"({FormatValue(values[0], format)}, {FormatValue(values[1], format)}, {FormatValue(values[2], format)})"
        : "—";

    private static double VectorElement(double[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : double.NaN;

    private static bool HasPose(TagPoseResult tag) =>
        tag.TranslationMillimeters.Length >= 3 &&
        tag.RotationVector.Length >= 3 &&
        tag.TranslationMillimeters.Take(3).All(double.IsFinite) &&
        tag.RotationVector.Take(3).All(double.IsFinite);

    private MarkerDictionaryKind SelectedDictionary()
    {
        return _dictionaryCombo.SelectedItem is MarkerDictionaryOption option
            ? option.Kind
            : AprilTagPoseDetector.DefaultDictionary;
    }

    private void SelectDictionary(MarkerDictionaryKind dictionary)
    {
        MarkerDictionaryOption? match = _dictionaryCombo.Items
            .OfType<MarkerDictionaryOption>()
            .FirstOrDefault(option => option.Kind == dictionary);
        _dictionaryCombo.SelectedItem = match ?? _dictionaryCombo.Items[0];
    }

    private AprilTagPoseDetector DetectorFor(MarkerDictionaryKind dictionary)
    {
        return _detectors.TryGetValue(dictionary, out AprilTagPoseDetector? detector)
            ? detector
            : throw new InvalidOperationException($"未配置检测字典：{dictionary}。");
    }

    private readonly record struct ProcessingOptions(
        CameraCalibration? Calibration,
        double TagSizeMillimeters,
        MarkerDictionaryKind Dictionary);

    private sealed record MarkerDictionaryOption(MarkerDictionaryKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class DetectionUiPayload : IDisposable
    {
        public DetectionUiPayload(
            DetectionFrameResult result,
            Bitmap displayImage,
            string sourceName,
            uint? frameNumber)
        {
            Result = result;
            DisplayImage = displayImage;
            SourceName = sourceName;
            FrameNumber = frameNumber;
        }

        private DetectionFrameResult? Result { get; set; }
        private Bitmap? DisplayImage { get; set; }
        public string SourceName { get; }
        public uint? FrameNumber { get; }

        public DetectionFrameResult TakeResult()
        {
            DetectionFrameResult result = Result ?? throw new ObjectDisposedException(nameof(DetectionUiPayload));
            Result = null;
            return result;
        }

        public Bitmap TakeDisplayImage()
        {
            Bitmap image = DisplayImage ?? throw new ObjectDisposedException(nameof(DetectionUiPayload));
            DisplayImage = null;
            return image;
        }

        public void Dispose()
        {
            Result?.Dispose();
            Result = null;
            DisplayImage?.Dispose();
            DisplayImage = null;
        }
    }
}
