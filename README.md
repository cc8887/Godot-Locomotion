# Godot ALS

基于 Godot 4.7.2 .NET 的 ALS V4 角色移动与动画示例。主场景为 `res://scenes/demo/als_demo.tscn`，包含 Mannequin、动作、Overlay、脚部处理和相机。项目仍在开发中，当前进度见文末及 [ROADMAP](ROADMAP.md)。

## 运行

需要 Windows、Godot 4.7.2 .NET、.NET 8 SDK 和 PowerShell 7。**首次 clone 后，必须先按[资产准备](#资产准备)从有权使用的 UE 5.9 源工程导出并导入资产；Git 仓库本身不能直接运行 Demo。**

在仓库根目录创建本机配置，填写 `ALS_UE_PROJECT_ROOT`、`UE_ENGINE_ROOT` 和 `GODOT_EXECUTABLE`。`GODOT_ALS_ROOT` 与 `ALS_UE_PROJECT_FILE` 会由配置自动计算；`ALS_REFERENCE_ROOT` 仅用于相关原生对照验证。`.env.local.ps1` 已被 Git 忽略。

```powershell
if (-not (Test-Path .env.local.ps1)) { Copy-Item .env.local.ps1.example .env.local.ps1 }
# 编辑 .env.local.ps1 后加载配置
. ./.env.local.ps1
```

完成下文的资产准备，确认 `assets/generated/als_v4/compiled/als_animation_set.tres` 存在后，再构建并启动：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT
```

也可以用 Godot 打开根目录的 `project.godot`，按 F5 运行。C# 程序集需先构建，导入插件菜单才能执行。`p4_locomotion_demo.tscn` 是旧诊断场景。

## 资产准备

`assets/generated/als_v4/` 被 Git 忽略。首次 clone 缺少以下文件（路径均相对于该目录）：

| 文件 | 数量 | 来源 |
| --- | ---: | --- |
| `als_manifest.json` | 1 | P2A 导出清单 |
| `animations/*.fbx` | 126 | P2A 动画 |
| `meshes/skeletal/*.fbx` | 7 | P2A 骨骼网格 |
| `meshes/static/*.fbx` | 4 | P2A 静态网格 |
| `textures/*.png` | 4 | P2A 纹理 |
| `compiled/als_animation_set.tres` | 1 | P2B 编译资源，Demo 启动时加载 |

清单的 `files[]` 记录 141 个 FBX/PNG 的路径、大小和 SHA-256；仓库中的 `reference/als-v4-export.lock.json` 锁定该批清单。`.import` 与 `.godot/imported` 由 Godot 生成，不需要下载。若已合法持有与仓库锁定批次相同的本地导出文件，可放入上述目录，跳过 P2A，直接运行 P2B。

自行准备包含 `AdvancedLocomotionSystemV.uproject`、`Content/AdvancedLocomotionV4` 和 `Plugins/ALS/ALS.uplugin` 的 UE 5.9 ALS V4 源工程；[ALS V4 的 Fab 页面](https://www.fab.com/listings/ef9651a4-fb55-4866-a2d9-1b38b028f9c7)可供确认内容来源。加载 `.env.local.ps1` 后，在仓库根目录运行：

```powershell
$lockedManifestSha = (Get-Content reference/als-v4-export.lock.json -Raw | ConvertFrom-Json).manifestSha256
.\scripts\verify-p2a.ps1 -EngineRoot $env:UE_ENGINE_ROOT `
  -UnrealProject $env:ALS_UE_PROJECT_FILE -UpdateAssetLock
$exportedManifestSha = (Get-FileHash assets/generated/als_v4/als_manifest.json -Algorithm SHA256).Hash.ToLowerInvariant()
if ($exportedManifestSha -cne $lockedManifestSha) {
  throw '导出批次与仓库锁不一致；请核对 UE 源工程/版本，不要继续导入或提交新锁。'
}
.\scripts\verify-p2b.ps1 -GodotExecutable $env:GODOT_EXECUTABLE -CleanImport
if (-not (Test-Path assets/generated/als_v4/compiled/als_animation_set.tres)) {
  throw 'P2B 未生成 Demo 所需的动画资源。'
}
```

P2A 构建并部署 UE 导出插件，完成两次导出和确定性校验；`-UpdateAssetLock` 会发布输出并更新本地锁文件。P2B 构建 .NET 项目，执行 Godot 清洁导入及资产验证。若导出哈希不同，不要提交更新后的锁文件。详细流程见 [P2A 导出](docs/architecture/p2a-full-ue-export.md)和 [P2B 导入](docs/architecture/p2b-godot-import-closure.md)。

## 操作

| 输入 | 行为 |
| --- | --- |
| `W` / `A` / `S` / `D` | 移动 |
| `Alt` / `Shift` | 行走 / 冲刺 |
| `Ctrl` / `Space` | 切换蹲伏 / 跳跃 |
| `V` / 鼠标右键 | 切换旋转模式 / 瞄准 |
| 鼠标移动 | 控制视角 |
| `B` / `T` | 切换第一、第三人称 / 左右换肩 |
| `R` / `X` | 翻滚 / 取消当前动作 |
| `G` | 进入或退出 Ragdoll |
| `Q` / `E` | 切换 Overlay 与道具 |
| `Esc` | 切换鼠标捕获 |

## 验证

资产导入成功后，可运行键鼠与动作输入冒烟测试，以及 .NET 测试：

```powershell
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT --rendering-method gl_compatibility `
  res://scenes/tests/p4_keyboard_mouse_smoke.tscn
& $env:GODOT_EXECUTABLE --headless --path $env:GODOT_ALS_ROOT `
  res://scenes/tests/action_input_smoke.tscn -- --hz=60
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Release
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release
```

完整的阶段验收与历史限制见 [ROADMAP](ROADMAP.md) 和 `docs/verification/`。

## 许可

原创代码及相关文档采用 [MIT 许可](LICENSE.md)。ALS V4、Epic/Fab 等第三方资产及其导出或转换文件不属于 MIT 许可范围，适用各自的来源条款。

[Epic 内容许可](https://www.unrealengine.com/eula/content)限制源格式内容的对外分发；[Fab 标准许可](https://www.fab.com/eula)允许按条款与项目协作者私下共享，但不允许单独公开再分发资产。因此本仓库提供导出工具，不上传这批源资产。具体边界见 [资产许可](ASSET_LICENSE.md) 和 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 开发状态

普通 Demo 已接入站立与蹲伏移动、Overlay、Roll、Mantle、Ragdoll/起身和 ALS 相机。后续重点是完整动画链路与复杂地形的对照验收、物理稳定性和长时间性能测试；当前范围与优先级以 [ROADMAP](ROADMAP.md) 为准。
