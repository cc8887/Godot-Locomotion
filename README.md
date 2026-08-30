# Godot ALS

这是一个面向 Godot 4.7.2 .NET 的 ALS 示例项目。当前主场景是
`res://scenes/demo/p4_locomotion_demo.tscn`，已经接入真实 Mannequin、P3
locomotion、AimOffset、上半身分层、Turn/Rotate In Place、Foot IK、Foot Lock、
pelvis correction，以及 Gather/Worker/Commit 多线程动画路径。

P4 功能、focused 短时门禁和 Task 17 clean-worktree 完整自动门禁已经通过，
P4 自动实现闭环已完成。Godot Editor 八项手工观感验收仍未签收，P7 的 10 分钟
Release 性能认证也不属于当前阶段证书，不能用本次自动闭环替代。

## 运行

环境要求：Godot 4.7.2 .NET、.NET 8 SDK、PowerShell 7，以及仓库验证脚本
使用的 Pester 模块（当前证书环境为 Pester 3.4.0）。

在 Godot 中导入仓库根目录的 `project.godot` 后运行主场景，或从 PowerShell
直接启动 Demo：

```powershell
& '<Godot-4.7.2-console.exe>' --path . res://scenes/demo/p4_locomotion_demo.tscn
```

键鼠：

| 输入 | 行为 |
| --- | --- |
| `W` / `A` / `S` / `D` | 相对相机方向移动 |
| `Alt` | Walk |
| `Shift` | Sprint |
| `Ctrl` | 切换站立/蹲伏 |
| `Space` | Jump |
| `V` | 切换 rotation mode |
| 鼠标右键 | Aiming |
| 鼠标移动 | Orbit camera |
| `Esc` | 切换鼠标捕获 |

## 资产边界

UE 5.9 批次锁定了 267 个 ALS 资产、141 个正式文件和 126 个动画，包含
Mannequin、Overlay 与道具模型；导出审计为 0 error / 0 warning。资产 lock
schema 为 1，manifest SHA-256 为
`F12C56C705F05C7C55E77955BD14A3729A2E96D5FAA669FDBD759702B0EA846E`。
音频按既定范围未导出，也不阻塞当前动画示例。

生成资产位于 ignored 的 `assets/generated/als_v4/**`，不应提交导入缓存、
`.godot`、`bin` 或 `obj`。

因此 clean checkout 不能只靠 Git 内容直接运行 Demo。首次准备必须先按
[P2A 全量导出](docs/architecture/p2a-full-ue-export.md)从 UE 5.9 源项目执行
`scripts/verify-p2a.ps1`，再按
[P2B 全量导入](docs/architecture/p2b-godot-import-closure.md)执行
`scripts/verify-p2b.ps1 -CleanImport`。完成后必须存在
`assets/generated/als_v4/compiled/als_animation_set.tres`；已有受审计的同批生成
资产时可复用该交付物，不必把它提交到 Git。

## 验证

日常开发使用的 focused 门禁：

```powershell
pwsh -NoProfile -File scripts/verify-p4.ps1 `
  -GodotExecutable '<Godot-4.7.2-console.exe>' `
  -Focused
```

它包含 P4 focused Import/Core、Pose/Feet、正式 Demo 和正式四格 Matrix，但跳过
全库回归、非 Skip P3B、Release 和仓库 closure。诊断及各子门禁证据可以正常
输出，但唯一顶层/终态成功标记只能是
`P4_FOCUSED_VERIFICATION_OK regression=skipped`，绝不能出现 full marker
`P4_VERIFICATION_OK`，也不能作为完整 P4 完成证书。Task 17 的正式完整命令为：

```powershell
pwsh -NoProfile -File scripts/verify-p4.ps1 `
  -GodotExecutable '<Godot-4.7.2-console.exe>'
```

只有完整命令在 clean worktree 上完成全部 P0-P4、P3B 子链、Release 测试和
locked-base closure，并唯一输出整行 `P4_VERIFICATION_OK`，才构成 P4 自动闭环。
当前 Task 17 已取得该完整证书；复跑仍必须使用同一默认命令，不能用 focused
marker 冒充 full 成功。

## 文档

- [P4 姿态与脚部架构](docs/architecture/p4-pose-and-foot-placement.md)
- [P4 设计规范](docs/superpowers/specs/2026-08-28-p4-aim-layering-foot-placement-design.md)
- [总路线设计](docs/superpowers/specs/2026-08-25-godot-als-port-design.md)
- [P2B 全量资产导入](docs/architecture/p2b-godot-import-closure.md)
- [P3 基础 Locomotion](docs/architecture/p3-basic-locomotion.md)

## 后续阶段

| 阶段 | 状态 | 范围 |
| --- | --- | --- |
| P5A | 待实施 | 通用 Curve/Notify/Notify State、Sync Runtime、ActionPlayer |
| P5B | 待实施 | Overlay gameplay、装备/切换和道具生命周期 |
| P5C | 待实施 | Mantle、Roll、碰撞安全 Root Motion |
| P6 | 待实施 | Ragdoll、Get-up、Pose Recovery、完整 ALS Camera |
| P7 | 待实施 | 30 秒热身、10 分钟 Release 最终性能认证 |
