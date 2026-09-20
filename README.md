# Godot ALS

这是一个面向 Godot 4.7.2 .NET 的 ALS 示例项目。当前主场景是
`res://scenes/demo/als_demo.tscn`，已经接入真实 Mannequin、P3
locomotion、AimOffset、上半身分层、Turn/Rotate In Place、Foot IK、Foot Lock、
pelvis correction，以及 Gather/Worker/Commit 多线程动画路径。

主目录统一为 `.`，后续在这里的 `main` 分支继续开发。
2026-09-20 已合入此前 P5A 工作目录的实现、数据和验证记录，并同步本地生成资产。
普通入口默认启用完整分层、Aim、Refactored 脚部调度及最终接触策略，不再依赖一串诊断参数。
这仍是开发中的 Demo：地形全程接触、起停滑步、换髋和上下身观感、P5A 收尾及后续玩法尚未全部验收。
历史 P4 证书不代表当前完整链路已验收；P7 十分钟性能认证也未完成。

共同 Montage 已发布真实动作摘要，并补齐 Roll 的类型化 GroundedEntry 通知及入口重置。
普通输入现已通过 Motor 接入共同动作所有者，主线程提交后发布动作结果。
主场景支持 R 地面翻滚、X 取消：同帧 Root Motion 经过胶囊碰撞移动，翻滚锁定
触发方向并使用半衰期转向，重复播放/空中触发受门控。详见
[地面 Roll 接线记录](docs/verification/2026-09-20-grounded-roll-gameplay.md)。落地自动 Roll
及翻滚离地转 Ragdoll 尚未实现；`--action-preview` 保留旧原地预览诊断入口。
角色永久销毁/换代现会根据主线程已提交记录结束 Notify State 和动作；包含回调内销毁及
未提交动作隔离。见[退役清理记录](docs/verification/2026-09-20-animation-retirement.md)。
完整入口现已区分调度暂停与真正停用；停用会清理动作，恢复时保留移动/脚部检查点，
不会继续播放旧动作。见[停用恢复记录](docs/verification/2026-09-20-animation-deactivation.md)。
Worker 在 Release 策略下完成回滚后，现可沿用原帧输入重试；成功提交才取消旧动作
和结束 Notify，不重复积分移动。见[故障恢复记录](docs/verification/2026-09-20-animation-failure-recovery.md)。

## 运行

环境要求：Godot 4.7.2 .NET、.NET 8 SDK、PowerShell 7，以及仓库验证脚本
使用的 Pester 模块（当前证书环境为 Pester 3.4.0）。

在 Godot 中导入仓库根目录的 `project.godot` 后运行主场景，或从 PowerShell
直接启动 Demo：

```powershell
Set-Location .
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --path .
```

Editor 使用 F5 运行项目；单独运行旧的 `p4_locomotion_demo.tscn` 是历史诊断入口。
需要复查旧链路时，在普通启动命令末尾加 `-- --legacy-animation`。
可用 `-- --overlay=Rifle` 检查指定 Overlay 姿势及道具，Q / E 切换。

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
| `R` | 地面翻滚，朝触发瞬间的移动方向；无输入时朝角色前方 |
| `X` | 取消当前已接受的动作 |
| `Q` / `E` | 上一个 / 下一个 Overlay，按 UE 原始规则装备或清空道具 |

道具已接入正式动画提交：步枪、单/双手手枪、弓、火炬、望远镜、箱子、桶；
弓随角色曲线拉弓，HUD 显示已提交的 Overlay。此批范围和验证见
[Overlay 道具运行时](docs/verification/2026-09-20-overlay-props.md)。

## 资产边界

UE 5.9 批次锁定了 267 个 ALS 资产、141 个正式文件和 126 个动画，包含
Mannequin、Overlay 与道具模型。最初 P2 批次的审计为 0 error / 0 warning；
后续追加了原始采样和曲线数据，不能沿用旧 manifest 哈希认证当前批次。
本次同步的 manifest SHA-256 为
`5D942E8566C9C8DE0FBBF015E02C6235AC2DEEF51C8C54446A56302A1D35BFD0`。
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

当前普通入口的键鼠/完整姿势回归：

```powershell
& '<Godot-4.7.2-console.exe>' --path . --rendering-method gl_compatibility `
  res://scenes/tests/p4_keyboard_mouse_smoke.tscn
```

覆盖 360 帧 Alt 行走、鼠标转向、左右横移、完整分层、最终脚部身份和锁脚曲线。
末尾添加 `-- --capture-dir=res://artifacts/<新名称>` 可保存逐段截图。
这是平地回归，不替代完整地形及人工输入验收。

动作输入及角色重建回归：

```powershell
& '<Godot-4.7.2-console.exe>' --headless --path . res://scenes/tests/action_input_smoke.tscn -- --hz=60
& '<Godot-4.7.2-console.exe>' --headless --path . res://scenes/tests/action_lifecycle_smoke.tscn
```

前者通过实际 R/X 输入检查替换、取消、自然完成和通知反馈；后者检查角色重建、
提交等待期间的按键锁存，以及回调产生的下一帧请求。动画预览尚不包含 Roll 玩法门控。

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Release
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release
```

旧 P4 蹲姿转身 oracle 和 P5A 冻结计划已使用各自锁定的历史配置回放，
正式 V4 配置保持原生资产的蹲姿转身规则。验证结果及限制见
[冻结回放修复记录](docs/verification/2026-09-20-frozen-reference-replay.md)；
入口与生成资产的整合记录见[主目录整合记录](docs/verification/2026-09-20-main-consolidation-and-demo-entry.md)。
原 `verify-p4.ps1` 保留用于历史阶段复查，其旧成功记录不能作为当前版本的新证书。

## 文档

- [P4 姿态与脚部架构](docs/architecture/p4-pose-and-foot-placement.md)
- [P4 设计规范](docs/superpowers/specs/2026-08-28-p4-aim-layering-foot-placement-design.md)
- [总路线设计](docs/superpowers/specs/2026-08-25-godot-als-port-design.md)
- [P2B 全量资产导入](docs/architecture/p2b-godot-import-closure.md)
- [P3 基础 Locomotion](docs/architecture/p3-basic-locomotion.md)

## 后续阶段

| 阶段 | 状态 | 范围 |
| --- | --- | --- |
| P3/P4 完整性 | 实现与整体验收中 | 默认完整入口已接通；地形、起停滑步、换髋与上下身联合验收未关闭 |
| P5A | 已部分实现，待收尾 | Curve/Notify/Notify State、Sync、共同 Montage、动作摘要及普通请求入口已接通；其余玩法消费者、生命周期通知闭合及当前完整图验收待完成 |
| P5B | 动画数据/姿势已有，玩法待实施 | Overlay 装备/切换和道具生命周期 |
| P5C | Roll 播放组件已有，玩法待实施 | Mantle、Roll、碰撞安全 Root Motion |
| P6 | 基础组件已有，完整功能待实施 | Ragdoll、Get-up、Pose Recovery、完整 ALS Camera |
| P7 | 待实施 | 30 秒热身、10 分钟 Release 最终性能认证 |
