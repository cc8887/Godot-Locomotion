# P4 Aim、分层姿态与 Foot Placement 架构记录

## 1. 状态和证据边界

P4 在 P3 真实 Mannequin locomotion 与 Gather/Worker/Commit 线程合同上实现了：

- View/Aim、AimOffset mesh-space correction 和上半身分层；
- Standing/Crouching 的 L/R 90/180 Turn In Place；
- Standing/Crouching 的 L/R Rotate In Place；
- Foot IK、platform-local Foot Lock、pelvis correction；
- 平地、连续斜坡、不同规格楼梯、平移/旋转平台和 base release；
- 1/10 角色 single/parallel 的生产路径矩阵和可操作 P4 Demo。

本文只记录已经冻结的合同与已有自动证据，不把尚未执行的验收写成通过项。
Task 17 focused 与 clean-worktree 完整 `verify-p4.ps1` 均已通过，覆盖四格短时矩阵、
自动 Demo、focused Import/Core、全部 P4 场景、P0-P3B 回归、Release 测试和仓库
closure，P4 自动实现闭环已经完成。Godot Editor 八项手工观感验收和 P7 长时
Release 性能认证尚未执行，本文不把二者写成已签收。

Task 17 计划要求根目录 README，但此前仓库没有该文件。本轮按已记录的实施裁定
创建简洁项目/验证索引，没有用其他文档冒充 README，也没有因此扩大 P4 完成范围。

## 2. 来源、资产和 Profile

### 2.1 固定来源

| 项目 | 固定值 |
| --- | --- |
| 参考仓库 | `https://github.com/Sixze/ALS-Refactored.git` |
| ALS-Refactored commit | `b754d6f0f2bb03741d301f8fb88077ebfe561e17` |
| 目标 UE | `5.9.0` |
| 兼容补丁 | `reference/patches/als-refactored-ue-5.9-engine-version.patch` |
| 补丁 SHA-256 | `3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f` |
| 资产 lock schema | `1` |
| 正式 manifest SHA-256 | `F12C56C705F05C7C55E77955BD14A3729A2E96D5FAA669FDBD759702B0EA846E` |
| 资产审计 | 267 assets / 141 files / 126 animations / 0 error / 0 warning |

`reference/als-refactored.lock.json` 锁定参考 commit、引擎和唯一兼容补丁；
`reference/als-v4-export.lock.json` 锁定正式资产摘要及数量。Overlay 和道具模型
已经包含在全量资产边界内，音频明确不在该批范围内。

### 2.2 P4 Profile

`assets/config/p4_pose_profile.json` 使用独立 schema 1。本文记录时的冻结文件
SHA-256 是
`4AC9FAF6E7C84738DDA77888E7E57489B18137AD74FF00F438A182F6C05055C6`；
它是当前内容摘要，任何受控 profile 变更都必须重新生成、复核并更新摘要，不能
把旧 hash 当成对未来内容也成立的常量。

Profile 通过 stable ID 指定 1 个 skeleton、AimOffset 及 Down/Forward/Up 三条
sweep、8 个 Turn clip 和 4 个 Rotate clip。导入阶段把 stable ID 编译为整数
animation/bone/curve ID；运行时不按 basename 回退，也不在帧路径进行字符串查找。

11 个 mask root 和边界如下：

| Mask | Root | 不进入该子树的边界 |
| --- | --- | --- |
| upperBody | `spine_01` | `neck_01`, `clavicle_l`, `clavicle_r` |
| head | `neck_01` | 无 |
| leftArm / rightArm | `clavicle_l` / `clavicle_r` | `Hand_L` / `hand_r` |
| leftHand / rightHand | `Hand_L` / `hand_r` | 无 |
| pelvis | `Pelvis` | `spine_01`, `Thigh_L`, `Thigh_R` |
| leftLeg / rightLeg | `Thigh_L` / `Thigh_R` | `Foot_L` / `Foot_R` |
| leftFoot / rightFoot | `Foot_L` / `Foot_R` | 无 |

编译器按 skeleton hierarchy 把每个 root 展开为有序 bone ID 数组，并拒绝缺失或
重复骨、越界子树、左右不配对、错误 skeleton、未知字段和名称 fallback。纯 Aim
因此只能影响声明的上半身闭包，root、pelvis、thigh 和 foot 不应被污染。

