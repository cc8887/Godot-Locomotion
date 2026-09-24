# 原始武器通知编译与候选请求

本批在 `D:/GodotALS` 的 `main` 继续。未改动用户的项目配置、旧计划、分层修改或头颈诊断文件。

## 原始行为与实现

`AlsRefactoredWeaponNotifyProfile` 读取已校验哈希的真实 EventGraph、对应 authored TransitionStart、baked edge 通知索引，以及 AIS_Als_Default 的 Transitions 配置。解析的是 `Transitions`，不是相邻的 `DynamicTransitions`。

| 武器 | 方向 | RelaxedToReady 速率 | ReadyToRelaxed 速率 |
| --- | --- | ---: | ---: |
| Bow | Left | 1.5 | 1.5 |
| Rifle | Left | 1.75 | 1.5 |
| PistolOneHanded | Right | 1.75 | 1.5 |
| PistolTwoHanded | Right | 1.75 | 1.5 |

全部调用的 BlendIn/BlendOut 为 0.2 秒、StartTime 为 0.3 秒、bFromStandingIdleOnly 为 true。Bow 的两个事件连接同一个函数调用节点，其余各自调用，编译器保留并校验双向执行连线和 GetParent 接收者。无引脚注释框不参与逻辑闭包，其他未消费节点拒绝。

本机 `AlsAnimationInstance.cpp` 的 PlayTransitionLeft/RightAnimation 根据 Stance 选择 Settings 中的源；PlayTransitionAnimation 在 Moving 或 Stance 不精确等于 Standing 时退出。因此本批仅绑定真实 Standing Left/Right 源，验证 mesh-space additive、禁用 root motion 和有效起播位置。空 stance 和 Standing 子标签都不通过门控。

`PlayQueuedTransitionAnimation` 使用 `Transition` Slot、LoopCount 1、BlendOutTriggerTime 0。主线程调用会立即尝试播放，因此多个生成通知必须按队列顺序保留；直接在 worker 调用该函数使用的单项覆盖队列是另一条路径，不能混为一谈。

`AlsRefactoredWeaponNotifyRuntime` 将同一个 machine profile 的候选通知解析为带帧身份的有序请求，保留 ordinal 和重复请求；提供 Prepare/ValidateCommit/Commit/Cancel。校验角色、generation、frame 和 profile 引用身份。父姿态门控在准备分发时应用，不能用动画图里混合中的 PoseState 权重代替 Stance。

## 验证

- 新增 12 项通过：四种真实事件图/源参数、四种候选请求事务/门控/身份、四类错误原图拒绝。
- 使用上一批实际 UE 导出的生成类 notifyDefinitions，逐一验证局部索引和名字。单帧 Ready→Relaxed→Ready 的两个请求顺序保留；Bow 两个相同播放参数的请求不合并。
- 站立、蹲伏、空标签、子标签与 moving 组合，取消重试、错误提交、角色/generation/帧/profile 错配均覆盖。
- 相关 Import **71 通过、0 失败、0 跳过**，包含既有新武器原生连续对照与旧 Overlay transition 回归。
- Godot Optimize 构建 **0 警告、0 错误**。
- 首轮 12 项因将 EdGraphNode_Comment 当作未消费逻辑节点而失败，检查原图后修正；首轮和修正后的 TRX 均保留在 `artifacts/refactored-weapon-notify/`。

未新增 UE 函数调用/动态 Montage 的原生运行 oracle，未更改插件、重导数据、启动 UE/Godot 或执行全量/性能/打包验收。

## 剩余工作

本批结果是已绑定原始源和播放参数的候选请求，尚未创建实际动态 Montage。接下来需要绑定 Refactored Transition Slot/组和共享宿主资源 ID，连接物理 Montage 实例的播放/替换/停止，再验证原生 EventGraph 函数消费。bStopTransitionsQueued 与 worker 直接调用的覆盖队列也不属于本批已实现范围。

四种武器的 state 源更新、完整姿态和动作隐藏分支仍待实现，完整 Overlay 姿态仍为 9/13。普通 Demo 未切换；实际移动状态机、统一宿主及 Ragdoll/Flail/Get-up 整体验收等旧缺口继续保留。道具物理、音频和头颈诊断仍暂缓。
