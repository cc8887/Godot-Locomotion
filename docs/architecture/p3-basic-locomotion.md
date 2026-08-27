# P3 Basic Locomotion Closure

## 1. 结论与范围

P3 已把 P3A 的确定性 motor/model 接到 Godot 4.7.2 的真实 Mannequin 动画资源，形成可操作的第三人称示例和 1/10 角色 single/parallel 自动门禁。当前闭环覆盖：

- Standing/Crouching、Walk/Run/Sprint；
- Looking Direction、Velocity Direction、Aiming 三种 rotation mode 的 P3 身体朝向；
- JumpStart、FallLoop、LandRecovery；
- 方向 BlendSpace、stride、play rate、lean 和 animation phase；
- 严格帧序 motor -> model -> animation -> commit；
- 真实 68-bone Mannequin、单 skeleton、多 clip animation library；
- 1/10 角色、120 帧 warmup、600 帧测量的 single/parallel 一致性与零稳态托管分配。

P3 不包含 AimOffset、上半身分层、Turn/Rotate in Place、Foot IK、Overlay gameplay、动作系统、Root Motion、Ragdoll 或完整 ALS Camera。这些功能没有取消，边界见第 10 节。

## 2. 锁定输入与资产闭包

| 合同 | 锁定值 |
| --- | --- |
| `ALS-Refactored` | `b754d6f0f2bb03741d301f8fb88077ebfe561e17` |
| P3A repository closure base | `e69f18bb3410d77ef50df38b073535b5e9f20635` |
| formal manifest SHA-256 | `369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327` |
| Godot | `4.7.2.stable.mono.official.ed1daf0bf` |

Formal manifest 状态为 `complete`，统计为 267 assets / 141 files / 7 skeletal meshes / 4 static meshes / 126 animations / 4 textures，`errorCount=0`、`warningCount=0`。代表资源 smoke 进一步确认 Mannequin 68 bones、6 clips、2 Overlay 资源和 1 个道具资源。`assets/generated/als_v4/**` 仍是可复现的 ignored 生成物，不进入 Git。

## 3. Stable-ID locomotion profile

`assets/config/p3_locomotion_profile.json` 只保存完整 UE object path 经过 formal manifest 唯一解析所得的 SHA-1 stable ID。编译器拒绝未知字段、缺失或重复 ID、跨 skeleton 引用、空或退化 sample grid、不支持的 additive 类型以及 additive base pose/type/frame 不一致；没有 basename 或运行时 fallback。

基础与动作映射：

| Purpose | Stable ID |
| --- | --- |
| Mannequin | `86d98d8177feb473c8a5f406c5b42f8c2a2f7b07` |
| Standing idle | `621a81bf492cb9120b45cfd91b685854afb7dc75` |
| Crouching idle | `146fff5000e151a3790ba5aca8a5bfee4363e909` |
| JumpStart | `0b439429cf3a3692eaea77a81b275ae96eab937f` |
| FallLoop | `8b1315b68336709c4a00bcd18871f0b3ba44308a` |
| LandRecovery | `8e310a7484f5a1fda4a549e1b80fd0405b1d4f35` |
| Lean BlendSpace | `6cdc10621a632cfe5aae96e76a82b37da26891e3` |

Standing sample ring：