### 2.3 曲线和 additive 来源

导出合同同时保留原始曲线和 canonical 曲线。每个结构化曲线携带连续
`StableCurveId`、`CanonicalKind`、`SourceName`、`SourceProvenance`、infinity
规则和按时间排序的 key/tangent/interpolation：

- 普通 ALS 曲线保留 `source_curve` provenance；
- Turn/Rotate 使用唯一 canonical
  `RotationYawSpeedRadiansPerSecond`，当前生产合同要求
  `derived_root_track` provenance：离线解包 root yaw 后按原采样率求导；
- 角度和角速度进入 Godot/Core 前统一为弧度；
- Aim sweep 的 additive type、base pose animation/frame 和 skeleton 都来自
  manifest 并在 profile 编译时校验兼容性；
- `FootLock_L` / `FootLock_R` 是 profile curve alias；可用曲线编译为整数 ID，
  单个 clip 缺少相应曲线时按 `missingLockDefault=0` 采样；alias、stable binding
  或 provenance 结构非法才阻止 profile 发布。

非有限、乱序或同时间重复 key，非法插值，不兼容 additive base，无法解包的
root yaw，以及缺少或具有非法 provenance 的结构化 curve 都在发布前失败。
生成器通过 staging、schema 校验、byte/hash 对比和原子替换保护上一次正式输出。

## 3. 线程所有权和帧时序

P4 保持固定 process group 顺序：

| Order | 线程 | 唯一所有权 |
| ---: | --- | --- |
| 0 | Main | motor、CharacterBody3D、输入/相机快照、floor/platform、物理 foot query |
| 1 | Worker | Core runtime state、AnimationTree/Mixer、Skeleton3D、curve sampler、pose scratch、结果双缓冲 |
| 2 | Main | identity/frame/generation/result 校验、target-yaw 锁存、下一帧 probe 缓存、可见性与生命周期 |

Gather 只发布无 Node 引用的不可变 `AlsFrameInput`。Worker 不做物理查询、不访问
其他角色和外部 SceneTree；Commit 不读取或写入 Worker 独占的 Skeleton、
AnimationTree 或 modifier。SceneTree 中 character transform 的唯一写入者是 Order 0
motor：Worker 负责 yaw 求解并发布值类型结果，Commit 只验证并锁存 target-yaw，
不旋转 Node；下一帧 Order 0 在移动前消费该锁存值。

这是实现阶段对冻结设计中“Main Commit 独占/提交 actor yaw”措辞的明确裁定：
该措辞只保留为 Order 2 对 yaw 结果的验证、锁存和生命周期所有权，不代表
Order 2 直接写 Node transform。以本文的 as-built 合同为准，SceneTree yaw 写入
始终由下一帧 Order 0 motor 完成，避免 motor 与 Commit 双写角色变换。

脚部查询保持 ALS 上一已求值姿态语义，同时禁止跨线程 Skeleton 读取：

```text
Worker N    发布未修正的 left/right foot probe origin
Commit N    按 identity 缓存 probe request
Gather N+1  用当前 character/platform transform 转换并执行 physics query
Worker N+1  消费不可变 hit，解算 Foot Lock、foot correction 和 pelvis
```

这一拍只作用于环境反馈，不给输入、motor 或基础 locomotion animation 额外增加一帧
延迟。Foot Lock 目标保存为 platform-local position/rotation；平台更新后以最新
transform 重建 world target。base change、teleport、jump/fall、ray miss、权重
失效、过伸、停用或平台移除进入确定性 release path，不把正常释放记录成 Worker
异常。

Worker 每帧只推进一次 AnimationTree，随后捕获完整 local pose，构建
component-space pose，依次应用 Aim/layer、pelvis、左右脚修正，再一次写回受影响
骨骼。Turn/Rotate phase 和由 canonical yaw 曲线积分得到的 actor yaw delta 在同一
Worker Evaluate 中产生；Commit 只验证/锁存结果，不重新推导 yaw 或旋转 Node。
因此 actor yaw 的 SceneTree 写入只有 Order 0 motor 一处，P3 target-yaw 与静止
Rotate 也不会形成两个互相竞争的求解来源。

