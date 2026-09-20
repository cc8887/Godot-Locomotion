# 转身 Montage 通知与统一候选事件提交（第九十四批）

日期：2026-09-12。工作区 `D:/GodotALS-p5a-events-actions`。
范围：原 P5A 前置能力，继续完整 BaseLayer 接线；不是完整 Demo 或视觉验收。

## 本批完成

- 新增只读 UE 导出 `tools/unreal/export_turn_notify_inputs.py`，正式数据为
  `assets/config/v4_turn_notify_inputs.json`。八个原始 Turn 序列共 26 条瞬时通知，
  包含对象身份、原生触发偏移、阈值、概率、LOD、服务器和 follower 策略。
  当前这些资产只有 queued、class-based instant 通知；导出/编译显式拒绝
  未支持的状态通知，不根据缺失字段填零。
- `AlsTurnNotifyCompiler` 复用原生来源通知校验，与动画集的时间、类型、对象
  归属逐项核对；来源策略下标保持不变，对象/名称采用统一命名空间。
  动态 Turn 的句柄位于既有完整 occurrence 布局之后，同资产多次播放通过
  PlaybackEpoch 保存独立的完整 Montage 身份，不限制为两次播放。
- `AlsTurnNotifyRuntime` 在 Blueprint 请求之前读取已推进的 Montage 遍历，
  中断实例跳过通知，自然结束仍允许收集片尾窗口。先按 NotifyWeight 过滤，
  再按实际 Slot 更新相关性和上一帧相关性合并，保留一帧的退出余量。
- UE AnimInstance 的 Montage 队列与 Proxy 来源队列有独立随机流，本实现
  保留两个状态；最终回调阶段使用一个编号分配器和一个 16 项事件缓冲。
  来源瞬时事件、Montage 瞬时事件、旧 State End、新 State Begin/Tick 按统一
  生命周期处理，不能简单拼接两套已经分发的事件或重新随机筛选。
- BaseLayer 映射入口按实际 Standing/Crouching Idle 更新决定 Slot 相关性；
  Main Movement 可延后事件准备，待 Slot 状态确定后只准备一次统一事件。
  事件、随机历史、Slot 历史、Montage 与最终姿势统一提交/丢弃。
  延后但未完成事件准备的 Main Movement 禁止提交。

## 原生依据

本机 UE 源码：

- `Engine/Source/Runtime/Engine/Private/Animation/AnimMontage.cpp`：
  `FAnimMontageInstance::HandleEvents` 在 bInterrupted 时直接返回；片段通知
  先进入按 Slot 分组的队列，使用 NotifyWeight。
- `Animation/AnimNotifyQueue.cpp`：`AddAnimNotifiesToDest` 完成过滤；
  `ApplyMontageNotifies` 根据 Slot 相关性无二次过滤地追加。
- `Animation/AnimInstanceProxy.cpp`：`UpdateSlotNodeWeight` 按局部 Montage
  权重记录相关性，不以最终全局权重重新筛选；`ClearSlotNodeWeights` 保留
  前帧状态；`IsSlotNodeRelevantForNotifies` 使用当前或前帧相关性。
  PostUpdate 先追加 Proxy 队列，再应用 Montage Slot 队列。
- `Engine/Public/Animation/AnimNotifyQueue.h`：每个队列各自初始化随机流。

这是源码语义和正式数据核对。未新增完整 UE Montage 生命周期/完整 AnimBP
逐帧数值探针，不能写成完整引擎运行时已实现 1:1。

## 验证

- Core 通知、动态 Montage 与队列/生命周期相关测试 112 项通过，包含共享
  回调顺序/分配器、缓冲溢出不发布、独立 RNG、隐藏余量、反向边界、中断、
  同资产不同播放身份、取消重试；预热后队列准备及合并回调各测得 0 托管分配。
- Import 通知与转身专项 69 项通过；最后句柄分配调整后新导入专项 12 项复跑
  通过。30/60/120 Hz 下八个实际片段分别完整播放，每档收齐原生 26 个事件。
- `base_layer_frame_smoke.tscn -- --turn-notify`：八个资产、2400 帧、50 次
  转身回调、58 个有事件帧的晚期 Slot 故障重试通过；包括实际片段骨骼输出、
  曲线与候选事件。58 大于 50 是因为统一队列还含站/蹲切换来源事件。
  日志 `artifacts/turn-notify-pose-coverage.log`。
- 映射入口 3360 帧、12 次晚期故障；旧入口 3360 帧、6 次晚期故障通过。
  日志 `turn-notify-base-layer.log`、`turn-notify-legacy-base.log`。
  该原有场景的转身窗口较短，不能以其总事件数作为八个转身通知的覆盖证据。
- Godot 构建零警告零错误。生产 single/parallel 各 180 帧通过：结果摘要
  `21E164D829153157`、完整姿势摘要 `CF9225D4DE9B2C8B`，来源事件均为 10。
  生产仍走旧 Standing 路径，这证明原入口回归保持，不证明新整图已进 Demo。
- UE 完整 Editor 构建与插件审计通过，记录前缀
  `20260912T010354852Z-9d8ec077de614b5e8ec4e5074b7705d0`，fingerprint
  `075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564`。
  导出和普通 Editor 重启均退出 0；两份通知 JSON 的 SHA256 均为
  `9BA13A498F679E84A818825FB1DB484D45BE329E409E3EAB8791ED2C6FB36C4D`。
  本批未改 UE 插件、配置或资产，不需要新增插件包构建/资产变更验证。
  普通 Editor 仍记录两条既有 AutomationTest Condition failed，不能称无错误启动。

## 保留的失败记录与边界

首轮 Python 无法读取未公开的 TriggerTimeOffset，改读原生结构 export_text；
随后修正 UE Python 枚举需取 value 的转换问题。首轮 C# 存在枚举 Span.Contains
约束和 Slot 字段名错误，均已修正。新增实际片段测试先误在透传 Slot 注入
回调故障，随后将故障轮改为真实非零 Slot 覆盖；又纠正“统一队列只会包含
Turn”这一测试假设，保留并检查普通状态切换来源事件。失败日志没有覆盖。

目前只闭合动态转身的瞬时通知。通用 ActionPlayer 同组互斥、其他 Montage
类型/State/branching point、动态脚部 Transition、Root Motion 和 gameplay
消费者仍按原 P5A/P5C 继续；不能使用本批 Turn 合同静默接受这些情况。
音频没有实施。旧 SourceEvents 属性在映射 BaseLayer 中现在包含统一候选事件，
真正主线程 gameplay 分发仍需随完整 Worker/Demo 接线一起完成。

下一步继续通用动作所有权和原生 Montage 生命周期对照，然后闭合最终曲线
反馈与真实 Worker/Demo 输出。P3/P4 的换髋/交错步、起步滑步、双臂/上身、
脚锁平台问题仍未经过新的移动多帧截图及人工验收；不把后续 Mantle/Ragdoll
作为推迟这些基础视觉修复的理由。最终十分钟性能验收也仍未完成。
