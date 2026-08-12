# 回环测试畸变矫正

反射投影系统的端到端几何预畸变程序，使用海康 GigE 相机、Windows 扩展桌面、Gray Code 稠密映射和可配置点阵回环。

已在 `MV-CS200-10GM / DA2239447` 上实测：Gray Code 覆盖率 81.2%，一次点阵回环 RMS 0.583 camera px；完整 1920×1080 输入的自然逆映射外接框约为 1777×687。

完整原理、实现、GitHub 调研和操作说明见 [技术文档](docs/反射投影回环畸变矫正-原理与实现.md)。

## 运行

```powershell
dotnet build .\DistortionCorrection.sln -c Release -p:Platform=x64 --configfile .\NuGet.Config
.\src\DistortionCorrection.App\bin\x64\Release\net8.0-windows\DistortionCorrection.exe
```

命令行入口：

```powershell
DistortionCorrection.exe --self-test
DistortionCorrection.exe --enumerate-cameras
DistortionCorrection.exe --full-calibration <输出根目录>
DistortionCorrection.exe --resume-loop <已有运行目录>
DistortionCorrection.exe --warp <calibration.json> <输入图片> <输出目录> [固定X 固定Y]
DistortionCorrection.exe --warp-folder <calibration.json> <输入文件夹> <输出文件夹> [固定X 固定Y]
```

图形界面分为相机采集、桌面投图、参数与回环标定、人眼确认、文件夹批量矫正五个 Designer 子界面。点阵列/行数与目标外接宽/高均可填写；每张输出固定为 1920×1080 PNG，逆映射区域外填黑。

相机采集页增加了 OpenCvSharp 镜头畸变矫正。准备至少 5 张（建议 12–20 张）同一分辨率、不同角度和位置的棋盘格照片，填写棋盘格内角点列/行数与方格边长，点击“棋盘格标定并保存”，即可生成镜头标定 JSON 并启用 `InitUndistortRectifyMap + Remap`。启用后单帧、Gray Code 和点阵回环的所有相机帧都会先做镜头矫正；相机像素坐标随之改变，因此必须重新执行完整回环标定，不能续用未启用镜头矫正时生成的旧回环目录。

## 版本管理约定

- `main` 保存可回溯的稳定快照。
- 每完成一个明确阶段就创建一次提交，提交信息说明该阶段的结果。
- `docs/task-history/` 保存任务需求和阶段状态，便于将代码版本与任务背景对应起来。
- 相机临时采集、生成结果、编译输出和本机密钥不进入版本库。

## 常用回溯命令

```powershell
# 查看版本历史
git log --oneline --decorate --graph --all

# 查看某次提交内容（不会修改当前文件）
git show <commit-id>

# 临时查看旧版本
git switch --detach <commit-id>

# 返回主分支
git switch main

# 从旧版本创建恢复分支（推荐，保留现有历史）
git switch -c restore/<name> <commit-id>
```

在确认要永久撤销某次已发布改动时，优先使用 `git revert <commit-id>`，它会新增一个反向提交，不会改写历史。
