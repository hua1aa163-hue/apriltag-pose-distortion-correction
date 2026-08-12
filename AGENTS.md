# Codex 协作说明

## 项目入口

- AprilTag 位姿识别：`AprilTagPose.sln`
- 回环畸变矫正：`DistortionCorrection.sln`
- 构建：`dotnet build AprilTagPose.sln -c Release` 与 `dotnet build DistortionCorrection.sln -c Release`
- 示例输入位于 `test-data/`；正式验证输出不得提交到仓库。

## 修改约定

- 保留现有 Git 历史；`main` 保持可构建，日常修改默认使用 `codex/<任务名>` 分支。
- 提交前至少构建受影响的解决方案；算法改动还应使用 `test-data` 做可重复验证。
- 不提交 `release`、`test-results`、`artifacts`、渲染缓存、构建物、本机设置、密钥或相机采集结果。
- 海康相机 SDK 引用当前依赖本机安装路径；更换环境时先检查项目文件中的 SDK 引用。
- 算法、标定格式或硬件流程变化时，同步更新 README、`PROJECT_CONTEXT.md` 和相关技术文档。
- 不用生成文件覆盖已有源码或手工维护的文档；交付时区分离线测试与真实相机/投影验证。
