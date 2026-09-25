# Godot ALS P4 Task 15 Handoff

**日期：** 2026-08-30
**项目：** `${env:GODOT_ALS_ROOT}`
**当前工作树：** `ARCHIVED_P4_WORKTREE_PATH`
**分支：** `feature/p4-pose-foot-placement`
**提交基线：** `678899e` (`test: add P4 cross engine pose golden`)
**引擎：** Godot 4.7.2 .NET
**参考实现：** `Sixze/ALS-Refactored`，锁定提交 `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

## 1. Handoff 结论

本次 handoff 对应用户确认的方案 1：

- `parallel` 是真实多角色 Worker 临界路径，承担 P4 性能硬门禁；
- `single` 使用同一完整 ALS 功能路径，只切换 Worker 所属线程组，是确定性参考路径；
- `single` 必须通过功能、single/parallel 摘要等价、错误计数、代际拒绝和稳态零分配门禁，并报告真实 timing；
- `single` 的十角色串行 wall-clock 不作为并行临界路径性能门禁；
- 不关闭 Aim、IK、Layering、Turn/Rotate，不降低质量或更新频率，不制造成功 marker。

P4 Task 15 的代码和测试已完成，当前提交包含本文件、规范口径更新和 Task 15 实现。Task 16（可操作 P4 Demo）与 Task 17（完整 P4 验证闭环）尚未开始，因此当前不能宣称整个 P4 已完成。

## 2. 总体目标和边界

### 已纳入 P4

- 完整 View/Aim 输入和 AimOffset mesh-space 修正；
- Head、Spine、Arm/Hand 等上半身分层 mask；
- Standing/Crouching Turn In Place 和 Rotate In Place；
- Foot IK、Foot Lock、pelvis correction；
- 平地、斜坡、楼梯、平移平台、旋转平台和 base-change/release 状态；
- Gather -> Worker -> Commit 三阶段线程所有权；
- worker modifier 失败时完整 local pose 和 visual root 回滚；
- 1/10 角色 single/parallel 确定性摘要和零分配矩阵；
- UE 导出、Godot 导入、P4 profile、curve、mask 和 cross-engine golden 合同。

### 明确留到后续阶段

- Overlay gameplay、装备/切换和道具生命周期；
- 通用 Notify / Notify State、Sync Runtime、ActionPlayer；
- Mantle、Roll、碰撞安全 Root Motion；
- Ragdoll、Get-up 和 gameplay 级 Pose Recovery；
- 完整 ALS Camera 行为。当前 P3 已有键鼠、相机 basis、WASD 对齐和 rotation mode 的正确基础语义；
- P7 的 30 秒热身、10 分钟 Release 最终性能认证。

## 3. 当前已完成能力

### P0-P3 基线

- P0：Godot C# 工程骨架、Godot-free `Als.Core`、固定布局 frame contract、双缓冲 exchange、generation 生命周期和确定性数学；
- P1：Godot process-group Gather/Worker/Commit、single/parallel 角色替换与线程亲和性验证；
- P2A/P2B：UE 5.9 C++ 导出器、完整 ALS 资产 manifest、267 个资产/141 个正式文件导入、真实 Mannequin/Overlay/道具资源审计；
- P3A/P3B：locomotion 状态、真实 Mannequin AnimationTree、single/parallel 运行时、输入/相机方向修正、可见性/替换/失败恢复。

### P4 已交付部分

- P4 asset/profile/curve/mask 合同和 cross-engine pose golden；
- P4 View/Aim、Turn/Rotate 和 physical foot placement；
- platform-local foot lock、pelvis correction、base change、teleport 和正常 ray miss/release；
- transactional modifier：捕获完整 local pose，统一 component-space 修正，一次写回，失败时精确回滚；
- Aim sampling 使用受影响骨骼及祖先闭包，避免无关 68 骨骼遍历；
- modifier full-pose digest 复用，避免重复读取整套骨骼；
- P4 10 角色矩阵的严格命名字段解析、重复/未知/缺失字段拒绝、finite/non-negative timing 校验和七项 digest pair 校验。
- Task 15 短时性能证书统一通过 `scripts/verify-p4-matrix.ps1` 执行：optimized non-incremental Debug build，四个 cell 固定顺序，child matrix 禁用 .NET tiering，并在成功或失败后恢复调用者环境。

## 4. Task 15 变更清单

### 新增

- `scenes/tests/p4_animation_harness.tscn`
- `scripts/p4-verification-functions.ps1`
- `src/Als.Godot/Locomotion/AlsP4HarnessContext.cs`
- `src/Als.Godot/Locomotion/AlsP4HarnessContext.cs.uid`
- `src/Als.Godot/Locomotion/P4AnimationHarness.cs`
- `src/Als.Godot/Locomotion/P4AnimationHarness.cs.uid`

### 修改

- `src/Als.Godot/Animation/AlsComponentPoseModifier.cs`
- `src/Als.Godot/Animation/AlsPoseScratch.cs`
- `src/Als.Godot/Locomotion/AlsP3Character.cs`
- `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- `src/Als.Godot/Locomotion/AlsP3bHarnessContext.cs`
- `src/Als.Godot/Locomotion/P4PoseSmoke.cs`
- `tests/VerificationScripts.Tests.ps1`
- `docs/superpowers/specs/2026-08-28-p4-aim-layering-foot-placement-design.md`

