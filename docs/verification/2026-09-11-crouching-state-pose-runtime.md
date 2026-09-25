# 蹲姿主状态姿势与曲线运行图

日期：2026-09-11。第五十三批。工作区 `ARCHIVED_P5A_WORKTREE_PATH`。

## 本批完成

新增 `AlsCrouchingStatePoseGraph`，消费第四十六批严格编译的五状态内容合同，
不另建播放器时钟，不直接写 Skeleton。外层提供状态机候选、来源秒数、Cycles
缓存姿势及其曲线、RotationScale/RotateRate 和现有 Turn 通道的双银行输入。

- Idle：固定显式来源，先覆盖 FootLock_L/R 与 Enable_Transition，再通过
  `(CLF) Turn/Rotate` Slot，最后将 RotationAmount 乘 RotationScale。
- Moving：直接消费外部 Cycles 缓存，不受 Idle Slot 影响。
- Rotate Left/Right：使用独立来源身份及外部时间，RotationAmount 乘 RotateRate。
- Stop：Cycles 为基底，左右两条固定采样在同一个 Mesh Space 分层操作中组合，
  曲线按 Base、左、右的 Override 顺序处理，再覆盖 FootLock_L/R。
- 活动过渡：按既有中断栈的顺序组合姿势，整栈结束后归一化；QuickFeet 使用
  已验证的逐骨骼 WeightFactor，曲线仍用普通 alpha。物理骨索引显式映射到 Godot
  索引，不能把两个编号空间直接混用。
- 非法状态、非有限动态输入、越界来源/Slot 时间在写出姿势前拒绝；重复求值不
  改变外部状态。显式采样器不消费外部时钟中的对应条目。

这是状态内容求值组件，不是整个主图 Update/Initialize/CacheBones 调度器。
Rotate 惯性化请求仍由拥有整个姿势历史的外层处理，没有在此处伪造第二套惯性化。

## 多来源 Mesh Space 混合

`AlsMeshSpacePoseBlend.BlendLayers` 扩展既有单层内核，接收连续的多个来源姿势与
逐骨骼 SourceIndex。未覆盖骨保持 SourceIndex=0、Weight=0，不能丢掉其目标链计算。

依据本机 `${env:UE_ENGINE_ROOT}\Engine\Source\Runtime\Engine\Private\Animation\AnimationRuntime.cpp`
的 `BlendPosesPerBoneFilter`，尤其 `AccumulateMeshSpaceRotation` 与 MeshSpaceRotation
分支：每个骨选择当前来源的局部旋转，但父目标旋转来自同一条已经累积的目标链。
它不是分别累积完整左右来源的组件姿势，也不是串行执行两次单层混合。
零权重子骨仍要通过混合后父旋转恢复局部空间，位置与缩放保留局部混合。

原单层 API 与运算顺序保留，允许精确覆盖基底或一个完整来源块，拒绝错位别名、
非法源索引、骨层级及非有限权重。现有 80 组原生单层案例同时检查新旧入口逐项
一致；新增不同父旋转的双来源案例明确排除逐层混合和独立父链两种错误做法。

本批没有新增 UE 双来源原生数值探针或完整 AnimBP 最终姿势 oracle。源码核对、
原有单层探针与合成双层测试不能被描述成完整原版最终骨骼等价证明。

## 资源与末尾时间

P4 动画库构建入口之前只合并 Locomotion、P4 和 Detail，未像基础入口一样合并
正式来源表。因此新增蹲姿主图同时需要 Turn 和 Stop 资源时暴露缺失。本批在
六方向 Cycle 配置下补齐全部正式来源资产，smoke 检查所有来源和 Turn 均在库中。
未改原始资产、正式绑定编号、播放速率、动画关键帧或既有键鼠控制。

复用的 `AlsStandingTurnSlot` 已含蹲姿 Turn 资产，不新增 Slot 时钟。其时间合同
使用 float 秒数，校验现在以 float 时长为边界，姿势通过既有 SampleSourceSeconds
处理 float 末尾可能略高于 Godot double 时长的情况。大于合法 float 末尾一个
ULP 的值仍拒绝。删除了测试中提前截短 Slot 时间的做法，直接验证两个活跃末尾。

曲线依据 `AnimNode_ModifyCurve.cpp` 和 `FAnimationRuntime::BlendCurves`：Scale
是乘法，Override 只用实际存在的来源曲线替换。当前组件接口仍返回标量，完整
presence/属性传递及最终角色曲线写回没有在本批关闭。

## 验证结果

日志及 TRX：`artifacts/test-results/crouching-state-pose/`。

场景 `scenes/tests/crouching_state_pose_smoke.tscn` 的最终日志为
`state-pose-endpoints.log`：

| 检查 | 结果 |
| --- | --- |
| 30/60/120 Hz，五个独立状态 | 1065 帧 |
| 连续起停与重入 | 1050 帧，186 个 QuickFeet 活跃记录、61 个中断栈帧 |
| 曲线逐值对照 | 12702 次，包含仅缓存拥有的曲线 |
| Slot | 140 帧实际改变 Idle，其他四状态不受影响，2 个活跃末尾样本 |
| RotateRate | 420 帧非零 RotationAmount 缩放 |
| 非法候选 | 5 次拒绝，写出姿势不变，恢复后相同重试 |
| 活动 Stop 过渡与 Slot 姿势/曲线 | 预热后 2000 次，0 B |

