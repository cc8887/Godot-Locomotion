# P3A 确定性 Locomotion 实现记录

## 阶段结论与来源锁定

P3A 实现的是 CharacterBody3D motor、基础 stance/gait/rotation mode/jump-land 状态模型，以及同帧的
Gather/Worker/Commit 执行边界。本阶段的完成证据覆盖确定性、线程归属、角色替换、热路径分配与基础物理
输入，不承诺 Overlay、IK、Mantle、Ragdoll、Camera 或 UE/Godot 物理解算结果逐帧相同。

行为参考固定为 `ALS-Refactored` commit
`b754d6f0f2bb03741d301f8fb88077ebfe561e17`。唯一批准的兼容性修改是
`reference/patches/als-refactored-ue-5.9-engine-version.patch`，只把 `ALS.uplugin` 的
`EngineVersion` 从 `5.8.0` 改为 `5.9.0`；补丁 SHA-256 为
`3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f`。
`reference/als-refactored.lock.json` 同时锁定 repository、commit、目标引擎和补丁 hash；不得把 reference
工作树中的其他修改、UE build 产物或外部路径作为正式输入。

## 单位、坐标与 trace 语义

UE 边界的长度/速度使用厘米，角度使用度；进入 Godot/Core 合同前统一转换为米和弧度。坐标映射为
UE `(X forward, Y right, Z up)` 到 Godot `(Y right, Z up, -X forward)`，即位置向量
`(X, Y, Z) cm -> (Y, Z, -X) / 100 m`。绕 up 轴的 yaw 因手性变化取反，并从 degree 转为 radian。

Golden trace 明确分三层：

- `physicalActual`：UE 实际角色运动、速度、落地等物理观测，用于证明输入场景确实触发了目标行为；
- `nativeActual`：ALS 原生动画/状态观测，用于理解参考实现，不是 Godot port 的通过/失败标准；
- `portExpected`：在同一规范化输入上按移植合同生成的期望状态，是 Core replay 的比较对象。

不直接比较 UE physics 与 Godot physics：两套引擎的碰撞求解器、接触缓存、floor 判定与积分细节不同，逐帧
数值等价既不稳定也不是移植合同。`nativeActual` 也不作 pass/fail，因为原生动画状态可能包含尚未移植的
表现层策略；若用它作硬期望，会把观察事实误写成当前切片的规范。通过标准是 physical evidence 足够、
`portExpected` 可重放且与 Godot Core 结果一致。

`physicalActual.acceleration` 不是输入命令或 `CharacterMovementComponent::GetCurrentAcceleration()`。
commandlet 在每次固定 60 Hz tick 后读取实际速度，并以相邻 post-tick 速度差除以 `1/60 s` 得到实际加速度；
frame 0 的前一速度取自最后一个 warmup tick，因此首帧同样有确定的物理历史。相同的 actual velocity 历史同时
输入独立 C++ port oracle 和 Core replay，三层 trace 的职责保持分离。

Godot/Core 中 `ActualVelocity.Y` 是 up。accepted jump 当帧进入 `JumpStart`，只要仍为 InAir 且垂直实际速度
严格大于 0 就保持；`Y <= 0`（包含顶点恰好为 0）转为 `FallLoop`。没有 accepted jump 的 walk-off 直接进入
`FallLoop`，落地清除 jump-start 子状态并进入 `Land`。

## 同帧管线、所有权与替换

每个 physics frame 固定按以下顺序执行：

1. Main thread / process-thread-group Order 0：`AlsCharacterMotor.Step()` 负责 CharacterBody3D 旋转与移动；
   其中已有上一帧提交目标时，先把该 `TargetYaw` 应用到 `CharacterBody3D.GlobalBasis`，随后执行移动，
   从同一个 post-`MoveAndSlide()` 最终 `GlobalTransform` 采集 transform 与 yaw，并连同 velocity、floor、
   命令快照发布为不可变 `AlsFrameInput`；首帧或角色替换后的首帧没有既有目标，保持当前真实朝向；
2. Worker / Order 1：只读取值类型快照，执行纯 `AlsLocomotionModel.Evaluate()`，不访问 SceneTree、Node、
   ResourceLoader 或文件系统，再发布 `AlsFrameResult`；single 模式在主线程运行同一模型，parallel 模式在
   sub-thread 运行；