Modifier 是事务：写前保存完整 local pose 和 corrected visual root。任一 Aim、
curve、pelvis、foot 或 Skeleton 写入失败时，恢复完整 pose/root，不发布部分结果，
冻结最后有效视觉状态，并要求新的 generation 成功提交后才重新可见。停用、替换
和失败恢复同样以 `(frameId, characterId, slotGeneration)` 隔离旧结果。

## 4. Cross-engine Golden

P4 golden 位于 `tests/Als.Core.Tests/Fixtures/P4`，使用 schema 1、kind
`p4_pose_trace`、固定步长 `1/60 s`：

| Trace | Cases | 内容 |
| --- | ---: | --- |
| `aim` | 5 | center/up/down/left/right |
| `turn` | 8 | standing/crouching、L/R、90/180 |
| `rotate` | 4 | standing/crouching、L/R |
| `feet` | 3 | flat/slope/stairs |
| `platform` | 5 | translate/rotate/base-change/teleport/release |
| 合计 | 25 | 固定 case ID 和离散状态合同 |

每个 trace 同时保留 `nativeActual` 和 `portExpected`：前者 provenance 为
`als_runtime`，记录锁定 UE/ALS 运行时观察；后者 provenance 为
`port_oracle_v1`，是 Godot/Core replay 的规范期望。两者职责独立，不能用 port
结果覆盖 native 观察，也不能要求跨引擎姿态 bitwise 相同。

离散状态、ID、方向、phase、选择和 reason code 必须精确一致；姿态比较允许
position `0.001 m`、rotation `0.0017453292519943296 rad` 的声明误差。所有 trace
继续携带同一参考 commit、UE 5.9 和补丁 hash。

## 5. 四格生产矩阵证据

每个 cell 使用相同生产角色、完整 Aim/Layer/Turn/Rotate/Feet 路径、120 帧
warmup 和 600 帧 measured。四个成功 marker 的身份前缀固定为：

```text
P4_MATRIX_OK mode=single characters=1 warmup=120 frames=600
P4_MATRIX_OK mode=parallel characters=1 warmup=120 frames=600
P4_MATRIX_OK mode=single characters=10 warmup=120 frames=600
P4_MATRIX_OK mode=parallel characters=10 warmup=120 frames=600
```

严格 parser 要求 marker 继续包含七个摘要字段：`result`、`pose`、
`full_pose`、`root`、`aim`、`turn_rotate`、`feet`。同一角色数的 single 与
parallel 必须逐字段相等。本轮自动证据为：

| Cell | result | pose | full_pose | root | aim | turn_rotate | feet |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `1/single` | `5176F0CCFD79BEB0` | `F3B51B66055E91EE` | `1F081DD47C204D8F` | `05CFF6F44DF04385` | `ADE73D416540EEB5` | `10BD5E3998B16F7B` | `E5C4F148648AA1C2` |
| `1/parallel` | `5176F0CCFD79BEB0` | `F3B51B66055E91EE` | `1F081DD47C204D8F` | `05CFF6F44DF04385` | `ADE73D416540EEB5` | `10BD5E3998B16F7B` | `E5C4F148648AA1C2` |
| `10/single` | `CAF7192A62967A03` | `F4DCFF7E0567681E` | `47C2417AD4FEA266` | `40E99EC0255FC635` | `3A57D7FCB5696D29` | `DA1CB8857AAFE227` | `16C619BEEF875F37` |
| `10/parallel` | `CAF7192A62967A03` | `F4DCFF7E0567681E` | `47C2417AD4FEA266` | `40E99EC0255FC635` | `3A57D7FCB5696D29` | `DA1CB8857AAFE227` | `16C619BEEF875F37` |

Godot `ObjectID` 是进程本地值，不能直接进入跨进程证书。Harness 只在计算摘要的
`AlsFrameResult` 值类型副本中，把每条 lane 的 translating/rotating platform 映射为
稳定的 `2*i+1` / `2*i+2`；映射同时核对 31-bit compact `PlatformId` 和完整
`ColliderId`，并按本帧 active lock 或 walkable gather hit 验证来源。未知、非法或
compact 相同但 full collider 不匹配的身份直接使证书失败。生产运行时的完整
platform/collider identity、Foot Lock 状态和提交结果均未改写。

