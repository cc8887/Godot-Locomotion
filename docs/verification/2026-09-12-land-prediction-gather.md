# 落地预测：真实胶囊采样与帧输入（第八十四批）

承接第八十三批输入数据/公式，继续原 P3 主移动输入与 P5A 统一候选提交。
本批将实际落地预测查询接入 Motor，而不是用 Floor/FootHit 或固定预测值替代。

## 源图与实现

`AlsLandPredictionCompiler` 读取正式 `v4_movement_runtime_inputs.json` 中的
`CalculateLandPrediction`，检查分支、数据连接、函数/变量归属、胶囊尺寸来源、
命中 Time、IsWalkable、掩码曲线以及零返回分支。保留以下原规则：

- FallSpeed < -200 cm/s 才做预测。Godot 输入契约使用 m/s，阈值为 -2。
- 方向是归一化后的 `(Velocity.X, Velocity.Y, clamp(Velocity.Z,-4000,-200))`
  （UE 坐标）；映射到 Godot 时竖直分量是 Y，水平分量是 X/Z。
- 扫掠距离由原始竖直速度从 [0,-4000] 映射到 [50,2000] cm 并钳制。
  半径、半高来自当前胶囊，起点来自胶囊世界位置。
- 使用 `ALS_Character`、非复杂碰撞、忽略自身。项目配置中该 profile 忽略
  Visibility/Camera/Grabbable/Climbable，普通物理物体仍阻挡；Godot 查询使用
  当前 Motor 碰撞掩码和物理 Body，忽略 Area/自身。
- 阻挡命中且可行走才采 `LandPredictionCurve(hit.Time)`，再 Lerp 到零，
  Alpha 是 `Mask_LandPrediction`。保持原 Lerp 不钳制 Alpha 的语义。

纯 Core 模型生成扫掠运动并计算最终标量。Godot 主线程拥有禁用自动处理的
ShapeCast 子节点，以当前胶囊形状做即时查询。`AlsFrameInput.LandPrediction`
只保存命中标志、时间区间、法线/位置、数值 collider id 和查询几何；没有
Godot 对象进入 Worker。地面、上升及低于门槛的下落帧不做预测，字段归零。
当前应查询但没有快照时，标量模型明确拒绝，不把缺失数据伪装成无命中。

查询没有更新角色位置、速度或动画时钟，重复查询得到相同快照。实际 Motor
在运动后填入同帧快照，已有 Exchange 连同身份/代次传递整个输入结构。

## 发现并修正的引擎差异

第一次几何测试失败：Godot 默认扫掠在平地返回 0.375，解析命中比例应为
0.37209302，对应约 1.56 cm。保留原失败日志，不放宽 0.002 的比例误差门槛。
现在只对已有命中细化 safe/unsafe 区间，最多 12 次，目标旅行区间 <=0.1 mm。
无命中不细化。平地/蹲伏解析对照的最大比例误差为 `8.970499E-06`，在该
5.375 m 距离下约 0.0482 mm。这是已测几何的结果，不是任意碰撞场景误差承诺。

第二次测试表明 Godot 零运动形状查询也报告“刚好接触”。原 UE IsWalkable
拒绝初始穿透，并不拒绝所有 Time=0 命中。通过当前胶囊对命中平面的支持距离
判断有符号分离，使用 1 微米数值容差区分接触/穿透；接触可进入可行走判定，
穿透不可。墙面、30 度与 70 度斜坡、接触和 0.2 m 穿透分别验证。

移动平台首次测试在同一回调中写 Node.Position 后立即查询，读取的仍是上一个
物理位置。诊断保留前后命中位置/比例证据。修正测试为平台经物理帧更新后查询，
没有改变生产查询去读取尚未进入物理世界的视觉变换。

## 验证结果

| 物理频率 | 几何检查 | Motor 有查询的帧 | 非零预测帧 |
| --- | --- | --- | --- |
| 30 Hz | 15 | 13 | 10 |
| 60 Hz | 15 | 28 | 21 |
| 120 Hz | 15 | 55 | 41 |

几何场景还覆盖无命中、掩码/自身排除、落地清空、蹲伏改变半高、移动平台身份。
预热后 64 次查询零托管分配；60 Hz 最终测试另验证每次快照完全相同。
计时/查询成本仍需纳入最终 P7，不能把零分配当作十分钟性能通过。

- Import 18/18：源图变异拒绝、读取原生距离参数、方向/单位、缺失查询拒绝、
  不可行走/穿透、命中时间和 201 个 UE 曲线样本及掩码行为。
- Core 契约/Exchange 24/24：新增结构无托管引用、字段追加顺序、完整快照、
  旧代次拒绝与下一帧清空。
- Godot 最终构建零警告/零错误。保留并修正测试中的命令源接口名、FileAccess
  命名冲突，以及只读 FrameIdentity 不能使用 with 赋值的构建首错。
- 既有 P3a Motor、P4 FootGather 通过；没有放宽脚部平台测试或改写脚锁曲线。
- 生产 single/parallel 各 180 帧通过，各有 14 帧携带查询快照且身份与提交帧
  一致。十个来源事件、结果摘要 `21E164D829153157`、完整姿势摘要
  `CF9225D4DE9B2C8B` 保持，代次替换和隐藏恢复验证保持。

主要日志位于 `artifacts/`：
`land-prediction-physics-first.log`、`land-prediction-physics-refined.log`、
`land-prediction-platform-diagnostic.log` 保留失败；
`land-prediction-physics-30.log`、`land-prediction-physics-60.log`、
`land-prediction-physics-120.log`、`land-prediction-motor-regression.log`、
`land-prediction-foot-regression.log`、`land-prediction-frame-single.log`、
`land-prediction-frame-parallel.log` 是对应通过记录。

## 剩余边界

**Motor 查询已进入真实帧输入，完整空中动画消费者尚未接线。** Smoke 的掩码
0/1/其他值是受控输入，不是实际最终动画曲线反馈。后续需要将同帧 FallSpeed、
角色局部速度、已提交曲线历史和 Lean 候选状态按 UpdateInAirValues 顺序组合，
再接统一 Main Movement/BaseLayer 输出。还要区分全局 UpdateGraph 的变量更新
条件与动画节点停用时的局部输入历史，不能把两者一起无条件冻结。

继续核对地面速度/加速度、Stride、JumpPlayRate，接通最终曲线、动态上身与
完整脚部消费者。Godot 可行走判定目前使用 Motor.FloorMaxAngle；尚未实现
UE 每组件 WalkableSlopeOverride 的映射，也没有完成任意接触几何的 Chaos/
Godot 逐帧交叉对照。这些不由本批平面/斜坡检查代替。

主图/真实 Montage/ActionPlayer、Overlay 全部道具、Mantle/Roll/Root Motion、
Ragdoll/Get-up/Pose Recovery/完整 Camera、人工移动与十分钟性能门禁继续按
原 P3–P7 推进。起步滑步、换髋、上身和平台脚锁仍未关闭，音频暂缓。
本批没有修改 UE 插件/资产或已确认键鼠，没有提交、合并或回滚工作树。