3. Main thread / Order 2：只消费 identity 与当前 frame/character/generation 完全一致的结果，并在同一
   physics frame 把纯值 `TargetYaw` 提交到角色槽，供下一帧 Order 0 消费；Order 2 不读写 Node transform。
   commit 校验本帧输入的 `CharacterYaw` 与 `CharacterTransform` yaw 一致，并校验它等于本帧 Order 0 已应用的
   上一提交目标，再把真实 yaw 写入 digest；missing、stale、generation mismatch、yaw commit mismatch 或
   lag 均使 gate 失败。

场景对象和 motor 归主线程；每个 worker 独占 runtime state，exchange 由该角色槽独占。identity 是
`(frameId, characterId, slotGeneration)`。测量期 frame 420 替换 character 0：旧槽释放后 generation 从 1
递增为 2，旧 generation 的结果必须仍可按旧 identity 读取、但必须被新 identity 拒绝；替换帧的新结果也
必须在该帧 commit。这个合同阻止复用槽把旧角色结果提交给新角色。

rotation 的 SceneTree 生产所有权固定在 Order 0 motor/collision/actual snapshot；worker 只能计算值类型
`TargetYaw`，Order 2 也只能提交值，二者均不得访问 Node。规则来自锁定 ALS 源码：`VelocityDirection` 在可靠
移动速度下朝实际移动方向；`LookingDirection` 非 Sprint 时采用 P3A 计划锁定的无曲线降级合同：
`RotationYawOffsetCurve` 不可用时 offset 固定为 0，目标直接取 view yaw；Sprint 仍朝 velocity yaw。这不是对
ALS native curve 行为的冒充，`nativeActual` 继续作为独立观察。真实 `RotationYawOffsetCurve` 的动画图输入与
采样属于 P3B 接入项。降级规则不依赖 actor yaw，因此把上一帧 `TargetYaw` 回灌为下一帧 `CharacterYaw` 时，
不同初始 actor yaw 会稳定收敛到同一 view 目标，且 view/velocity 不同时 Looking 不会退化成 VelocityDirection。
低速停止保持当前角色 yaw，本阶段不执行 TIP；`Aiming` 朝 aim yaw。目标角先用对应固定角速度做最短角
constant interpolation，再从当前真实角色 yaw 做固定 half-life 插值。Order 2 提交该帧最终 `TargetYaw`，
下一帧 Order 0 在移动前应用；该帧 gather 发布的 yaw 与 transform 则严格对应移动后的最终 body basis。

当前 floor 合同还没有 moving-platform identity。无论 grounded 与否，platform tuple 均明确为
`PlatformId=-1`、`PlatformTransform=Identity`、`PlatformAngularVelocity=Zero`；这表示 unavailable，不能
解释成静止平台已被识别。

## Golden 生成边界

`prepare-p3-reference.ps1` 只接受锁定 repository/commit，先验证补丁普通文件边界与 SHA-256，再通过临时
index 原子验证/应用唯一批准 patch。`generate-p3-golden.ps1` 的正式再生成路径必须通过 guard：reference
HEAD/remote/clean 状态、锁定 patch、插件受控同步、cold editor `ReadyCheck`，以及输出 schema、场景语义、
帧数和确定性检查。不得手改 fixture 后把 Pester 通过当成合法再生成；也不得跳过 cold `ReadyCheck` 或在
未锁定的 reference 修改上生成 golden。

## Harness 门禁与固定摘要

每个矩阵先运行 120 个 warmup frame，再测量 600 frame，固定 60 Hz。warmup 用于稳定 JIT 与运行时路径，
不进入正式 digest 或 allocation 结论。测量 coverage 的内部 bitmask 必须等于 `0x3FFF`，14 位依次表示
Walking、Running、Sprinting、Crouching、Jump、Land、Forward、Right、Backward、Left、
LookingDirection、VelocityDirection、Aiming、AppliedYawVariation；最后一位证明提交后的实际 GlobalBasis yaw
在测量期确实发生变化。coverage 是内部诊断，不进入公开成功 marker。

四阶段 allocation 必须各自为 0：`gather_motor`、`model`、`exchange`、`commit`。公开四矩阵 marker 的
完整固定格式与基线为：

