# Overlay 状态机通知消费者与完整性依赖

日期：2026-09-13。第一百二十批。直接对照本地 ALS V4 AnimBP 与当前 UE 源码。

## 原图证据与本次实现

- `v4_overlay_transition_inputs.json` 来自新 UE 原生读取，包含生成通知 8–15、
  CanOverlayTransition、PlayTransition 包装函数及 EventGraph。没有保存 UE 资产。
- 新编译器把八条原生编号/名称与对应机器、转换边、事件入口逐一绑定，
  校验执行线、布尔输入、枚举、结构体参数到 PlaySlotAnimationAsDynamicMontage
  的连接。编译输入绑定 layering/overlay 文件摘要和动画集定义。
- 门控是 Stance=Standing AND NOT ShouldMove；与状态机规则中的 IsMoving
  是两个不同输入。只在派发时决定是否生成请求，不提前修改输入或播放时间。
- 步枪与弓使用 Transition_L，单双手手枪使用 Transition_R。淡入/淡出均
  0.2 秒、起点 0.3 秒；弓双向倍率 1.5，步枪/手枪放松→就绪 1.75、反向 1.5。
  Grounded Slot，单次循环，BlendOutTriggerTime=0，均来自图中实际参数。
- 两个 Transition 资产 AdditiveType=2（Mesh Space Additive）。新绑定保留
  类型，不能接进目前只支持非加法段的蒙太奇路径并声称效果已经一致。
- 新运行时生成带帧身份、队列次序和机器/边绑定的候选播放请求。UE
  `FAnimInstanceProxy::AddAnimNotifyFromGeneratedClass` 使用
  `FAnimNotifyQueue::AddAnimNotify`，直接 Add，不套用资产通知权重/概率
  过滤，也不对重复的瞬时通知去重。本次保留这一行为。
- 组合移动/Overlay 场景的 QueueTransitionNotify 不再为空实现；图求值成功
  后解析门控，失败时与来源、姿势候选一起取消，同帧重试比较请求内容/次序。

## 验证

- 整项目 UE Editor 构建及插件依赖/产物审计通过；BuildId
  `a4192a27-77ab-4bb5-9996-c71b2d55f53d`，未修改原生 C++ 或插件配置。
- 冷启动命令行与普通 Editor 均成功导出 3 个图、8 个通知并退出 0。
  两份 JSON 字节摘要不同，分别为
  `19127A6EE0F690E5017F683B6FC7D4F13DF2F5E2FBD0368487D48D7E1AD68141` 和
  `6170EB3AAB8193B72E51931CDC26F01BA383CDC28E92DA70808485ABE2FAAEBC`；
  独立编译比较得到的八条消费者完全一致，不声称文件逐字节一致。
- 新增 14 项专项通过：实际消费者参数、五类变更拒绝、四种站姿/移动门控、
  身份/重复通知/重试、普通 Editor 重复、原生状态机逐帧通知顺序及
  热身后 10,000 帧零分配。原生轨迹复用既有夹具，不是新最终图 UE 对照。
- 全量 Import 在最初 11 项新增测试时为 1,946 通过、1 既有跳过；随后新增
  三项和已有十一项一起专项通过。没有以此声称整个 Core 测试集通过。
- Godot 优化 Debug 构建通过，无警告/错误。正式 Worker 单线程和并行各
  600 帧通过，result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
  root=`A4F6C26CBAB8A0E7`，各 28 个事件，lag/stale=0。
- 最终组合场景在 30/60/120 Hz 共 1,260 帧通过：801 次移动 leader 驱动
  步枪 follower 观测、42 帧隐藏移动、24 个资产来源事件、3 个状态机通知
  和 3 个通过门控的播放请求。20 次晚期失败（包括通知生成帧）及每帧
  取消/重试均通过；日志为 `overlay-transition-shared-frame-input.log`。
  场景新增了站立瞄准窗口，因此事件/来源计数不与上一批强行保持相同。

相关日志位于 `artifacts/overlay-transition-*`。原始失败日志保留：首次导出
使用了错误的返回字段名；编译时拒绝非零加法类型揭示了实际依赖；组合
场景最初未触发通知，随后新增瞄准输入时需同步设置姿势 Aiming 标记。
这些均不得解释为已经修复视觉问题。

## 尚未完成与下一步

本次只完成原图事件到候选播放请求。还未实现 Grounded Slot 加法蒙太奇
的物理播放/同步/通知/姿势消费，以及它与最终上身组合的完整帧事务。
默认 Demo 仍使用原 BaseLayer 生产路径，不能据此声称双臂、换髋、起步
滑步已修复。下一步先闭合此 P5A 依赖，再继续原 P4 最终图和脚部验收。

完整规划中的 P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、P6
Ragdoll/Get-up/Pose Recovery/完整 Camera 与 P7 十分钟性能仍保留；
既有 Core 23 项失败和性能预算未通过项未在本次解决。没有 commit/revert。