| Sample | Coordinate | Stable ID |
| --- | --- | --- |
| Walk F | `(0, 0.5)` | `6124eafdcbeaaf04bca366add34c821faa0e4963` |
| Walk LF | `(-0.353553, 0.353553)` | `44a7f89b2c1dac832ca63753c131a037420f9d7e` |
| Walk RF | `(0.353553, 0.353553)` | `fc2d3a4142a1bd82d20877d806c783ff56dfe688` |
| Walk B | `(0, -0.5)` | `32fe18c71ccb860fe35c01d6b2b10fa2e4d98297` |
| Walk LB | `(-0.353553, -0.353553)` | `a4c6e0e455e7be7355cdd7c3ce49272d07773b18` |
| Walk RB | `(0.353553, -0.353553)` | `eb84a748fee4615754ce3cbcd3c259b33918b935` |
| Run F | `(0, 1)` | `572c3c83c9007964c233db4c7288ae38e20c3dec` |
| Run LF | `(-0.707107, 0.707107)` | `8ae1b9703a7d0144e570247d88629530b376885f` |
| Run RF | `(0.707107, 0.707107)` | `945bdda63e8a379694c792c3545fe11dd166b724` |
| Run B | `(0, -1)` | `859f8a49c55747e7382a1ae15970b23cc12f3f85` |
| Run LB | `(-0.707107, -0.707107)` | `b07a51bbab122c81679ac30d3f2f78f45ac14dc8` |
| Run RB | `(0.707107, -0.707107)` | `245ea51e30449a60b7d2b783ec0f6d24ec3bacc0` |
| Sprint F | `(0, 1.5)` | `8bd6ad52ad04a2ad23b47187886c630701df5260` |

Crouching sample ring：

| Sample | Coordinate | Stable ID |
| --- | --- | --- |
| Walk F | `(0, 1)` | `8eb8837c9f32973628b83ed2b98f7d68a0e82aa9` |
| Walk L | `(-1, 0)` | `21c24bd7df5192db2e2a860457f2b7b0681de41d` |
| Walk R | `(1, 0)` | `db60b2c35ce5ef5216c782fc1f33549cbcf8278d` |
| Walk B | `(0, -1)` | `f9ec8804e251f03c57e04dde4db9bd921457bae4` |

Lean BlendSpace 在初始化期展开为 5 个 additive clips：center `3c7efe09469508fd9e40015b3bf593dde39c540b`、forward `bbee3b8e0605aecaa74a14b1e60765c5a1c8d455`、back `15d6284bb8404d8015e3d35c704a0097f54bb1e3`、left `59c6b48109bab7ffcd187cec52f931e56e4873d7`、right `32e7c873dcf8fafd51e1140b17fa35081a941126`。五者必须共享 `ALS_N_Run_BasePose` stable ID `8e23008795aa766a761798e916907dd97aee6506`、additive type `1`、base pose type `3` 和 frame `0`。因此最终 library 含 28 个去重 profile clips。

## 4. Animation library 与 graph

初始化期只实例化一个 Mannequin 和一个 68-bone `Skeleton3D`。`AlsAnimationLibraryBuilder` 加载 profile 的 28 个 clips，将 track 重写到同一 skeleton，并以 `clip_<integer id>` 注册到一个 `AnimationLibrary`；integer ID 到 `StringName` 的表在 worker 启动前冻结。

`AlsLocomotionGraphBuilder` 一次性建立并缓存所有 `NodePath` / `StringName`：

```text
Grounded
  Standing: Idle + directional locomotion + Lean + Scale + Seek
  Crouching: Idle + directional locomotion + Lean + Scale + Seek
JumpStart: clip + Lean + Scale
FallLoop: looping clip + Lean + Scale
LandRecovery: clip + Lean + Scale
```

顶层状态为 `Grounded -> JumpStart -> FallLoop -> LandRecovery -> Grounded`，Grounded 内部在 Standing/Crouching 间切换。控制器从 `AlsFrameResult` 写入实际 stance/state、blend coordinate、stride、play rate、lean 和 phase，然后每帧手动 advance 恰好一次。动作状态不会每帧 seek；LandRecovery 使用物理已 Grounded、恢复窗口未结束的独立语义。

## 5. 初始化、线程与生命周期所有权

角色组合根的 process order 固定为：

```text
Order 0 / main: AlsP3Character
  input command -> CharacterBody3D motor -> post-move snapshot -> exchange input
Order 1 / main or subthread: AlsP3WorkerRoot
  model Evaluate -> visual root -> AnimationTree Apply/advance -> pose digest -> exchange result
Order 2 / main: AlsP3CommitStage
  identity/generation/frame validation -> diagnostics/HUD publication
```