```text
GODOT_ALS_P3A_OK mode=single characters=1 warmup=120 frames=600 digest=B79EDC1516A133F9 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0
GODOT_ALS_P3A_OK mode=parallel characters=1 warmup=120 frames=600 digest=B79EDC1516A133F9 missing=0 stale=0 generation=0 off_main=1 lag=0 allocations=0
GODOT_ALS_P3A_OK mode=single characters=10 warmup=120 frames=600 digest=D81D16BAA88519DC missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0
GODOT_ALS_P3A_OK mode=parallel characters=10 warmup=120 frames=600 digest=D81D16BAA88519DC missing=0 stale=0 generation=0 off_main=10 lag=0 allocations=0
```

1/10 角色的 fixed digest 分别为 `B79EDC1516A133F9` 与 `D81D16BAA88519DC`；single/parallel 必须命中各自
基线且相互相等。motor smoke 的成功 marker 为 `GODOT_ALS_P3A_MOTOR_OK cases=7`。

Core 热路径对 gait、stance、rotation mode 和 locomotion history 的合法性检查使用连续 byte enum 的显式
范围判断，不使用会依赖运行时 enum metadata cache 的 `Enum.IsDefined<T>`。后者的 cache 在测量期被回收后
可能以固定 232-byte 单位重建，并被误记为 model/motor 业务分配。Core 单测通过 `Enum.GetValues<T>()`
覆盖所有合法值，并锁定 Gait/Stance/RotationMode/LocomotionState 均以 byte 为底层、从 0 开始、连续到命名
terminal；测试输入 `-1` 转为 byte enum 后实际值是 `255`，与 terminal+1 等上界外值一起验证拒绝行为。
修复后连续三轮 focused 四矩阵均保持 `allocations=0` 且命中上述固定 digest；digest 同时包含 Core result 与
Order 0 actual snapshot 中的真实 yaw。focused 结果只用于根因验证，正式
完成仍以下面的非 skip 默认闭环为准。

失败时先看 `GODOT_ALS_P3A_DIAGNOSTIC`：`gather_motor/model/exchange/commit` 定位分配阶段，
`first_gather_frame/first_model_frame/first_commit_frame` 定位首帧，`replacements`、
`old_generation_rejected`、`replacement_frame_committed` 检查替换，`coverage` 检查缺失行为，
`rotation_commit_mismatches` 检查 snapshot transform/yaw 一致性及上一提交 TargetYaw 的 Order 0 应用结果，
`affinity_violations` 检查线程归属。
公开 marker 的 `missing/stale/generation/lag` 分别定位未发布、旧帧、
旧 generation 和非同帧结果；`off_main` 错误表示 single 泄漏到 worker 或 parallel 没有实际离开主线程。
脚本还把 Godot 输出中的 `SCRIPT ERROR`/`ERROR:` 视为失败，不能只看进程退出码。

## 编译边界

解决方案的 Release 映射会把 Godot host 项目编译为 `ExportRelease`，用于证明交付配置可编译，并运行
Release Core/Import 测试。Godot headless editor 实际加载的是带 `TOOLS` 的 Debug editor-host assembly；
P3A 运行时 gate 因此明确重建 `Debug -p:Optimize=true --no-incremental`，并在运行时拒绝关闭优化的 assembly。
这两项证据不可互换：optimized Debug/TOOLS 的 harness 运行不等于 ExportRelease runtime 已执行。

本机没有 Godot export templates，因此只验证了 `ExportRelease` 编译边界，没有执行 exported runtime；文档
和 marker 均不把 editor-host 结果冒充 exported runtime 验证。

为排除旧 P2A 批次 FBX 中遗留的 external texture object，本轮使用仓库现有 exporter 重新执行完整 P2A
双导出。新正式 manifest SHA-256 为
`369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327`，审计为 267 assets / 141 files、
0 error / 0 warning，第二遍确定性比较覆盖 146 files。137 个 FBX 中 `Texture`/`Video` external object 计数
为 0，`export_report.json` 的 `normalizedFbxKeys` 包含 `ExternalTextureObject`。随后清空的仅是当前 worktree
`.godot/imported` 与 ignored asset sidecar/compiled cache；`verify-p2b.ps1 -CleanImport` 首次导入无
`SCRIPT ERROR`/`ERROR:` 并输出 `P2B_VERIFICATION_OK`。因此本轮不依赖二次暖缓存，也未生成 TGA 旁车或
改写 manifest。正式 P2A 资产和确定性副本位于 ignored 输出目录，不进入 P3A Git 提交。

