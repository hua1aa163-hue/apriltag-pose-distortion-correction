# AprilTag 位姿识别项目交接上下文

更新时间：2026-08-12（Asia/Shanghai）

## 项目定位

这是一个 Windows x64 WinForms 应用，使用海康 MVS V2 SDK 获取工业相机图像，并通过 OpenCvSharp/OpenCV 识别方形视觉标记及计算单标签位姿。

- 新项目根目录：`D:\CHATGPT_file\AprilTag位姿识别`
- 主解决方案：`AprilTagPose.sln`
- 应用源码：`src\AprilTagPose.App`
- 当前发布 EXE：`release\AprilTagPose\AprilTagPose.exe`
- 版本发布目录：`release\AprilTagPose-v1.1.0`
- 完整发布 ZIP：`release\AprilTagPose-v1.1.0-win-x64.zip`
- 使用说明：`README-AprilTagPose.md`

## 当前功能状态

程序支持两个可显式选择的字典：

1. `ArUco DICT_4X4_250`，ID 0–249；界面默认选择此字典。
2. `AprilTag tag16h5`，ID 0–29。

当前版本不是同一帧自动双扫；用户需要在右侧“识别字典”下拉框中选择一种。显式选择可避免两个 4×4 字典对同一候选产生重复或跨字典误配。

输出包括：ID、四角/中心像素坐标、rvec、tvec、旋转矩阵、四元数、ZYX 欧拉角、距离、重投影 RMS，并支持 JSON/CSV/叠加图导出。

## 海康相机与依赖

- 已验证相机序列号：`DA2239447`
- 实际采集：5472×3648、Mono8、Offset=0、未镜像
- 海康托管 SDK：`D:\MVS\MVS\Development\DotNet\win64\netstandard2.0\MvCameraControl.Net.dll`
- SDK / Runtime 版本：4.8.0.3 x64
- OpenCvSharp：4.13.0.20260627
- 目标框架：`net8.0-windows`、x64、win-x64
- 目标机需要安装匹配的海康 MVS Runtime/驱动和 .NET 8 Desktop Runtime x64。

## 关键实现决定

- `DICT_4X4_250` 现场纸码需要 `ErrorCorrectionRate=1.0`；原 0.6 无法识别。
- 为抑制背景伪匹配，位姿只接受重投影 RMS `<= 5 px`。
- 现场帧可靠 ID：89、183、225，RMS 约为 0.44、0.26、0.61 px。
- 背景金属支架曾误解码为 ID 37，RMS 约 19.65 px，现仅显示 `pose rejected`，不输出位姿、不绘制坐标轴。
- 手提箱正面的旧打印码不是有效 `DICT_4X4_250`，最近 ID 168、Hamming 距离 2，超过 1 bit 纠错能力，需重新打印。
- 当前用户设置中的 `fx=fy=5472`、零畸变是近似内参；可以显示粗略位姿，但精确测量必须使用真实相机标定。

## 已完成验证

- Release 构建：0 警告、0 错误。
- `--self-test --dictionary 4x4_250`：通过。
- `--self-test --dictionary tag16h5`：通过。
- DICT_4X4_250 三组合成真值测试：全部通过；最大平移误差约 0.432 mm，最大旋转误差约 0.115°。
- 用户现场图：ID 89、183、225 均输出有效位姿。
- WinForms 离屏冒烟测试：字典下拉框和结果表显示正常。
- 发布包 v1.1.0 已通过两种字典自检和现场图检测。

## tag16h5 Word 码册验证

文件：`AprilTag_tag16h5_全部30个码.docx`

- 共 30 张 1200×1200 PNG，依次对应 ID 0–29。
- 当前正式程序按 `tag16h5` 逐张检测：30/30 正确，无缺失、重复或错码，30/30 位姿有效。
- DOCX 结构：5 页，每页 6 码，图片显示尺寸 66.67 mm，无裁剪。
- 黑色码外框标称 50 mm；整图 66.67 mm 包含白色静区。
- 使用时必须选择 `AprilTag tag16h5`；打印选“100%/实际大小”，标签边长填黑外框实测值 50 mm。若打印缩放，应重新实测并填写，否则距离尺度会产生系统误差。

## 常用命令

```powershell
dotnet build .\AprilTagPose.sln `
  -c Release -p:Platform=x64 --configfile .\NuGet.Config

dotnet publish .\src\AprilTagPose.App\AprilTagPose.App.csproj `
  -c Release -r win-x64 -p:Platform=x64 `
  --self-contained false --no-restore `
  -p:PublishTrimmed=false -p:PublishSingleFile=false `
  -o .\release\AprilTagPose

.\release\AprilTagPose\AprilTagPose.exe --self-test `
  --dictionary 4x4_250 --json aruco-self-test.json

.\release\AprilTagPose\AprilTagPose.exe --self-test `
  --dictionary tag16h5 --json tag16h5-self-test.json
```

## 重要文件

- `src\AprilTagPose.App\Services\AprilTagPoseDetector.cs`：字典、检测参数、PnP 和 5 px 过滤。
- `src\AprilTagPose.App\MainForm.cs`：相机、实时处理、字典下拉框、结果 UI。
- `src\AprilTagPose.App\Services\DetectionExporter.cs`：JSON/CSV。
- `src\AprilTagPose.App\Services\CommandLineRunner.cs`：命令行分析与双字典自检。
- `src\AprilTagPose.App\Camera\HikCameraService.cs`：海康 SDK 封装。
- `test-results\pose-validation`：数值验证。
- `test-results\docx-tag16h5-validation`：Word 中 30 个 tag16h5 的验证汇总。

## 迁移记录

2026-08-12 从 `D:\CHATGPT_file\畸变矫正` 完整复制到当前目录。复制校验：源/目标均为 1217 个文件、1,631,084,275 字节；解决方案、源码、DOCX、EXE、DLL 和发布 ZIP 的 SHA-256 均一致。

新目录已完成 Release 构建及两种字典自检，并已注册为 Codex 左侧项目；交接任务也已创建、置顶并成功读取本文件。旧目录中的 1217 个项目文件已于 2026-08-12 清除。由于发起迁移的旧 Codex 任务仍持有其工作目录句柄，Windows 暂时只保留一个无文件、无子目录的空壳目录；已设置有限重试清理，在旧任务释放句柄后自动删除该空目录。