所有 Godot 资源、graph、animation library、参数 handle 和双缓冲 exchange 均在 worker process group 开启前创建。运行期不查找字符串路径、不重建 animation graph。single 模式把 Order 1 放在 main thread，parallel 模式放在 subthread；Order 0 和 Order 2 始终属于 main thread。

角色替换只在完整 committed frame 边界发生：registry 提升 generation，旧 generation 结果必须被真实拒绝，active/spare visual rig 交换后继续同帧恢复。teardown 会先停 process、等待 inflight worker、再释放 graph/library/skeleton；worker 异常时恢复捕获的 68-bone pose 和 visual root，headless/debug 发布稳定失败码并非零退出，interactive release 保留最后有效 pose。

## 6. 可玩示例

主场景为 `res://scenes/demo/p3_locomotion_demo.tscn`，包含真实 Mannequin、平地、低障碍、缓坡、方向光、`SpringArm3D` orbit camera 和无边框诊断 HUD。Camera 跟随实际移动的 motor anchor，不跟随静止的组合根；HUD 显示实际速度、state、gait、stance、rotation mode、blend、stride、rate、lean、phase、worker timing 和 error count。

| 输入 | 行为 |
| --- | --- |
| `W/A/S/D` | 前/左/后/右移动 |
| `Alt`（按住） | Walking |
| `Shift`（按住） | Sprinting |
| `Ctrl` | Standing/Crouching toggle |
| `Space` | Jump edge |
| `V` | Looking Direction / Velocity Direction toggle |
| RMB（按住） | 临时 Aiming，释放后恢复此前 rotation mode |
| Mouse | Orbit yaw/pitch，pitch 有限幅且上移鼠标为抬高视角 |
| `Esc` | 捕获/释放鼠标 |

HUD 状态字符串只在 committed frame 改变时格式化，性能字符串每 15 次 refresh 更新一次，避免 render refresh 持续产生无意义字符串分配。

## 7. P3B 实测矩阵

以下数据来自 2026-08-27 的完整非 `-SkipRegression` gate；每行先 warmup 120 帧，再测量 600 帧。p95/p99 记录的是 worker 内六个有界 production segments 的合计墙钟时间，不包含 allocation probes、结果记录和事后 percentile 排序。

| Mode | Characters | Result digest | Raw pose digest | off_main | p95 us | p99 us |
| --- | ---: | --- | --- | ---: | ---: | ---: |
| single | 1 | `21A9D10F0AB9D1D5` | `0D87D2E73CA95BEB` | 0 | 402 | 563 |
| parallel | 1 | `21A9D10F0AB9D1D5` | `0D87D2E73CA95BEB` | 1 | 378 | 520 |
| single | 10 | `6C58FCA799D913B6` | `53B465443D67B4E0` | 0 | 369 | 497 |
| parallel | 10 | `6C58FCA799D913B6` | `53B465443D67B4E0` | 10 | 836 | 1053 |

四行均为 `missing=0 stale=0 generation=0 lag=0 allocations=0`。每个角色有且仅有 `600` 次 measured advance 和 `599` 次 raw skeleton pose change；character 0 的生产替换均观测到 `old_generation_rejected=1`。

五个独立 allocation buckets 在四行中均为：

```text
GODOT_ALS_P3B_ALLOC model=0 controller=0 skeleton=0 exchange=0 commit=0
```

这些微秒值是本次机器和进程条件下的阶段证据，不是最终性能预算。i7-10700、30 秒热身、10 分钟采样和完整里程碑 B 预算只由 P7 判定。

## 8. 完整验证命令与证据

在 repository root 执行：

```powershell
pwsh -NoProfile -File scripts/verify-p3b.ps1 `
  -GodotExecutable 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

本次完整 gate 的关键证据：

