# Grounded Slot 加法动态蒙太奇

日期：2026-09-13。第一百二十一批。上一批为已验证的具体进展，本批继续
原 P5A 前置依赖；完整 Godot ALS 目标未完成，没有缩小范围或关闭目标。

## 改变的实际行为

之前 Overlay 状态机通知只能形成候选播放请求。现在组合场景将请求提交
给 BaseLayer 已有的 AlsMontageRuntime，创建真实实例；下一帧冻结求值
数据后，Grounded Slot 从实例位置采样 Transition L/R，叠加到真实移动姿势。
没有新增独立蒙太奇时钟，也没有让当前帧的新请求提前参与当前姿势。

- 新增通用动态序列资产/命令，支持普通、局部加法、网格旋转加法类型。
  加法类型随实例和冻结求值帧传递；旧转身 API 保持非加法语义。
- 从已有 Skeleton SlotGroups 编译出 Grounded Slot=3、Grounded Group=1。
  它与站立/蹲伏转身共享组；新请求沿用现有组中断、旧实例淡出及身份逻辑。
- 按本地 UE `AnimInstanceProxy.cpp` 的 GetSlotWeight，SourceWeight 由
  非加法权重决定；加法权重仍参与总权重及超额归一化。纯加法保持源权重 1。
- 按 SlotEvaluatePose 的普通路径，先混非加法及基础姿势，再按冻结实例
  顺序施加加法。网格加法旋转与局部平移/缩放分别处理；曲线使用附加
  差值并累加，保留缺失与存在为零的区别。此路径不支持 BlendProfile。
- 新精确 Slot 消费器在内部 scratch 完成采样/混合后才写输出。晚期采样
  失败不留下半个调用者姿势。旧转身消费器拒绝误传入的加法求值数据。
- Godot Grounded Slot 适配器使用已验证的 raw source bank；两个资产的
  AdditiveBasePoseType=LocalAnimFrame、RootMotionEnabled=false、
  ForceRootLock=false。实际附加基准由既有精确源采样器计算。

## 验证与证据范围

- Godot 优化 Debug 构建无警告/错误，见 `grounded-additive-build-final.log`。
- 蒙太奇相关 Core 98 项通过，包括原动态/混合动作的 UE 既有生命周期
  夹具，以及新增 6 项：共享组中断、下一帧播放、混合源权重/曲线、
  网格旋转空间、晚期失败与重试、热身后 10,000 次零分配且不推进时间。
- Import 全量 1,950 项通过、1 既有跳过；追加编译约束后，15 项 Overlay
  消费/资产绑定专项再次通过。检查了原生组身份、资产类型和缺失 Slot 拒绝。
- 30/60/120 Hz 组合场景共 1,260 帧通过：3 条状态机通知及 3 次实际请求，
  284 帧加法播放；求值累计观测到 16,655 个骨骼值变化。这只是确认真实
  姿势消费，变化数量不能代替视觉正确性或 UE 全图误差指标。
- 该场景包含 20 次晚期失败、1,260 次取消/同帧重试，比较了移动/Overlay
  姿势、来源、通知请求、物理蒙太奇实例与冻结求值帧。801 次移动 leader
  驱动步枪 follower，42 帧隐藏移动，24 个原资产来源事件。
- 原正式 Worker 单线程/并行各 600 帧通过，各 28 个事件，lag/stale=0；
  result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
  root=`A4F6C26CBAB8A0E7`。日志见 `grounded-additive-worker-*.log`。

未修改 UE 插件/配置、未重新启动 UE。本次使用本地引擎源码与既有原生
夹具；新增加法 Slot 的独立 UE 数值对照仍需完成，不能用原转身生命周期
夹具或解析式旋转测试冒充新的完整加法图原生对照。

## 尚未闭合

两个 Transition 资产各有 2 条通知，清单时间为约 0.795465/1.331635 秒。
本次尚未补入精确触发偏移/策略及正式蒙太奇通知绑定，不能用现有 24 个
来源事件声称这些通知已派发。下一步补采通知和播放元数据，加入原生
混合/中断对照，并将受控组合场景的消费顺序收进正式映射帧 owner。

默认 Demo 尚未启用最终 Aim/Overlay/LayerBlending 组合。全图初始化/缓存
更新遍历、最终曲线、手部/脚部约束及同输入多帧人工验收仍待完成；
P5B–P7、既有 Core 23 项失败和十分钟性能门禁未在本批解决。音频暂缓。
本批没有 commit、revert 或合并。
