# PoseMoving 的实际移动缓存接入

日期：2026-09-13，第一百七十二批。原 P4 Refactored 脚部输入补完。

## 生产改动

`--refactored-movement-curves` 在实际动画图构造时启用两个已编译的原生
ModifyCurve 生产点。当前默认 Demo 不开启此选项；没有切换新脚部 Rig。

- Standing：在 V4 Locomotion/Detail 缓存结果上写 PoseMoving=1，之后由
  Standing 的 Moving/Stop 消费，再经过 Standing、站蹲、Grounded Slot、
  Main Movement、BaseLayer、上层分层和最终根。原 V4 合同明确 MovingRead
  和 Stop 的相关 reads 指向 DetailBinding.CacheNodeIndex。
- Crouching：在 Crouching Cycle 缓存内、Lean 等求值之后写 PoseMoving=1，
  再由 Crouching Moving/Stop 和父状态混合。
- Standalone raw、缓存求值的 float/precise 两条入口及 uncached 对照路径
  同时保持对应写入位置。没有从最终速度或 ShouldMove 推导 PoseMoving。
- 新增按 site 绑定的曲线生产器。移动缓存只增加 PoseMoving；不为了满足
  全局布局而把 PoseGrounded/PoseInAir/FootLeftIk 等插成存在的零值。
- 最终 production adapter 在完整根求值后读取该曲线，随 Commit 更新诊断
  快照；候选数据不会提前发布。测试逐帧记录 presence/value 的摘要。

这是明确的跨版本输入适配：保留当前 V4 的状态/缓存图并加入 Refactored
Movement 曲线生产点，不声称两个版本的完整状态图等价。其余 6 个生产点
尚未接入，Refactored 的脚部 Rig、历史 PoseState 与物理查询也尚未接生产。

## 验证

37 项相关 Import 测试通过，含按缓存布局绑定、误用别的 site 拒绝、原 8 点
配置合同、294 组原生骨盆 getter、1440 帧原生腿部组合及事务专项。
结果：`artifacts/tests/refactored-movement-curves-172.trx`。

Main Movement + BaseLayer 真实动画源回放，30/60/120 Hz、冷空中/地面入口
共 3360 帧通过；覆盖站蹲、起停、Jump/Fall/Land、Slot、惯性化及晚期失败
重试。新通道出现 1224 帧，蹲伏 210 帧，中间权重 450 帧，速度归零但停止
姿势仍有权重 126 帧；1338 帧纯空中输出没有未访问移动缓存的曲线。
这些数字不代表交错步的人工视觉验收。

原入口同样通过 3360 帧。新通道专项另加了 6.0–6.6 秒停止刺激，所以两次
回放的事件数量和惯性化次数不作相等断言。日志分别为
`artifacts/refactored-movement-curves-172-main.log`、`-original.log`。

完整生产 Worker/主线程提交 single 与 parallel 各 960 帧通过，新通道均有
478 个正值帧；最终骨骼、角色根与原生产摘要一致：result DB9FEFC95ADA4B15，
pose EFEF274D127B1A99，full_pose 765E1669B4501131，root 3C8B520C47ECA5C7。
曲线逐帧 presence/value 摘要均为 AAB0325E448AAC0C；最终复核记录使用
`artifacts/refactored-movement-curves-172-production-{single,parallel}-verified.log`。
旧脚部链仍未消费新通道，因此这里的姿势不变是回归证据，不是视觉修复证据。

首次生产测试因 P3B 参数白名单未接新开关而在初始化退出，修正白名单后
再次通过。没有放宽动作/姿势门槛。Godot 优化 Debug 构建零警告、零错误。

## 下一步的版本合同

本地 Refactored `AlsAnimationInstance.cpp:1142` 与当前
`AlsLandPredictionModel.cs` 证明空中预测不能直接复用：

| 条件 | 当前 V4 | Refactored |
| --- | --- | --- |
| 开始预测 | 垂直速度 < -2 m/s | <= -2 m/s |
| 扫掠距离映射 | [0,-40] m/s → [.5,20] m | [-2,-40] m/s → [1.5,20] m，另乘 LocomotionScale |
| 屏蔽曲线 | Mask_LandPrediction，不钳制 Lerp Alpha | GroundPredictionBlock 钳制 0..1；allowance <= 1e-4 不查询 |
| 初始穿透 | 拒绝 | blocking 且法线可行走时仍接受 |
| 物理参数 | 当前 Motor 掩码/胶囊路径 | InAir 设置中的 channel/responses 与世界胶囊尺寸 |
| 响应曲线 | V4 导出曲线 | Settings.InAir.GroundPredictionAmountCurve，尚待正式导出/绑定 |

继续补这个独立输入及其正确读取时点，再接 Grounded/Jump/Fall/Land 的其余
写入点、缓存 PoseState、完整 Rig 和生产查询调度。第 616 帧旧失败仍开放；
原 P3/P4 完整角色验收与 P5A–P7 未关闭，音频暂缓。