以下 timing 是同一次完整四格受控运行的结果，没有从不同轮次拼接最好值：

| Cell | Gather+Commit p95 | Worker p95 | Total p99 | 性能语义 |
| --- | ---: | ---: | ---: | --- |
| `1/single` | `166 us` | `899 us` | `1332 us` | 非门禁真实参考 |
| `1/parallel` | `168 us` | `903 us` | `1285 us` | 非门禁真实参考 |
| `10/single` | `962 us` | `8981 us` | `10560 us` | 串行确定性参考，不作为 parallel wall-clock 预算 |
| `10/parallel` | `998 us` | `2036 us` | `3937 us` | 硬门禁 `<=1500 / <=2500 / <=4000 us` |

只有 `10/parallel` 承担 P4 短时性能硬门禁；其他三格仍必须报告真实 timing，
且通过完整功能、摘要、线程、代际、帧计数和分配合同。每格要求
`missing/stale/generation/lag/thread=0`，每角色 600 次 advance、modifier、commit，
总计精确为 `characters * 600`，并要求一次 replacement、一次 old-generation
拒绝和正确 lane 数。

稳态硬门禁的七个托管分配桶是 `model`、`curve`、`controller`、`modifier`、
`skeleton`、`exchange`、`commit`，四格均为 `0 B`。Godot 物理查询 wrapper 的
`foot_gather` 分配透明地单列但当前不作为 P4 零分配门禁；本次四格观察值依次为
`1519416 B`、`1519416 B`、`13734368 B`、`13734368 B`。因此本文只声称七个
Worker/交换/提交桶为 `0 B`，不声称包含 Foot Gather 的整条 Godot 物理路径为
`0 B`。

这组 120/600 数据是 optimized Debug/TOOLS editor-host 的短时阶段证据，不是
P7 的 Release 长时证书。

## 6. Demo 证据和手工验收

生产入口为 `res://scenes/demo/p4_locomotion_demo.tscn`。Task 16 runner 先运行
真实 P3 键鼠/相机 smoke，再运行注入确定性命令的 300-frame P4 场景 smoke；已有
自动 marker 为：

```text
GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1
P4_DEMO_OK frames=300 rigs=1
P4_DEMO_VERIFICATION_OK frames=300 rigs=1
```

P4 smoke 以真实 motor 命令穿过连续地面、斜坡、不同楼梯、平移平台及相邻固定
落点，并验证 Aim、Turn/Rotate、Foot Lock、release、pelvis、非陈旧 HUD frame
和单一可见生产 rig。HUD 保留 P3 的 Grounded/Speed/Blend/Stride/Rate/Lean/
Phase/FPS 诊断，同时显示 P4 所需的 frame、mode、aim angles、turn、rotate、
left/right lock、pelvis、errors 和 worker timing。

以下八项必须在 Godot Editor 中人工执行；截至本文建立时均未签收：

1. RMB 下 AimOffset 连续看上/下/左/右，pelvis 和 feet 不受上半身修正污染；
2. LookingDirection 静止旋转镜头能触发正确 L/R 90/180 Turn，无 actor snap 和明显脚滑；
3. Aiming 静止超过阈值时 Rotate clip 与 actor yaw 同步；
4. 移动、蹲伏、跳跃和 rotation mode 切换能正确取消或切换 Turn/Rotate；
5. 平地、斜坡和楼梯上双脚不漂移、不反折，pelvis 无明显跳变；
6. 平移/旋转平台上 Foot Lock 保持平台局部目标，平台切换和 teleport 正确释放；
7. HUD 显示 frame、mode、aim angles、turn/rotate、foot lock、pelvis、errors 和 worker timing；
8. 场景中始终只有一个可见生产 rig，不增加教程或说明文字。

自动 smoke 证明合同和覆盖，人工清单补充动作观感；二者不能相互替代。

## 7. Task 17 完整门禁

正式入口的 CLI 已冻结为：

```powershell
pwsh -NoProfile -File scripts/verify-p4.ps1 `
  -GodotExecutable '<Godot-4.7.2-console.exe>'
