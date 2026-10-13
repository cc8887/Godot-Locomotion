# Mantle Root、显示模型接触与落点交接修复

本轮修复普通 ALS Demo 中 Root 开启时绕过手锁和身体避障、保留体型的女模型支撑手悬空，以及成功结束后沿用攀爬速度继续前滑的问题。使用当前主目录、既有动作和原角色事务；资产、模型、默认指针及验收阈值保持。完整 Mantle／整包生产验收仍开放。

证据目录：`artifacts/tests/mantle-contact-repair-20261012-v1/`。活动包为 `als_nonprop_crouch_slip_20261011_v2`，161 个替换身份、1,768 个登记载荷，仍缺 22 个非道具身份，`productionReady=false`。包 JSON SHA256 为 `e25c96e7433315a4acb7c458addcd8d04cee0800239f14a4d783820796b4f0af`。默认显示模型仍为 `girl_high_als_retarget.tscn`。

## 根因与改动

1. `MantleProgressControl` 原来由 `!SampleRootMotion` 决定，Root 开启同时禁用了手锁。普通 Demo 现在在 Root 开／关下消费同一份前移、抬升和固定手部合同；未配置 Demo 的原生诊断与显式负对照保留原入口。
2. High 的原生 Root 路径绕过交付的避障前移。Core 保留原生去 Root 锚点，并额外保存明确的角色起点／目标；Root 开启的控制平移使用这些端点，按同一 montage 时间采样进度。相对平台使用当前平台变换运输，不重复消耗 Root 位移。曾尝试用 `SampleLast()` 重建端点，发现 sequence rate 与 montage 结束采样不同，已改为显式端点；实验记录保留。
3. 原女模型映射只匹配脚端，驱动 wrist 贴沿不能证明显示模型贴沿。条件手臂 IK 在 Core 使用目标真实骨长和同帧接触权重；当较高肩点／较短手臂无法够到时，在参考比例差限定的预算内平移共同身体根，再匹配脚端和手端。没有骨长伸缩，也没有第二套时钟或提交链。实际不可达目标保留残差。
4. 正常显示写入消费候选接触，失败回滚消费已提交接触。新增真实故障场景在支撑中注入 `BeforePublish` 故障，检查完整显示骨架及身体修正精确恢复，并检查重试后的支撑释放。
5. High 在结束前一帧的水平进度约为 `0.968774`。Montage 结束后 Motor 直接退出自定义移动，普通积分继续使用退出前的速度，静态 1.8 m 台上最终 Z 为 `-0.870063`，目标为 `-0.75`。成功完成现在运输同一平台相对终点，并把清除后的速度交给本帧既有普通移动积分；中断、取消及原生无进度路径保留原行为。

稳定合同见[攀爬控制](../architecture/als-mantle-contact-control.md)及[角色模型绑定](../architecture/als-character-model-binding.md)。诊断现在观察 `PresentationSkeleton`、实际蒙皮、驱动骨架对照、模型可达残差／身体预算、Ragdoll 阶段和完成目标。

## 修复前与受控定位

| 60 Hz 案例 | 完整蒙皮峰值 | 支撑手误差或其他发现 | 证据 |
| --- | ---: | --- | --- |
| 初始 Mannequin Low，Root 关闭 | 1.47210 cm | 固定手腕正常 | `baseline-mannequin-off.json` |
| 初始 Mannequin Low，Root 开启 | 4.84294 cm | 接触控制帧为 0 | `baseline-mannequin-on.json` |
| 开启手锁，但 High 仍走旧 Root 平移 | 10.08569 cm | 手腕固定，身体／腿仍穿台 | `contact-mannequin-high-on-v1.json` |
| 修正观察对象后的 Girl Low | 约 0.000054 cm | 实际手腕离锚点 29.27463 cm | `contact-girl-low-off.json` |
| 仅增加女模型手臂 IK | 约 0.000054 cm | 仍有 22.83763 cm 的不可达误差 | `model-girl-low-off-v2.json` |

上述实验通过不等于最终代码通过。`before/` 保存任务开始时的已有脏文件版本及后续诊断版本；没有用旧 worktree 或旧资产覆盖当前交付。

## 验收与最终矩阵

