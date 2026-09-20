# Refactored 两腿、骨盆与地面查询帧所有者

日期：2026-09-13，第一百六十九批。原 P4 补完继续推进，生产入口尚未切换。

## 本批实现

新增 `AlsRefactoredFootRigFrame`，持有独立参考骨架、父索引、候选/提交姿势、
曲线、左右腿历史、查询偏移和骨盆弹簧。`AlsFootRigCompiler.CreateFrame` 将
前两批的实际原图配置与参考骨架绑定组合为运行实例。

原图脚部切片按 FootOffset → PelvisOffset → 左 ApplyFootIk → 右 ApplyFootIk
求值。输入是脊柱控制后的 native component pose，后续 Hand IK 仍由外层消费。
接口分三段：Prepare 保存当前候选源姿势/曲线和 locked targets 并产生查询请求；
Evaluate 消费物理线程返回的观察值并完成两个腿链；外层全部消费者验证完成后
才 ValidateCommit/Commit。Cancel 丢弃候选，不改变任何已提交动画历史。

查询请求带帧身份、单调递增的独立请求序号、ToWorld、两条射线与启用标记。
返回值必须回显整个请求；取消后的同帧同射线重试也不会接受旧响应。
请求序号属于传输关联，故不随动画状态回滚。该接口尚未连接 Godot 调度器，
没有把现有旧帧 V4 物理观察直接冒充为新查询的响应。

保留原图分支差别：

- 跳过整个 Rig：姿势/曲线透传，骨盆和两腿历史不推进。
- 脚变换无效：跳过查询与两腿函数，保留上次偏移及腿历史；骨盆仍使用保存
  的两脚偏移运行弹簧，并传播骨盆变换。
- 某脚曲线低于查询阈值：该脚查询输出归零/向上法线；腿部函数仍运行，
  即便写回权重为零也推进其内部历史。这不等同于无效脚变换。
- Reinitialize 在候选中重置 Rig 历史；取消重初始化不会污染旧状态。

必须明确提供 FootLeftIk/FootRightIk/PoseMoving 的曲线布局。已知布局内
Present=false 的曲线按原 GetCurveValue 语义返回零；缺少布局项则拒绝绑定。
没有自动使用 V4 同义猜测或移动布尔值。

## 验证与范围

新增 7 项事务专项全部通过，覆盖：右腿晚期失败时左腿已有写入的整体丢弃；
取消请求与同帧重试隔离；脚无效和整个 Rig 跳过；禁用曲线及候选重初始化；
原版曲线布局；十个独立实例的串行/并行一致性。

并行专项各模式运行 10 实例 × 48 帧 = 480 帧，比较提交姿势和完整状态。
同线程预热后的额外 48 帧检测到零托管分配。该检查使用十骨测试骨架和纯
观察值，不能代替十个完整 Godot 角色的最终性能预算。

相关 Import 回归 26/26 通过，含前批原生腿部组合 1440 帧及图合同。
Godot Debug 优化构建零警告、零错误，diff 空白检查和脚本语法检查通过。
记录：`artifacts/foot-frame-169-tests.log`、
`artifacts/foot-frame-regression-169-tests.log`、`artifacts/foot-frame-169-godot-build.log`。
本批未修改 UE 插件源码；未重复前批已通过的插件构建/加载/打包，也没有
声称整套新帧已完成原生 CR_Als/AnimBP 或渲染回归。

## 曲线与输入审计的新证据

当前加载清单 `assets/generated/als_v4/als_manifest.json` 包含 126 个动画。
它们的片段曲线没有 FootLeftIk、FootRightIk、PoseMoving、PoseGrounded、
PoseInAir；有 15 个 FootLock_L、15 个 FootLock_R、26 个 Weight_Gait。
统计为 `artifacts/foot-curve-input-audit-169.json`。
这仅是**动画片段曲线**统计，不代表最终图没有脚部权重：

- 当前 Grounded 原图包装器用 ModifyCurve 写 Enable_FootIK_L/R；
  由 `AlsGroundedMovementCurveCompiler` 校验，输入引脚值而非序列曲线决定输出。
- Jump/Fall 的原图写 Weight_InAir；Landing 的不同状态还会写脚 IK/锁定曲线。
- V4 原 PelvisAlpha 为左右 Enable_FootIK 的平均值，已经由
  `AlsPelvisIkInputCompiler` 按图校验。
- Refactored `GetControlRigInput()` 的 PelvisOffsetAmount 则是
  Clamp01(PoseState.GroundedAmount + PoseState.InAirAmount * InAirState.GroundPredictionAmount)。
  PoseState 从曲线缓存读 PoseGrounded/PoseInAir/PoseMoving，不能沿用 V4 的
  PelvisAlpha 或只看当前 MovementState。

因此下一步必须补最终图的曲线来源与混合权重适配，连同输入更新阶段一起
验证；仅改曲线名或用 Weight_Gait 代替 PoseMoving 不构成等价移植。

## 未完成项

新所有者尚未连接真实主线程查询和 Worker 的阶段调度，生产仍是 V4 控制器。
后续要验证上述曲线/骨盆输入来源、locked Final 与普通脚目标历史，接入
native/Godot 空间转换及真实查询，再接入完整根的提交/取消和手部消费顺序。
完成后进行 UE 整链、移动支撑窗口、平台、30/60/120 Hz、多帧截图与人工验收。
第 616 帧约 41.100025° 的旧视觉失败仍开放；默认完整入口、原 P3/P4 整角色
验收和 P5A–P7 均未完成，音频继续暂缓。
