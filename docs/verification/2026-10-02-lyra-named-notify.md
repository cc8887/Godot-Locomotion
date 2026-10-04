# Lyra 命名通知外部接收与实例转发

2026-10-02。继续使用现有 ALS 人物：68 根蒙皮骨、81 根逻辑骨和每角色 14 个 typed Layer 入口的共享组实例。本批接入原 SaveAttack/ResetCombo 命名通知的外部接收器，并对照真实 UE 委托与实例转发；整个 Lyra 移植目标仍 active。

## 原生边界

安装版 UE 5.8 的 `AnimInstance.cpp` 中，`TriggerSingleAnimNotify` 先调用 `HandleNotify`，命名通知随后只广播发送实例的 external delegate，再读取发送实例的 propagate 标志。关闭传播时执行自身类方法；开启时按 Main、Linked、PostProcess 顺序执行允许接收的类方法。外部处理器不会转发到其他实例。

实际 `FSimpleMulticastDelegate` 倒序调用，允许重复注册。广播中的 RemoveAll 只使条目失效，影响本轮后续调用；新增条目不进入本轮。非广播移除与失效条目清理使用 swap remove，会改变后续顺序。弱 owner 失效、嵌套广播与相应清理阈值均按原行为实现。这与原 Emote 移动回调使用的 dynamic multicast snapshot 是不同委托，保持各自语义。

`SkeletalMeshComponent::ForEachAnimInstance` 在 Main 类方法之后复制 Linked 列表，在最后读取 PostProcess。因此 Main 方法移除 Linked 会影响本轮名单，Linked 方法移除另一个 Linked 不改变已有快照；receive 标志仍逐实例读取。

可选 `LyraNamedNotifyOracle` 探针执行真实 `TriggerAnimNotifies`、原 external delegate、实际 Linked roster adapter。实例采用原 `ABP_Mannequin_Base` 生成类或受控签名类；受控类验证 HandleNotify 拦截、零参数方法和单对象参数为 null 的方法。三个原 Fire Montage 提供真实命名通知区间。**受控实例 roster 和方法不是整个原 Main AnimGraph 的运行，也不是原武器 Provider 的连续换层姿态参考。** 原十类通知接收策略仍由此前冻结 policy 校验，默认接收/传播关闭、没有两条命名方法。

两次独立成功 UE 进程都实际退出 0，55 条轨迹、890 帧完整结构相同；原资产与 probe source SHA 保持。连续三Hz/三原资产和重复注册、移除顺序、广播中增删、失效 owner、大小写、嵌套广播、发送实例、传播标志、Main/Linked/PostProcess 修改边界均逐条对照，共 229 次回调。

## Godot 接线

`LyraNamedNotifyRouter` 实现原 delegate 和实例访问顺序。外部 owner 使用弱引用及 typed `ILyraNamedNotifyHandler.Receive`，消息保留角色/帧身份、原名称和原通知引用；类方法通过 typed message 适配原支持的签名种类。它不创建动画时钟，也不把原命名事件改成 GameplayEvent。

`LyraNamedNotifyConsumer` 接同一角色的 Source/Montage 合并队列。在完整 Main、Montage、最终 ALS skin 提交之后，按原 callback 顺序与现有 GameplayEvent/ContextEffects/武器消费者交错分发。Linked playback provenance 保留，但实际 external receiver 是拥有队列的 Main。Prepare、Evaluate、Cancel 和 retry 不调用接收器；注册表在分发时读取，包括 Prepare 之后才注册的处理器。Delivered 统计命名引用数，监听器调用数另计。

装备重绑保留 Main 接收器；旧 Linked 接收器随旧 Layer 退休，新接收器使用真实 Layer epoch 和合同标志。同类复用保持实例。角色退休清空接收器；旧候选、跨角色候选、重复消费与未提交分发被拒绝。回调中重绑在改变 Main/装备之前拒绝，保持角色提交边界。