原门禁：完整蒙皮穿入 ≤2 cm，完整支撑 wrist 误差 ≤1 mm、palm 误差 ≤0.1°、可达残差 ≤1 mm，移动平台局部漂移 ≤2 mm。增加普通动画阶段手臂骨长变化 ≤0.01 mm、模型身体修正不超过其参考预算，以及成功完成帧的水平落点误差 ≤1 mm（移动平台沿用 2 mm 的一步观察余量）。所有案例仍检查原场景结束／落台／中断断言和已提交／显示身份。

最终冻结输入的真实 Godot 正向矩阵为 **88/88**，共 33,120 个观察帧；另有 **4/4** 故障回滚和 **2/2** 预期拒绝的关闭手锁负对照。汇总见 [`final-summary.json`](../../artifacts/tests/mantle-contact-repair-20261012-v1/final-summary.json)，完整 Mantle 与整包 `productionReady` 仍为 false。

| 最终运行目录 | 范围 | 结果 |
| --- | --- | ---: |
| `final-standard-matrix-v3` | Low 1 m／High 1.8 m，Mannequin／Girl，Root 开／关，30／60／120 Hz，并行 | 24/24 |
| `final-height-platform-matrix-v3` | 0.75／1.2／1.25／1.5／2 m、移动 Low／High、支撑中取消及销毁，两个模型及 Root 开／关，60 Hz | 36/36 |
| `final-single-matrix` | Low／High／移动 Low／取消，两个模型及 Root 开／关，60 Hz 单线程 | 16/16 |
| `final-injured-support-matrix` | Injured 的另一侧 Low 支撑及移动 Low，两个模型及 Root 开／关，60 Hz | 8/8 |
| `final-rendered-review` | Low／High，两个模型，Root 开启，真实 OpenGL 渲染；240 张 GPU 截图 | 4/4 |
| `final-contact-rollback` | Girl 完整支撑且身体修正非零时注入 BeforePublish 故障，Root 开／关及单／并行 | 4/4 |

所有最终目录的配置、相关源码、程序集及登记载荷在各自运行期间保持；没有沿用较早版本的通过证书。回滚测试恢复 68 根显示骨骼、接触诊断和 1.97391 cm 身体修正，保持已提交身份，重试后清除支撑和修正。

正向矩阵完整蒙皮峰值 1.60751 cm，Girl 峰值 0.44982 cm；最大支撑手误差 0.001368 mm，掌面 0.0000552°，平台局部漂移 0.13539 mm，普通动画手臂骨长变化 0.001147 mm。全部成功完成帧的水平落点误差为零，静态 High 的最终 Z 为 `-0.7499999`。模型身体修正最大 24.11343 cm，仍小于由实际参考差异计算的 52.17420 cm 预算。

[`frame-parity-final.json`](../../artifacts/tests/mantle-contact-repair-20261012-v1/frame-parity-final.json) 保存逐帧完整 JSON 比较：Root 开／关 34 对、12,960 帧；单／并行 16 对、5,760 帧；headless／渲染 4 对、1,440 帧，均完全一致。实际查看了[最终 GPU 关键帧拼图](../../artifacts/tests/mantle-contact-repair-20261012-v1/contact-review-sheet-final.png)的 24 个样本，观察抓沿、支撑、提腿、释放与台上恢复；这只是有限抽查，不能覆盖完整动作风格或连续释放平滑度。

Release Core 69/69，包含 Root 开／关端点、不同进入点／rate／相对平台、条件接触、不可达拒绝、释放无历史、错误权重原子拒绝及零分配；日志 `core-tests-v5.log`。Release Import motion/root/contact/montage 专项 23/23，包括原生组件对照；日志 `import-tests-v3.log`。较广 Mantling Import 为 97/102，5 项原有 HostPose DefinitionDigest 绑定失败，日志 `import-tests-v2.log`；保留错误及断言，没有重写导出以使其通过。

早期完成接口的 Optimize 构建遇到工作区同期新增的 `KimodoJumpDiagnostic.cs`：先引用不存在的包路径属性，随后 `Environment` 与 Godot 同名类型冲突。前者已由同期修改解决，本轮只将后者限定为 `System.Environment`；`AlsAnimationResourcePack.cs` 没有最终改动。两次失败日志保留，`finish-integration-build-v3.log` 最终为零警告／零错误。