真实来源独立采样后直接组合的分支用于检查接线，Cycles 输入和主状态时间仍由
组件夹具提供，不是完整外层缓存/同步调度或 UE 完整图逐帧回放。

其他结果：

- Core 常规 1891/1891，沿用排除 P5A golden/trace schema 两组的命令。
- Mesh Space 专项 20/20，含新增 9 项；Release 20/20。
- Import 全套 1073/1073；蹲姿/Lean 相关 Release 202/202。
- Godot 最终构建零警告、零错误。
- Cycles 1248 帧、2385 次缓存求值、3744 次曲线检查、3 次故障拒绝通过。
- Standing/Pivot 各 5040 帧、Detail 1890、Sprint 1260；Slot 端点改动后复跑通过。
- Main 1050 来源帧、420 混合帧、104 中断帧通过。
- Worker 单/多线程各 180 帧、10 个来源事件，晚期事件失败回滚均无泄漏；
  result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4`、
  pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB` 保持。
- `verify-p4-pose.ps1` 通过，图、姿势、双模式脚部与晚期回滚、零分配守卫通过。
- `verify-p4-matrix.ps1` 四格通过：1/10 角色、单/多线程，各热身 120 帧与采样
  600 帧；两组摘要一致。此旧矩阵不代替最终全质量十分钟预算。
- 用已安装的 Pester 4.10.1 显式运行 Profile/RuntimeDefaults/Golden 三组脚本，
  25/25 通过，没有修改系统模块配置。

## 未通过的 Demo 门禁

`verify-p4-demo.ps1` 未通过，直接复跑同样失败；不得写成“全套 P4 通过”。
实际生产路线的移动平台证据窗口为第 85--96 帧，锁定样本为零。

`P4DemoSmoke` 仅增加失败诊断，不改变路线输入、状态门槛或断言：

- 平台窗口首尾：左右均为 `Locked=0 / ReleaseReason=None`。
- 平台窗口曲线最大值 `(IKL, IKR, LockL, LockR) = (1, 1, 0, 0)`。
- 旧诊断中的 FootLock `(1,1)` 来自后面的第 111--145 帧转身窗口，不能当成
  角色仍位于平台时的曲线证据。
- 暂时仅撤下本批动画库补齐和 Slot 末尾适配后，完全相同的窗口、位置、曲线
  与零锁定失败重现；随后已恢复两项改动并重新构建。没有回退用户修改。

证据见 `p4-demo-direct.log`、`p4-demo-without-library-and-slot-changes.log`、
`p4-demo-diagnostic.log`、`p4-demo-window-diagnostic.log`。
这排除了上述两个改动，并确认此窗口缺少脚锁输入；还需核对当前原图状态、最终
曲线链及场景路线起停时机。不能仅据此认定 IK 求解器错误，也不能将曲线强制设为
1、延长锁定或放宽断言来掩盖它。该门禁保持未通过，加入后续集成验证。

## 保留的首错

1. 新测试先使用 `==` 比较过渡栈而编译失败；换成 ValueType.Equals 后又被 Godot
   运行时的 InlineArray 限制拒绝。最终按状态、数量、Latest、每个过渡和边身份
   逐项比较，未修改运行时结构或比较门槛。
2. `state-pose-first.log`：大小写敏感的骨名查找失败；改用现有不区分大小写的
   名字映射，并保留一一对应与父链校验。
3. `state-pose-second.log`：测试调用基础库入口而缺 Turn；改用 P4 入口后，
   `state-pose-third.log` 进一步暴露其正式来源资产缺失，补齐库闭包。
4. `state-pose-fourth.log`：上述 InlineArray 的运行时比较失败。
5. `p4-focused.log`：聚合脚本默认加载 Pester 6，而使用了旧 `Invoke-Pester -Script`
   参数，停在 Profile 脚本阶段。单独显式调用已安装 Pester 4 后三组 25/25 通过；
   不宣称聚合脚本本身已修复或通过。

## 下一步

先接蹲姿主状态共享的 Cycles 缓存：Moving 读取 compiled 110、Stop 读取 122，
都指向 compiled 31 的 Save。严格验证其原生身份与所有权，按真实 Update 顺序、
最大权重上下文和生命周期初始化来源，而不是从最终姿势权重反推更新。
随后把 Main、站/蹲上游缓存、来源收集和候选提交接入生产路径。

继续最终曲线及 RotationScale、动态 Layering/Add/LS/Lean/IK masks、完整 Foot
IK/Lock/pelvis/thigh；专项重放上述平台曲线窗口，并进行多帧移动截图和人工验证。
Standing 已知 WeightFactor/零速度回退边界仍在计划内。然后按原 P5A/P5B/P5C/P6/P7
推进正式事件/动作、Overlay 道具、Mantle/Roll/Root Motion、Ragdoll/Get-up/Recovery、
完整相机及十分钟性能预算；音频暂缓。

未运行新的移动截图或 UE 完整图最终姿势对照，不关闭起步滑移、交错步、上身和
人工视觉验收。本批未 commit/revert/merge，也未修改或启动 UE 插件。