## 验证入口与默认回归闭环

正式完成证据只使用默认入口，禁止带 `-SkipRegression`：

```powershell
pwsh -NoProfile -File scripts/verify-p3a.ps1 `
  -GodotExecutable "${env:GODOT_EXECUTABLE}"
```

入口先 restore/build Release，运行 Pester、自身 motor smoke、optimized Debug editor host 和 P3A 四矩阵；
四矩阵通过后先校验正式 P2A manifest SHA-256
`369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327`，再严格按以下顺序执行。P2B 默认
强制 `-CleanImport`，只清理当前 worktree 的 Godot/ignored asset cache；任一步非零或 Godot 输出含
`SCRIPT ERROR`/`ERROR:` 均立即失败，不会继续打印 P3A success：

```powershell
pwsh -NoProfile -File scripts/verify-p2b.ps1 -GodotExecutable '<Godot console>' -CleanImport
pwsh -NoProfile -File scripts/verify-p1.ps1 -GodotExecutable '<Godot console>'
pwsh -NoProfile -File scripts/verify-p0.ps1 -GodotExecutable '<Godot console>'
dotnet test GodotALS.sln -c Release --no-restore
```

本轮用于修复旧 P2A 批次并证明冷导入的命令为：

```powershell
pwsh -NoProfile -File scripts/verify-p2a.ps1 `
  -EngineRoot "${env:UE_ENGINE_ROOT}" `
  -UnrealProject "${env:ALS_UE_PROJECT_FILE}"
pwsh -NoProfile -File scripts/verify-p2b.ps1 `
  -GodotExecutable '<Godot console>' `
  -CleanImport
```

默认闭环应出现 `P2B_VERIFICATION_OK`、`P1_VERIFICATION_OK`、`P0_VERIFICATION_OK` 和最终
`P3A_VERIFICATION_OK`；同时 Release Core/Import、Pester、motor 与四矩阵都必须通过且无脚本错误。

`-SkipRegression` 只允许 focused 开发迭代：它跳过 Pester、motor 以及上述 P2B/P1/P0/final test 回归，
仅保留 build 与 P3A 四矩阵以缩短定位周期。该路径只输出
`P3A_FOCUSED_VERIFICATION_OK regression=skipped`，绝不输出 `P3A_VERIFICATION_OK`，因此不能冒充正式
completion evidence。reference/golden 再生成属于独立的受控维护操作，也不能由 `-SkipRegression` 替代。

正式 non-Skip 入口锁定 P3A base `e69f18bb3410d77ef50df38b073535b5e9f20635`。在打印
`P3A_VERIFICATION_OK` 前，它先验证 base commit 存在且为 HEAD 祖先，再分别执行 base 到 HEAD 的 committed
diff、HEAD 到 index 的 cached diff，以及 index 到 worktree 的 diff whitespace 检查；因此 feature 分支及 fast-forward 后的 main
都覆盖完整 P3A 变更，而不是只检查未提交内容。随后检查全部 tracked 路径；
`.godot/.mono/bin/obj`、UE `Binaries/Intermediate/Saved/DerivedDataCache/StagedBuilds/Cooked`、生成资产、
benchmark/artifact 输出（保留 `.gdignore` sentinel）以及编译/打包扩展名均会使 gate 失败。负例测试锁定
whitespace error 和误跟踪输出都不能越过正式成功 marker。人工收口复核仍执行：

```powershell
git diff --check
git status --short
git ls-files | Select-String -Pattern '(^|/)(\.godot|bin|obj)/'
Get-FileHash -Algorithm SHA256 "${env:ALS_UE_PROJECT_FILE}"
```

最后一项必须仍为
`1F0661A6711E23D6271C33A1E9FD6AB9C9B94AC50ACEF79E35B97E3D933A61BB`；受控 exporter 构建、部署和
commandlet 双导出前后该 hash 一致，UE/插件 build 输出均不得被仓库跟踪。reference 侧也只允许前述锁定
patch。