临时 stage profiler、实验性代码和编辑器偶然生成的既有 P4 `.uid` 已清理；只保留两个新 C# 脚本的 `.uid`。

## 5. 验证证据

最近一轮验证结果：

- `dotnet test`：`788/788` 通过，其中 Core `539/539`、Import `249/249`；
- `VerificationScripts.Tests.ps1`：`22/22` 通过；
- `VerifyP3b.Tests.ps1`：`67/67` 通过；
- `dotnet build`：0 warning、0 error；
- `git diff --check`：通过；
- P4 Pose graph、single/parallel foot placement、late rollback、完整 pose 回归：通过；
- P4 Pose active benchmark：`5655.346 ms / 10000`，稳态和 active 托管分配均为 `0 B`。

### P4 矩阵

timing 顺序为 `gather_commit_p95_us / worker_p95_us / total_p99_us`：

| Cell | 结果 | timing |
| --- | --- | ---: |
| `1/single` | 通过，7 项 digest 相等，7 个分配桶为 0 | `324 / 1932 / 3060 us` |
| `1/parallel` | 通过，和 single 摘要相等，7 个分配桶为 0 | `465 / 2396 / 3473 us` |
| `10/single` | 功能/摘要/线程/代际/零分配通过；仅不参与性能 hard gate | `1519 / 15386 / 18205 us` |
| `10/parallel` | 功能/摘要/线程/代际/零分配通过；本次受控运行 timing 超门槛 | `1591 / 4023 / 6808 us` |

`10/parallel` 曾观测到最好一次 `1341 / 2558 / 5619 us`，但这仍不能替代稳定的性能闭环。两种 10 角色模式的七项 digest 为：

```text
result      160ACAD43D4BA04B
pose        2F89CFF92BE8F5C5
full_pose   B82CA1FAB5CD621C
root        40E99EC0255FC635
aim         3A57D7FCB5696D29
turn_rotate DA1CB8857AAFE227
feet        A5C6917B045EB6FF
```

两组 10 角色 cell 在 timing gate 前均通过 missing/stale/generation/lag/thread、精确 600 帧/角色计数、replacement、old-generation rejection 和七个 0B allocation bucket。

## 6. 下一阶段执行顺序

### Task 16：可操作 P4 Demo

1. 复制或扩展现有 P3 demo，保留已修正的键鼠和相机语义；
2. 接入一个可见生产 Mannequin rig，保证场景中只有一个可见角色；
3. 增加连续缓坡、楼梯、平移平台、旋转平台和 base-change 路径；
4. 接入 Aim、Turn、Rotate、Foot Lock、pelvis 和失败恢复的真实 Worker 路径；
5. 增加 300 physics-frame headless smoke 和 HUD：frame、mode、aim、turn/rotate、foot lock、pelvis、errors、worker timing；
6. 手工确认 RMB aim、静止 LookingDirection Turn、静止 Aiming Rotate、移动/蹲伏/跳跃取消、平台局部锁定和 teleport release。

### Task 17：完整验证闭环

1. 新增/完善 `verify-p4.ps1`，依次执行 Debug build、P4 focused/core/golden、Godot smokes、P4 Demo、1/10 矩阵、Pester、P3B 回归和 Release tests；
2. 将 `10/parallel` 的性能硬门禁固定为 Gather+Commit p95 `<=1500 us`、Worker p95 `<=2500 us`、total p99 `<=4000 us`；
3. `single` 继续执行全部功能和等价门禁，并保留真实 timing；
4. 完成 locked-base、`git diff --check`、clean worktree 和唯一 `P4_VERIFICATION_OK` marker；
5. 进行独立代码审查，确认后再合并到 `main`。

## 7. 复现命令

在 `ARCHIVED_P4_WORKTREE_PATH` 执行：

```powershell
dotnet build .\GodotALS.sln --no-restore
dotnet test .\GodotALS.sln --no-build --no-restore
Invoke-Pester -Path .\tests\VerificationScripts.Tests.ps1
pwsh -NoProfile -File .\scripts\verify-p3b.ps1 -GodotExecutable <Godot-4.7.2-console.exe>
pwsh -NoProfile -File .\scripts\verify-p4-matrix.ps1 -GodotExecutable <Godot-4.7.2-console.exe>
git diff --check
git status --short --branch
```

Godot harness 必须只接受四个用户参数：`--mode`、`--characters`、`--warmup`、`--frames`；缺失、重复或未知参数应失败。
裸 Godot matrix 命令仅用于诊断，不构成 Task 15 的受控短时证书。P7 仍负责默认 Release、30 秒热身和十分钟长时认证。

## 8. 维护规则

- 不修改 `Als.Core` 的已冻结 P0-P3 合同，除非新增行为有独立 schema、golden 和 focused tests；
- 不把 `single` timing 伪装成并行 timing，不通过删除工作、降低质量或调整计时窗口达标；
- 新增 P4 字段必须进入 result digest、single/parallel pair、回滚和非法输入测试；
- Worker 不做物理查询、不访问其他角色、不修改外部 SceneTree；
- Main Commit 不读取 Worker 独占的 Skeleton/AnimationTree；
- 任何阶段失败都必须保持完整姿态回滚和 generation 保护；
- P5A/P5B/P5C 的通用动作、Overlay、Mantle/Roll/Root Motion 要建立独立设计和验证边界，不在 P4 demo 中偷渡。