`final-standard-matrix`／`final-height-platform-matrix` 的场景断言全部通过，但共享 `GodotALS.dll` 在运行期间被另一任务重新编译，总体完整性门禁拒绝；各自 v2 又因共享运行源码和程序集改变被拒绝。四份失败报告保持，不改写为通过。用户授权协调后，另一任务暂缓共享源码写入和编译，最终 v3 及后续矩阵才取得完整性通过；验证结束已发送恢复通知，独立资产工作始终可继续。

## 保留的失败与覆盖边界

- `height-platform-matrix` 的早期全程骨长检查有两个 Mannequin 销毁场景失败。攀爬阶段骨长变化约 0.00055 mm，Ragdoll 物理发布阶段手臂长度变化达 8.95984 mm；原接触、蒙皮和中断断言均通过。新诊断显式记录 Ragdoll，骨长门禁仅认证普通动画／IK 阶段，物理骨长偏差仍记录并保留失败报告。`destroy-retest` 四组真实复验通过，不能据此宣称 Ragdoll 解剖质量已通过。
- 关闭手锁的两组最终负对照为 4.71714 cm，原 2 cm 蒙皮门禁明确拒绝。见 `final-negative-hand-lock/results.json`；runner 中的 `expectedSkinRejection=true` 表示负对照验证成立，`skinPassed=false` 仍保留。
- 原版 Low、Mannequin、Root 开／关均为 2.31517 cm，最终复验仍失败；峰值 tick 51、montage 0.3333334 s、`ring_03_l`，接触权重近零。它是抓沿前自由指节的独立几何问题，手腕及最终落点门禁通过不能覆盖此失败。见 `final-original-reference/results.json` 与逐帧轨迹；不能将候选通过写成原版对照或完整 Pipeline 通过。
- 女模型参考比例差得出的身体修正预算约 52.17420 cm，当前 Default／Injured 案例最大约 24.11343 cm。Low 60 Hz 释放时单步身体修正变化最大 11.59759 cm；这与当前一帧接近／释放曲线有关。关键截图只作有限渲染抽查，身体释放平滑度／动作风格仍需人工验收。
- 未执行完整 Overlay／斜面／薄边缘／装备矩阵、UE 整角色连续轨迹对照、十分钟性能、打包或完整制作 Pipeline。本轮没有生成或替换 FBX，不改变源质量、完整资产闭包和正式启用资格。R6 全部验收保持开放。

## 复现

在主目录使用本机 .NET 8 和 Godot 4.7.2 Mono。输出目录必须新建，避免覆盖旧证据。完整生成资源时仍执行既有完整 Pipeline；以下命令仅复验本轮运行修复。

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build GodotALS.csproj -p:Optimize=true

$mantlePython = 'F:/Github/Kimodo-Pipeline/.venv-win/Scripts/python.exe'
& $mantlePython scripts/verify-mantle-contact-models.py `
  --pack res://assets/generated/als_nonprop_crouch_slip_20261011_v2/resource-pack.json `
  --output artifacts/tests/mantle-new-standard `
  --cases low high --models mannequin girl --roots off on --hz 30 60 120

& $mantlePython scripts/verify-mantle-contact-models.py `
  --pack res://assets/generated/als_nonprop_crouch_slip_20261011_v2/resource-pack.json `
  --output artifacts/tests/mantle-new-platforms `
  --cases short-low medium-low threshold-high medium-high nominal-high moving-low moving-high cancel destroy `
  --models mannequin girl --roots off on --hz 60

# --modes single 复验单线程，--overlay Injured 选择另一支撑手；--render 保存真实 GPU 截图。
# 负对照使用 --cases no-hand-lock --models mannequin，必须确认 skinPassed=false。
$env:ALS_RESOURCE_PACK_PATH = 'res://assets/generated/als_nonprop_crouch_slip_20261011_v2/resource-pack.json'
& './Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' `
  --headless --path . --fixed-fps 60 res://scenes/tests/mantling_contact_rollback_smoke.tscn `
  -- --als-model=girl --enable-root-motion --single
```

runner 在启动前逐文件核对活动包登记摘要，冻结配置、模型场景、相关源文件和程序集，并在结束后再次核对；任何变化都使总体失败。日志、逐帧数据、manifest 和结果按运行目录独立保存。
