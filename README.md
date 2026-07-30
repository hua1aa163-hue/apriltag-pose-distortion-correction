# 回环测试畸变矫正

本仓库用于保存反射投影系统的回环畸变矫正程序、设计文档，以及同一 Codex 工作目录下的任务记录。

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