```

可选 `-ProjectRoot <repo>` 用于显式指定仓库。`-Focused` 可以输出诊断和各子门禁
证据，但唯一顶层/终态成功标记只能是
`P4_FOCUSED_VERIFICATION_OK regression=skipped`，绝不能出现 full marker
`P4_VERIFICATION_OK`。
下面第 1 至第 6 项是 Focused 与 full 的公共前半段。Focused 不是只跑单元测试：
它先完成 Task 16 正式 Demo runner 和 Task 15 正式四格 Matrix runner，只有第 6
项成功后才输出 focused marker 并返回；默认 full 模式继续执行第 7 至第 11 项。

入口按固定顺序执行：

1. 保存并禁用 `DOTNET_TieredCompilation` 与 `COMPlus_TieredCompilation`，在统一
   `try/finally` 中保证原值恢复；
2. restore，并以 `Debug -p:Optimize=true --no-incremental` 构建 Godot host；
3. 运行 P4 profile/curve/import focused tests 和 Core/golden tests；
4. 运行 graph、pose、single/parallel rollback、foot gather、lifecycle、
   single/parallel foot-placement scenes；
5. 运行 Task 16 P3 input + P4 Demo 精确 marker；
6. 按 `1/single`、`1/parallel`、`10/single`、`10/parallel` 运行正式矩阵 runner，
   parse 四格并比较两组七摘要；
7. 运行仓库 Pester；
8. 以隔离子进程运行不带 Skip 的 P3B 完整回归，并要求唯一
   `P3B_VERIFICATION_OK`、`P3A_VERIFICATION_OK`、`P2B_VERIFICATION_OK`、
   `P1_VERIFICATION_OK`、`P0_VERIFICATION_OK`；
9. 最后运行 `dotnet test GodotALS.sln -c Release --no-restore`，随后以
   `--no-build` 分别生成并校验 Core/Import Release TRX，要求两项目各自非零执行、
   全部通过，防止 solution 级单一 TRX 被后写项目覆盖；
10. 以冻结 base `1d941ee0611ca2f6af710deab7a6d63f07e2105c` 检查祖先关系、
    committed/cached/worktree 三层 `git diff --check`、禁止跟踪的构建输出和
    `git status --porcelain --untracked-files=all`；
11. 所有步骤通过后恰好输出一次整行 `P4_VERIFICATION_OK`。

Godot headless editor 加载带 `TOOLS` 的 Debug host，所以场景和矩阵证据使用优化的
Debug build。最终 solution Release 及独立 Core/Import TRX 证明 ExportRelease
编译/测试边界，但不等于已经执行 Godot exported Release runtime；两种证据不能
互相替代。

本轮 focused 命令真实输出了
`P4_FOCUSED_VERIFICATION_OK regression=skipped`。随后 clean-worktree 完整命令
通过 repository Pester `235/235`、非 Skip `P3B_VERIFICATION_OK`、Release Core
`539` 项和 Import `249` 项独立 TRX，并输出
`P4_REPOSITORY_CLOSURE_OK p4_base=1d941ee0611ca2f6af710deab7a6d63f07e2105c`
及唯一终态 `P4_VERIFICATION_OK`。首次完整运行发现并补交了三个 Godot C# script
UID；上述证据来自 UID 已跟踪后的 clean HEAD，不是失败轮次的拼接结果。

## 8. 后续范围

P4 不提前实现下列模块：

- P5A：通用 Curve Runtime、Notify/Notify State、Typed Event、Sync Runtime、
  Dynamic Transition 和 ActionPlayer；
- P5B：Overlay gameplay、Rifle/Pistol 道具挂点与生命周期、装备/收起/切换；
- P5C：Mantle、Roll、Motion Correction 和碰撞安全 Root Motion；
- P6：PhysicsAsset runtime、Ragdoll、face-up/down、Pose Recovery、Get-up 和完整
  第三/第一人称 ALS Camera；
- P7：i7-10700、30 秒热身、10 分钟 Godot .NET Release Export 最终认证。

P7 还必须覆盖完整里程碑路径、前 10 个 Tier 0 全质量角色、60 Hz、p95/p99、
分配、事件和线程错误；当前短矩阵不能冒充这项最终证书。
