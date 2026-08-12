# ArUco / AprilTag 位姿识别器

`AprilTagPose.exe` 是一个 x64 Windows 桌面程序，使用海康 MVS V2 SDK 采集图像，并通过 OpenCvSharp/OpenCV 识别 ArUco `DICT_4X4_250` 或 AprilTag `tag16h5`。界面默认选择 `DICT_4X4_250`，与用户当前纸码一致；两种字典显式切换，不会把同一候选跨字典重复匹配。程序支持海康 GigE、USB3 和 GenTL 相机，也可以直接分析 BMP、PNG、JPG、TIFF 等本地图像。

## 主要功能

- 海康相机枚举、连接、连续取流、断线/SDK 错误提示
- 可选择 `DICT_4X4_250` 或 `tag16h5`，输出标记 ID、四角和中心像素坐标
- 输出 `rvec`、`tvec`、旋转矩阵、四元数、ZYX 欧拉角、直线距离和重投影 RMS
- 图像上叠加标签边框、中心和 XYZ 坐标轴
- 相机内参、畸变系数和标签尺寸可编辑、加载、保存
- 检测结果可导出为 JSON、CSV 和叠加图
- 没有标定参数时可使用近似焦距预览；界面和结果会明确标记 `APPROX`

## 运行环境

1. 64 位 Windows。
2. 海康 MVS Runtime 4.8 或与相机匹配的 MVS Runtime。本机使用的托管 SDK 是 `D:\MVS\MVS\Development\DotNet\win64\netstandard2.0\MvCameraControl.Net.dll`。
3. .NET 8 Desktop Runtime x64。本机已安装 8.0.28。

发布目录中的文件必须一起保留，不能只复制 EXE。OpenCvSharp 的原生 DLL 和海康托管 DLL 都是运行依赖；海康原生 Runtime 由 MVS 安装程序提供。

## 使用步骤

1. 启动 `AprilTagPose.exe`。程序默认尝试加载上次图片；首次运行会加载用户提供的试验图。
2. 在“识别字典”中选择码族。当前现场纸码请选择 `ArUco DICT_4X4_250`；标准 AprilTag 16h5 请选择 `AprilTag tag16h5`。
3. 填写标签边长。这里指方形标记**外侧黑色正方形**的实际边长，单位 mm；平移向量会使用同一单位。
4. 加载相机标定 JSON，或在界面中填写 `fx/fy/cx/cy` 和 `k1/k2/p1/p2/k3`。标定分辨率必须与采集分辨率及 ROI 相同；不匹配时程序会拒绝计算位姿并提示重新标定，避免把 ROI 裁剪误当成整图缩放。
5. 点击“刷新相机”，选择设备并连接，再开始实时识别；也可以点击“打开图片”离线检测。
6. 在结果表格中选择标签查看完整位姿、四角、旋转向量和四元数。
7. 使用导出按钮保存 JSON、CSV 或叠加图。导出结果会记录实际使用的字典。

没有真实内参时，程序用图像长边作为 `fx=fy`，主点取图像中心。这只能用于确认检测流程和获得粗略位姿，不能用于毫米级测量。

## 坐标约定

- OpenCV 相机坐标：`X` 向图像右，`Y` 向图像下，`Z` 从相机指向场景。
- 标签原点在黑色正方形中心；标签 `X` 向右、`Y` 向上，`Z` 按右手系指向标签正面。
- `tvec` 表示标签原点在相机坐标中的位置，单位与填写的标签边长相同。
- 欧拉角由标签到相机的旋转矩阵按 ZYX 顺序分解。正对相机时，由于标签 Y 轴向上而相机 Y 轴向下，Roll 接近 ±180° 是正常的。
- 程序同时计算 `IPPE_SQUARE` 与迭代 PnP，过滤负深度解并采用重投影误差较小的有效解，以避开正视平面时 IPPE 的退化结果。

## 用户试验图的验证结论

试验图 `C:\Users\admin\MVS\Data\Image_20260810145722324.bmp` 为 5472×3648 的 8 位灰度图。全字典诊断确认现场纸码属于 ArUco `DICT_4X4_250`，而不是 AprilTag `tag16h5`。现场帧可可靠识别 ID `89`、`183`、`225`；较早的试验图中可见的标记也能按该字典解码。

正式位姿验证中，ID `89`、`183`、`225` 的重投影 RMS 分别约为 `0.44`、`0.26`、`0.61 px`，均可正常输出位姿。实时帧左下金属支架曾误匹配为 ID `37`，但重投影 RMS 约 `19.65 px`；程序会用 `5 px` 安全阈值拒绝该位姿并且不绘制坐标轴。手提箱正面的打印码与最近的 ID `168` 相差 2 bit，超过该字典最多 1 bit 的纠错能力，需要重新打印后才能识别。

发布目录附带 `aruco-4x4-250-id7-test.png` 和 `tag16h5-id7-test.png`。程序内置 `--self-test` 会根据 `--dictionary` 生成相应的 ID 7 标记，验证检测和位姿链路。打印标记时应完整保留黑色边框和足够白色留边，并测量外侧黑框边长后填入程序。

## 命令行验证

命令行模式通过文件返回结果，适合自动化测试：

```powershell
AprilTagPose.exe --self-test `
  --dictionary 4x4_250 `
  --generated-input aruco-4x4-250-id7.png `
  --overlay self-test.png --json self-test.json --error error.txt

AprilTagPose.exe --analyze input.bmp `
  --dictionary 4x4_250 `
  --tag-size-mm 50 `
  --calibration camera-calibration.json `
  --overlay detected.png `
  --json detected.json `
  --error error.txt
```

`--dictionary` 可用值为 `4x4_250` 和 `tag16h5`；省略时默认使用 `4x4_250`。

退出码：`0` 成功，`1` 运行异常，`2` 自检没有识别到预期标签，`3` 自检位姿无效。

## 构建

项目固定为 `net8.0-windows / x64`，使用本机离线 NuGet 缓存：

```powershell
dotnet build .\AprilTagPose.sln `
  -c Release -p:Platform=x64 --configfile .\NuGet.Config

dotnet publish .\src\AprilTagPose.App\AprilTagPose.App.csproj `
  -c Release -r win-x64 --self-contained false --no-restore `
  -o .\release\AprilTagPose
```

由于本机离线缓存没有 .NET 8 自包含 Runtime Pack，交付采用框架依赖发布；它仍然生成可直接双击的 `AprilTagPose.exe`。
