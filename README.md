# Godot ALS

当前剩余任务、完整角色动画链路的接入顺序与验收条件统一记录在 [ROADMAP](ROADMAP.md)。历史目录是否归并见 [主目录审计](docs/verification/2026-09-25-main-directory-audit.md)。

这是一个面向 Godot 4.7.2 .NET 的 ALS 示例项目。当前主场景是
`res://scenes/demo/als_demo.tscn`，已经接入真实 Mannequin、P3
locomotion、AimOffset、上半身分层、Turn/Rotate In Place、Foot IK、Foot Lock、
pelvis correction，以及 Gather/Worker/Commit 多线程动画路径。

仓库位置从本地环境配置中的 `GODOT_ALS_ROOT` 读取；后续在 `main` 分支继续开发。
2026-09-20 已合入此前 P5A 工作目录的实现、数据和验证记录，并同步本地生成资产。
普通入口默认启用完整分层、Aim、Refactored 脚部调度及最终接触策略，不再依赖一串诊断参数。
2026-09-26 普通入口已默认使用新 Refactored Standing/Crouching 姿态宿主和共享 Transition Slot，
接入原 Gather/Worker/Commit。Grounded 站蹲外层、空中/落地及上身暂沿用现有图作为桥接，
尚非完整 Refactored 整图替换；范围、三频率与十角色验证见
[新宿主接入记录](docs/verification/2026-09-26-demo-refactored-stances.md)。
这仍是开发中的 Demo：地形全程接触、起停滑步、换髋和上下身观感、P5A 收尾及后续玩法尚未全部验收。
历史 P4 证书不代表当前完整链路已验收；P7 十分钟性能认证也未完成。

共同 Montage 已发布真实动作摘要，并补齐 Roll 的类型化 GroundedEntry 通知及入口重置。
普通输入现已通过 Motor 接入共同动作所有者，主线程提交后发布动作结果。
主场景支持 R 地面翻滚、X 取消：同帧 Root Motion 经过胶囊碰撞移动，翻滚锁定
触发方向并使用半衰期转向，重复播放/空中触发受门控。详见
[地面 Roll 接线记录](docs/verification/2026-09-20-grounded-roll-gameplay.md)。中等落差现支持
1.3 倍速自动 Roll，详见[落地动作记录](docs/verification/2026-09-20-landing-action-routing.md)。
高落差及翻滚离地现已接入自动 Ragdoll，支持 G 手动进入/退出及按 Overlay 选择起身动画；
物理稳定性仍有未通过项。`--action-preview` 保留旧原地预览诊断入口。
两套原始 PhysicsAsset 的形状、质量/惯量、关节和碰撞排除数据现已导出并校验，
见[物理资产记录](docs/verification/2026-09-20-physics-asset-export.md)，现已由 Core 物理运行时消费并接入普通角色。
普通入口现使用移植的 ALS 相机图、骨骼插槽和球扫跟随，详见
[相机接入验证](docs/verification/2026-09-24-camera-demo-integration.md)。完整相机等价及最终性能尚未验收。
角色永久销毁/换代现会根据主线程已提交记录结束 Notify State 和动作；包含回调内销毁及
未提交动作隔离。见[退役清理记录](docs/verification/2026-09-20-animation-retirement.md)。
完整入口现已区分调度暂停与真正停用；停用会清理动作，恢复时保留移动/脚部检查点，
不会继续播放旧动作。见[停用恢复记录](docs/verification/2026-09-20-animation-deactivation.md)。
Worker 在 Release 策略下完成回滚后，现可沿用原帧输入重试；成功提交才取消旧动作
和结束 Notify，不重复积分移动。见[故障恢复记录](docs/verification/2026-09-20-animation-failure-recovery.md)。

## 运行

环境要求：Godot 4.7.2 .NET、.NET 8 SDK、PowerShell 7，以及仓库验证脚本
使用的 Pester 模块（当前证书环境为 Pester 3.4.0）。

本机路径配置使用 `.env.local.ps1`：`GODOT_ALS_ROOT` 自动取仓库位置，需填写
`ALS_UE_PROJECT_ROOT`、`UE_ENGINE_ROOT`、`ALS_REFERENCE_ROOT` 和
`GODOT_EXECUTABLE`。该文件已加入 `.gitignore`；提交前不要使用 `git add -f`
强制跟踪它。

