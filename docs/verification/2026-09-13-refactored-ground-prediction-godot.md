# Refactored 落地预测的 Godot 胶囊查询

日期：2026-09-13，第一百七十四批。延续原 P4 脚部/骨盆输入补完。

## 查询实现

AlsRefactoredGroundPredictionGather 消费上一批独立查询合同，使用自有的
query-only ShapeCast3D 和胶囊；不会修改角色碰撞体。UE 厘米/世界轴转为
Godot 米/世界轴，胶囊尺寸已经是世界尺寸，不能再乘角色缩放。

要求显式提供同时满足 Visibility 阻挡与 WorldStatic/WorldDynamic/
Destructible 分类的 Godot 层映射。三组非空且不重叠，其他层不参与查询，
不是直接复制 Motor 的 CollisionMask。调用方传入角色全部物理 RID，
包括需要排除的附属物体。忽略 Area，保留最近阻挡，无论其法线是否可行走；
坡度判断仍由原预测模型消费。

Godot CastMotion 忽略起始重叠，因此先执行零运动胶囊查询，再执行扫掠。
记录初始接触/穿透，不沿用 V4 拒绝初始穿透的行为。Godot 安全/不安全
分数是区间，先细化至 0.1 mm 行程，再从首个命中的接触点、法线和胶囊
支撑函数解算接触面时间，避免将物理容差变成额外胶囊半径。平面解精确；
曲面仍依赖后端接触点/法线精度，不能声称任意复杂几何逐位等于 Chaos。

主线程/物理阶段检查先于 Godot 查询；禁用时不查询，无效输入在查询前
拒绝，返回原请求的完整身份/序号。准备与结果消费保持纯数据，可放 Worker。

本次使用 agent-reach 核对网页读取路径；Jina 读取失败后，官方文档直读成功，
并与本机 GodotSharp 4.7.2 XML 一致。初始重叠处理及查询限制参考
[PhysicsDirectSpaceState3D 官方说明](https://docs.godotengine.org/en/stable/classes/class_physicsdirectspacestate3d.html)。

## 真实物理验证

场景 `scenes/tests/refactored_ground_prediction_smoke.tscn` 在 30/60/120 Hz
各通过以下检查，均正常退出 0：

- 同上一批 UE 临时世界的 1260 组高度、速度、缩放和 mask 输入，实际在
  Godot 中查询，再从 Worker 消费预测结果。每档 462 组预测为正；最大
  预测误差 9.685755e-8，最大命中时间误差 5.9604645e-8。
- 17 项几何/边界检查：可行走/陡坡、三种允许/不允许碰撞层、miss、
  最近墙体阻挡后方地面、三角网格、球面解析接触、三次胶囊尺寸变更、
  禁用无查询、Worker 查询拒绝、非法尺寸、非物理阶段和移动平台。
  原生平地批次同时覆盖角色/道具 RID 排除与 Area 忽略。
- 每档 3019 次显式 ShapeCast 更新。没有把这个查询次数当作最终性能
  预算合格证据；10 角色完整动画、持续十分钟的预算仍属 P7。

日志：`artifacts/refactored-ground-query-174-{30,60,120}-verified.log`。
15 项预测/姿态曲线 Core 逻辑与 Import 数据检查通过：
`artifacts/tests/ground-prediction-174.trx`。Godot 优化 Debug 构建零警告/错误。

## 保留差异与首错

恰好接触（中心高 90 cm，半高 90 cm）有 108 个有效查询：Chaos 夹具标为
初始穿透，Godot 几何判定标为接触。本批保留各自分类，并严格限定差异
只能发生在该边界、双方 blocking、TOI 为 0；其他高度穿透分类必须一致。
原模型接受两者，预测值保持一致。每档实际穿透 108 组。

首次 60 Hz 测试停在这个分类差异（row 288），记录
`artifacts/refactored-ground-query-174-60.log`。随后在斜向扫掠 row 558 发现
原始时间分数偏早，预测误差约 2.76e-4；记录 `-60-contact.log`。
补接触面解后通过，保留原预测 2.5e-4、TOI 1.5e-4 的测试门槛，并加入
曲面解析接触检查，没有用更宽误差掩盖该问题。首个构建的身份 int/uint
类型错误已修正。

## 当前接线边界

这是真实物理桥接组件，尚未由生产角色的帧调度调用。生产场景的明确
碰撞层映射、GroundPredictionBlock 来源与读取时点仍需接入；不能把本批
标成实际 Demo 的空中预测或脚部视觉已经修复。

下一项补 Grounded/Jump/Fall/Land 其余六个曲线生产点、缓存 PoseState，
将独立预测、查询和完整脚部 Rig 接到生产候选/提交链，再进行整角色
接触、平台及多帧截图验收。第 616 帧旧失败、默认完整入口、P3/P4 验收
和 P5A–P7 继续开放，音频暂缓。