- P3B 四矩阵通过，single/parallel result digest 与 raw pose digest 精确相等；
- Pester `80 passed / 0 failed`；
- focused P3A 四矩阵通过：1 角色 digest `12CD6393BA75A1F9`，10 角色 digest `7BE3F9467CC4EB63`；
- `P2B_VERIFICATION_OK`、`P1_VERIFICATION_OK`、`P0_VERIFICATION_OK` 均来自真实子脚本的唯一精确 marker；
- Release tests：`Als.Core.Tests 253/253`、`Als.Import.Tests 64/64`；
- `P3B_RELEASE_TESTS_OK`；
- `P3B_REPOSITORY_CLOSURE_OK p3a_base=e69f18bb3410d77ef50df38b073535b5e9f20635`；
- 最终 `P3B_VERIFICATION_OK`，进程退出码 `0`。

为避免把 P2B/P1/P0 和 Release tests 重复执行两遍，P3B 调用 `verify-p3a.ps1 -SkipRegression` 得到真实 `P3A_FOCUSED_VERIFICATION_OK regression=skipped`，然后显式执行 P2B、P1、P0、Release tests，并复用 P3A 的同一 `Assert-P3aRepositoryClosure` 与锁定 base commit。P3B 不伪造 `P3A_VERIFICATION_OK`；上述组合证据与 P3A full closure 的覆盖范围等价，但只运行一次下游回归。

每个 child gate 必须同时满足退出码 `0`、唯一精确成功 marker、全部输出不含任意位置的 `SCRIPT ERROR` 或 `ERROR:`。因此“成功 marker 后仍有错误行”不能形成假阳性。

## 9. 手工验收

编辑器入口：

```powershell
& 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' `
  --path '.\.worktrees\p3b-real-animation-demo' `
  --editor 'res://scenes/demo/p3_locomotion_demo.tscn'
```

手工验收检查 W/A/S/D、Alt、Shift、Ctrl、Space、V、RMB、mouse orbit 和 Esc，并观察真实 Mannequin 的 Stand/Crouch/Walk/Run/Sprint/Jump/Fall/Land。自动门禁另有 300-frame demo smoke，手工检查用于动作观感、camera framing 和输入体验，不替代第 8 节的确定性回归。

## 10. 后续阶段边界

| 阶段 | 明确实现内容 | P3 当前状态 |
| --- | --- | --- |
| P4 | AimOffset、mesh-space correction、上半身分层、Turn/Rotate in Place、Foot IK、Foot Lock、pelvis correction、楼梯/移动平台视觉处理 | 未实现，下一阶段 |
| P5A | Curve Runtime、通用 Notify/Notify State、Typed Event、Sync Marker、Dynamic Transition、ActionPlayer | 未实现 |
| P5B | 完整 Overlay gameplay：Overlay profile/state、Rifle/Pistol 道具挂点与生命周期、装备/切换动作、与 stance/gait/rotation/action 的组合规则 | 资产已导出并通过 P2B 代表 smoke；gameplay 未实现 |
| P5C | Mantle detection、Roll、Motion Correction、碰撞安全 Root Motion、移动平台目标 | 未实现 |
| P6 | PhysicsAsset runtime、Ragdoll、face-up/down、pose capture、RecoveryBlend、Get-up、完整第三/第一人称 ALS Camera | 未实现；P3 只有最小 orbit camera |
| P7 | 1/10/16/32 扩展曲线、Profiler 驱动优化、i7-10700 30 秒热身 + 10 分钟 Release 预算 | 未实现；P3 timing 仅为短时回归证据 |

因此 AimOffset、分层、Turn/Rotate in Place、Foot IK、Overlay、Notify/Sync/ActionPlayer、Mantle/Roll/Root Motion、Ragdoll/Get-up/Pose Recovery、完整 Camera 和最终十分钟性能门禁都仍在总方案中，只是严格位于 P3 之后。