首次 clone 后先用 .NET 8 SDK 构建 `GodotALS.csproj`，再用 Godot 4.7.2 .NET
打开仓库根目录的 `project.godot`。导入插件在构建前也可加载，但执行菜单命令需要
已编译的 C# 程序集。先按下文[首次 clone 的资产准备](#首次-clone-的资产准备)导出并导入资产，
再运行主场景，或从 PowerShell 直接启动 Demo：

```powershell
if (-not (Test-Path .env.local.ps1)) { Copy-Item .env.local.ps1.example .env.local.ps1 }
# 编辑 .env.local.ps1，填写本机 UE、参考仓库和 Godot 可执行文件路径。
. ./.env.local.ps1
Set-Location $env:GODOT_ALS_ROOT
dotnet build GodotALS.csproj -p:Optimize=true
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT
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
| 鼠标移动 | 控制视角，ALS 相机按状态跟随 |
| `B` | 切换第一/第三人称 |
| `T` | 左右换肩 |
| `G` | 进入/退出 Ragdoll |
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

## 首次 clone 的资产准备

Git 忽略整个 `assets/generated/als_v4/`，所以单独 clone 后缺少以下运行所需文件：

| 路径（均在 `assets/generated/als_v4/` 下） | 数量 | 生成阶段 |
| --- | ---: | --- |
| `als_manifest.json` | 1 | P2A 正式清单，记录 267 个资产及文件哈希 |
| `animations/*.fbx` | 126 | P2A 动画序列 |
| `meshes/skeletal/*.fbx` | 7 | P2A 角色和道具骨骼网格 |
| `meshes/static/*.fbx` | 4 | P2A 静态网格 |
| `textures/*.png` | 4 | P2A 纹理 |
| `compiled/als_animation_set.tres` | 1 | P2B 编译后的 Godot 资源，Demo 启动时直接加载 |

141 个 FBX/PNG 的具体文件名、大小和 SHA-256 以生成的 `als_manifest.json` 的
`files[]` 为准；仓库跟踪的 `reference/als-v4-export.lock.json` 锁定正式清单的
SHA-256。`.import`、`.godot/imported` 是 Godot 在导入时生成的缓存，
`export_plan.json`、`audit/`、`partial/` 是导出审计材料，不需要另行下载来运行 Demo。
缺少 `.tres` 时主场景无法启动；缺少或改动清单中的源文件会导致导入校验失败。

准备一份自己有权使用、与本仓库锁定批次一致的 UE 5.9 ALS V4 源工程
（包含 `AdvancedLocomotionSystemV.uproject` 和 `Content/AdvancedLocomotionV4`）；
源工程和导出资产不随本仓库提供。ALS V4 的来源见
[Fab 官方页面](https://www.fab.com/listings/ef9651a4-fb55-4866-a2d9-1b38b028f9c7)。
在 Windows PowerShell 7 中，从本仓库根目录执行：

```powershell
if (-not (Test-Path .env.local.ps1)) { Copy-Item .env.local.ps1.example .env.local.ps1 }
# 编辑 .env.local.ps1：ALS_UE_PROJECT_ROOT 指向源工程目录，
# UE_ENGINE_ROOT 指向 UE 5.9 安装根目录，GODOT_EXECUTABLE 指向 Godot 4.7.2 .NET。
. ./.env.local.ps1
$lockedManifestSha = (Get-Content reference/als-v4-export.lock.json -Raw | ConvertFrom-Json).manifestSha256
.\scripts\verify-p2a.ps1 -EngineRoot $env:UE_ENGINE_ROOT `
  -UnrealProject $env:ALS_UE_PROJECT_FILE -UpdateAssetLock
$exportedManifestSha = (Get-FileHash assets/generated/als_v4/als_manifest.json -Algorithm SHA256).Hash.ToLowerInvariant()
if ($exportedManifestSha -cne $lockedManifestSha) {
  throw '导出批次与仓库锁不一致；请核对 UE 源工程/版本，不要继续导入或提交新锁。'
}
.\scripts\verify-p2b.ps1 -GodotExecutable $env:GODOT_EXECUTABLE -CleanImport
Test-Path assets/generated/als_v4/compiled/als_animation_set.tres
```

P2A 会构建并部署仓库内的 UE 导出插件，完成两次导出、审计和确定性比较；
`-UpdateAssetLock` 才会发布候选输出，同时重写本地的锁文件。哈希检查用于确认
这次导出与仓库锁定批次一致；不一致时不要将本地锁文件提交。P2B 会重新构建
.NET 项目、执行 Godot 清洁导入并验证资产和真实 rig。详细门禁与限制见
[P2A 全量导出](docs/architecture/p2a-full-ue-export.md)和
[P2B 全量导入](docs/architecture/p2b-godot-import-closure.md)。若已有**同一锁定批次**
的合法本地生成文件，可以放在上述目录后直接执行 P2B，无需重复 UE 导出。

仓库只提供导出工具和代码，不公开分发这些源资产：ALS V4 包含 Epic/第三方内容；
[Epic 内容许可](https://www.unrealengine.com/eula/content)限制向第三方分发源格式内容，
[Fab 标准许可](https://www.fab.com/eula)允许在适用条款下与项目协作者私下共享，
但不允许把资产作为独立资源公开再分发。仓库自己的
[资产许可说明](ASSET_LICENSE.md)也不能替原权利人授予分发权。

## 验证

当前普通入口的键鼠/完整姿势回归：

```powershell
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT --rendering-method gl_compatibility `
  res://scenes/tests/p4_keyboard_mouse_smoke.tscn
```

覆盖 360 帧 Alt 行走、鼠标转向、左右横移、完整分层、最终脚部身份和锁脚曲线。
末尾添加 `-- --capture-dir=res://artifacts/<新名称>` 可保存逐段截图。
这是平地回归，不替代完整地形及人工输入验收。

动作输入及角色重建回归：

```powershell
& $env:GODOT_EXECUTABLE --headless --path $env:GODOT_ALS_ROOT res://scenes/tests/action_input_smoke.tscn -- --hz=60
& $env:GODOT_EXECUTABLE --headless --path $env:GODOT_ALS_ROOT res://scenes/tests/action_lifecycle_smoke.tscn
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

## 许可

本项目原创代码及相关文档采用 [MIT 许可](LICENSE.md)，可用于商业项目；
第三方代码仍按其原始许可使用。MIT 不覆盖从 ALS、UE 或其他来源导出的动作、模型、骨骼、
曲线等资源及其转换版本，即使这些数据以 JSON、Godot 资源或测试夹具形式保存。
本项目不授予这些导出资源商业使用权；本项目完全拥有版权的原创资源仅授权
非商业使用，具体见[资源许可与来源边界](ASSET_LICENSE.md)。

第三方资源须另行遵守来源许可；仅从本仓库取得资源，不代表获得非商业使用、
再分发或商业使用授权。独立从原权利人获得的权利仍以其原许可为准，详见
[第三方声明](THIRD_PARTY_NOTICES.md)。商业项目可以单独使用 MIT 代码，
但必须为所用资源另行取得相应权限。

## 后续阶段

| 阶段 | 状态 | 范围 |
| --- | --- | --- |
| P3/P4 完整性 | 实现与整体验收中 | 默认完整入口已接通；地形、起停滑步、换髋与上下身联合验收未关闭 |
| P5A | 已部分实现，待收尾 | Curve/Notify/Notify State、Sync、共同 Montage、动作摘要及普通请求入口已接通；其余玩法消费者、生命周期通知闭合及当前完整图验收待完成 |
| P5B | 普通装备/切换已接入 | Overlay 和道具生命周期；道具物理按用户要求暂缓 |
| P5C | Roll/Root Motion 已接入，Mantle 未实现 | 继续 Mantle 及整体验收 |
| P6 | Ragdoll/起身恢复及 ALS 相机已接入，尚未完整验收 | 物理稳定性、相机场景/原生组件对照和联合验收 |
| P7 | 待实施 | 30 秒热身、10 分钟 Release 最终性能认证 |
