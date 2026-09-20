# Refactored 六个状态曲线生产点接入主图

日期：2026-09-13，第一百七十五批。原 P4 脚部输入补完。

## 接线位置

AlsMainMovementFrameRuntime 增加可选的严格 Refactored 曲线定义绑定，要求
Standing/Crouching 的 PoseMoving 缓存生产器已存在。保持当前 V4 资产与
状态图，明确适配以下 Refactored 生产边界，不宣称两版本整图完全相同。

- Grounded：读取实际 Grounded 缓存/Slot 输出后，写 PoseGrounded 和两脚
  IK=1。Grounded 状态直接消费，Land Movement 在该结果上继续附加落地
  曲线。后者继承 Grounded 输入，不套用静止 Land 的锁脚写入。
- Fall、Jump：完成各自 Lean/预测姿势混合后，先以独立
  GroundPredictionAmount 为 alpha 写两脚 IK，再写 PoseStanding/PoseInAir=1。
- Land：完成静止落地轻/重混合后，写两脚 IK/Lock、PoseStanding、PoseGrounded=1。
- 所有写入都在 Main Movement 父状态混合之前，之后继续 BaseLayer
  惯性化/Slot；float 和 precise 两条求值入口使用相同生产器。

原图证据是第 170 批完整节点导出和正式配置：Grounded ModifyCurve_6
接在 Inertialization 后；Jump/Fall ModifyCurve_3 接 TwoWayBlend_0，
随后 ModifyCurve_0；Land ModifyCurve_0 接 TwoWayBlend_1。
另外复查 Land Movement 的结果直接来自 Grounded 缓存/附加姿势混合，
该分支没有独立 Land ModifyCurve，不能以状态名称近似后强行锁脚。

新增 AlsRefactoredPoseCurveInputs，携带帧身份和独立预测值。未提供、
跨帧/世代、非有限值或把该输入传给未启用生产器的图，都会拒绝。
每次 Prepare 重新捕获候选输入，初始化但未访问的入口同样检查；隐藏
且不求值的图不会复用它求值。没有默认读取 V4 LandPrediction，也没有
修改 AlsFrameInput 的公开布局。

## 验证

MainMovementRuntimeSmoke 使用 `--base-layer --refactored-movement-curves
--refactored-state-curves`，后一参数仅为该测试场景选择绑定，尚不是 Demo
生产开关。30/60/120 Hz、冷地面/空中入口总计 3360 帧，通过：

- 六个新增点加两个既有缓存点实际运行；297 个状态混合帧，210 帧真实
  Land Movement Grounded 基础姿势，站蹲/停止/起跳/下落/静止落地均覆盖。
- 新预测独立变化于 .2..8，V4 姿势预测仍按原刺激为 0/1。逐曲线与
  状态权重核对，检查没有误接 V4 alpha 或在最终输出强制覆盖曲线。
- float/precise 各 3360 帧的曲线 presence/value 完全相同；原姿势、
  来源时钟、通知、请求及尾部惯性化保留同帧重试一致性。
- 18 次缺失/错误世代/NaN 输入拒绝，6 次 Grounded Slot 晚期失败和
  6 次最终 Slot 晚期失败，已提交来源/尾部历史保持。
- 原无新增曲线入口另跑 3360 帧通过。新增测试沿用第 172 批额外停止
  刺激，不能拿两次回放的事件数量作等值比较。

最终日志 `artifacts/refactored-state-curves-175-main-verified.log` 和
`artifacts/refactored-state-curves-175-original.log`。首次仅 float 验证日志
为 `refactored-state-curves-175-main.log`。补 guard 时误用只读身份属性的
with 赋值造成一次编译错误，改为显式构造后构建零警告/零错误。

## 未完成的生产闭环

新六点已在实际主移动图类型中实现，但目前只有回放显式传入定义和
独立预测数据。默认生产 BaseLayer 尚未启用；真实 Motor 查询尚未传给
这个新增输入，当前测试的预测值是受控数据。

接下来将第 174 批真实查询接到生产帧，按已提交最终曲线读取
GroundPredictionBlock 和 PoseState，确保隐藏/恢复、取消与重试一致。
之后连接新脚部 Rig 和完整根，再跑整角色移动接触、平台及多帧视觉
验收。不要将主图回放通过称为 Demo 脚步修复或完整移植验收。
第 616 帧旧失败、P3/P4 整角色与 P5A–P7 均开放，音频暂缓。
