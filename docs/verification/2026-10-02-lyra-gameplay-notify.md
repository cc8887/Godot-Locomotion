# Lyra 原通知到角色 GameplayEvent

2026-10-02，继续在主目录推进 ALS 人物与完整 Lyra 移植。本批将原 AN_Melee / AN_Reload 的通知实际送到 Godot 角色事件接收器，保持原标签、默认载荷和角色归属。其他消费者、完整 Main 连续原生、RootMotion 碰撞、武器/地形和性能继续开放。

## 原类行为及实现

原 `AN_Melee.Received_Notify` 向 `MeshComp.GetOwner()` 发送 `GameplayEvent.MeleeHit`；`AN_Reload` 发送 `GameplayEvent.ReloadDone`，两者返回 false。该返回值不会阻止已经发送的事件。原左右 FootPlant 的 Received 回调没有副作用；本批不会根据类名添加锁脚、改曲线或写骨骼。TransitionToLocomotion 的空回调与活跃 State 查询是不同边界，后者继续开放。

UE `UAbilitySystemBlueprintLibrary::SendGameplayEventToActor` 将标签与 `FGameplayEventData` 分开传给角色 ASC。原两个 BP 没有连接 payload，所以载荷的 EventTag 保持无效，原生 `ToString()` 为 `None`，Magnitude 为 0，Instigator/Target/OptionalObject/Context/tags/TargetData 均为空。Godot 保留这项默认语义，动画来源和 epoch 仅留在通知 provenance。

新增 `LyraGameplayNotifyConsumer` 从既有统一队列的真实 instant callback 构建 typed 命令。Prepare/Evaluate/Cancel 不执行玩法；角色预校验通知及接收器身份，Main、bank、真实碰撞及 skin 提交完成后，才发送 Godot 信号。候选先标记已消费再调用信号，拒绝重复/重入发送；每个事件重新查找该角色的接收器，匹配原逐次 SendGameplayEventToActor 查询边界。

`LyraGameplayEventComponent` 是角色直接子节点，发出 `GameplayEventReceived(eventTag, payloadEventTag, magnitude)`，供玩法订阅。普通角色与六角色实际实例均已挂载。没有接收器时记录 MissingReceiverEvents，不投递给其他角色；这个缺接收器诊断计数不等同于 UE 的 ASC 缺失错误日志。伤害、弹药和 GAS 技能激活由玩法系统提供，本批验证到事件接收器。ContextEffects、武器 mesh Montage、MotionWarping、命名事件及其他 State 消费仍由完整目标继续推进。

## 原对象执行与验证

独立 `tools/unreal/LyraGameplayNotifyOracle` 在真实初始化临时 PIE World 中创建两个角色及注册后的 ASC，直接调用真实原 Sequence/Montage Notify UObject 的原生成 Blueprint Received_Notify，监听原 ASC GameplayEvent delegate。第三个无 owner 的 Mesh 验证无投递。没有替换 Notify 类、复制 BP 算法或保存资产。

747 个原对象：3 Melee、6 Reload、360 左 FootPlant、378 右 FootPlant。每对象两角色重复各两次，加一次无 owner，共 3,735 调用；实际 GameplayEvent 36 条，其余回调无副作用，所有返回值 false。两独立最终 UE 进程退出 0、结构内容相同，676 包及 824 旧 JSON 字节哈希保持。原生资产 `gameplay_notify_dispatch_v1_native.json` 为 1,641,101 字节，SHA256 `bef0186de0affba85812b3083c35ceeb6d242b29bf3ec54bd5594ecfe43809e8`。

Godot 窗口/队列/typed 接收器测试按同一原对象库存运行：3,735 次取消重试、18,750 次失效/跨角色/重复拒绝、36 次迟到接收器变化及 36 次信号重入拒绝。输出 36 条信号与原生标签/载荷/调用顺序/角色精确一致。此受控库存验证独立于真实 Main；无 owner 原生用例与 Godot 缺接收器用例仅验证无投递结果，不扩大为错误日志等价。

Debug / Optimize 各生产六角色三 Hz 已通过：各共 10,080 角色帧，每 Hz 24 换类/2 重建，真实 Pistol Melee 和 Reload 各投递 2 条信号，每配置共 12 条；信号监听时断言 Main 队列、物理 bank 与 skin 已发布，取消后的完整历史和双角色输出相同。两配置各普通十角色 4,800 发布帧及旧 Montage 33,235 帧、Source 8,042 帧原生队列回归已通过。普通十角色没有 Montage 动作输入，事件消费者接入标志不代表该夹具覆盖 Melee/Reload。

两配置的库存窗口测试均逐项通过原生；三份完整角色报告及普通十角色完整报告在 Debug / Optimize 间逐值一致。两次构建 0 警告 0 错误、两个七进程矩阵均退出 0 且无 Godot ERROR/WARNING，六个 Debug DLL/PDB 已逐文件哈希恢复。最终 `tools/verify_lyra_gameplay_notify.py` 实际退出 0，汇总 `artifacts/lyra-analysis/lyra-gameplay-notify-verification.json`；再次核对 824 旧 JSON、676 当前包及探针源/实际打包源，均通过。Core 算法没有修改，本批未重复上批 124 项 Core 门禁。

## 保留的失败及范围

首轮 UE 探针直接初始化未注册 ASC，AbilityActorInfo 尚未由 OnRegister 分配，引发断言；补真实 RegisterComponent 后独立重建为 package-second。随后原生精确断言发现默认 GameplayTag 文本是 `None`，修正 Godot/预期表示，未改原资产或原生结果。首轮 smoke 构建的匿名报告字段重名已修。全部失败日志和两轮插件包保留。

原生范围是上述类的真实 BP 函数及 ASC 事件投递，不是完整 AnimInstance 连续 TriggerAnimNotifies、NotifyState BP Begin/Tick/End、完整技能激活或完整 Main 原生验收。本批没有修改 Engine/UE 资产，没有新渲染、性能或全量验收。音频、道具物理和头颈暂缓项保持。

复跑入口：`scripts/export-lyra-gameplay-notify.ps1`、`scripts/verify-lyra-gameplay-notify.ps1 -Configuration Debug|Optimize`；证据文件和原生结果使用排他创建，勿覆盖已有日志、报告和哈希依赖 JSON。