实际业务可在角色拥有的 `NamedNotifies.Main` 注册 SaveAttack/ResetCombo typed handler，并按 owner 移除。原默认类仍没有这些方法或业务订阅，本批测试使用显式注册的监听器观察原 Montage；没有新增推测的攻击/连击玩法。

## 验证

Debug 与实际 ExportRelease Optimize 的原生回调对照各 55 条/890 帧、890 次取消重试、229 次回调，精确比较全部顺序。实际 ALS/Jolt 六角色用原武器 Fire Montage，站/蹲、初始 Unarmed/Pistol/Rifle、每角色一次真实换层，验证 Main 保留、Linked 退休、重复注册、Prepare 后注册及全部候选消费门禁。

| Hz | 物理帧 | 六角色移动/retry | 原命名通知 | 监听器调用 | 换层 | Prepare 后注册帧 | 拒绝次数 |
|---|---:|---:|---:|---:|---:|---:|---:|
| 30 | 120 | 720 | 40 | 140 | 6 | 40 | 1706 |
| 60 | 240 | 1440 | 40 | 140 | 6 | 40 | 3146 |
| 120 | 480 | 2880 | 40 | 140 | 6 | 40 | 6026 |

两构建的三份完整物理 JSON 和 callback SHA 相同。Debug/实际 Optimize 各 15 个最终验证进程均退出 0、无 Godot ERROR/WARNING，两次最终构建均 0 警告/0 错误。每构建三Hz六角色合计 5040 移动/retry、120 条命名通知、420 次回调、18 次换层。旧 Pivot 36,920 帧原规则/历史对照、三Hz六角色 Pivot、Emote/Warp/root、普通十角色三Hz及 E 输入全部回归通过；两构建的 Pivot/普通十角色/E 完整 JSON 逐对一致，六份 Debug DLL/PDB 已按 SHA 恢复。

另一个实际 OpenGL/RTX 5080 渲染进程退出 0、stderr 为空；240 物理帧报告与 Debug/headless 60Hz 完全相同。第 31/151/211 帧三图已全部查看，均有六个人物，能观察站/蹲和装备更换；人物仍为远景，没有关闭近景握持/足部/材质视觉验收。最终合计 31 个 Godot 验证进程、两个成功独立 UE 参考进程；先前失败与预检不计入通过数量。

最终审核：[named-notify-verification.json](../../artifacts/lyra-analysis/named-notify-verification.json)。可复跑入口为 `scripts/verify-lyra-named-notify.ps1`；`tools/verify_lyra_named_notify.py` 核对冻结资源、编译 probe 镜像、两构建加载程序集、全部最终日志/报告和截图。

## 失败与剩余范围

最初可选模块编译误用了受保护的可变 Linked 列表 API 和 TSharedRef 转换；修为实际公开 adapter 与正确引用后构建通过，失败 package/log 保留。最初 C# 夹具局部变量声明顺序编译失败也保留工具输出，修后构建通过。

独立复采首次失败来自 Python 构造中的 tuple 与读回 JSON list 直接比较；规范化比较对象后复采退出 0，冻结 JSON 未改动。首次实际角色场景发现 Main 类不属于 provider 查询表；从已冻结合同单独读取 Main receive/propagate 后修复。失败的 native/physics 日志保留，不作为成功证据。

未修改 UE 源码、原 uasset、项目描述符或 Config，可选模块已移入任务 artifacts。860 份旧 JSON、709 个原包按 SHA 保护，新增三个 ignored JSON，当前总 863。以前的 frozen verifier 保留历史数量，不为新增资源改写旧验收记录。

只关闭本批原两类命名事件、external typed receiver 与受控实例转发边界。任意嵌套机器 Notify 上下文、剩余 NotifyState 完整副作用、整个 Main 联合连续 native、通用 Warp delegates/Seek、Shotgun/Feminine 全图、多 Group/self/Unlink、复杂地形/近景握持/材质/性能、动画线程并行与跨帧故障范围仍开放。音频、道具物理、头颈专项继续暂缓；没有全量、十分钟或人工完整视觉验收，没有提交或推送。
