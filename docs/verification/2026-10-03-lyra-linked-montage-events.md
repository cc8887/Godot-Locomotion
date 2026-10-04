# Lyra Linked 实例的 Montage 事件排队阶段

后续已完成四容器内核、指定单Section真实生产者和原Emote消费者，24次最终运行、200项Core与独立审计通过，详见 [四类Montage事件](2026-10-03-lyra-montage-delegates.md)。任意回调改bank/重绑、弱对象与销毁、真实Section及NotifyState teardown继续开放；本页的委托容器待办为前批状态，原阶段证据和原探针字节保持。

2026-10-03，继续 ALS 人物和 Animation Interface / Layer 路线。按要求使用本机 UE 5.8 的实际源码与资源，不展开 5.8 / 5.9 差异。最终矩阵和独立审计通过；本批只关闭所述实例事件排队阶段，整个目标保持 active。

## 资源与接口决定

继续使用原 ALS Mannequin 模型、材质、蒙皮和 68 根物理骨。Lyra 动画经 UE IK Retargeter 离线重定向，Godot 运行时保留 69 raw / 81 logical 布局；`weapon_r`、原 ALS 虚拟骨和武器空间左手虚拟骨参与源采样、混合和控制，最终唯一 writer 只发布原 68 根蒙皮骨。`LyraAlsCharacterBinding` 校验骨名、父骨和目标参考布局，关闭模型内额外动画播放器。

ALS 没有 Manny 的 `spine_04/05`。脊柱补偿、骨遮罩、IK 链和武器挂点需要使用 ALS 目标布局；额外逻辑骨不改变网格蒙皮和身体比例。距离曲线、Sync Marker、Notify、additive 基底、typed 属性及 RootMotion 要与动画一起导出。近景握持与变形效果仍需视觉验收。

| UE 概念 | 当前 Godot 对应 |
| --- | --- |
| 普通 Blueprint Interface | typed 角色服务、命令和事件接收器 |
| Animation Layer Interface | 原编译签名合同与生成 C# 参数/姿态包装；Aiming 两参数保留 double |
| Linked Anim Layer 调用节点 | 保留节点身份、输入姿态、参数、权重、相关性及调用顺序 |
| Layer Group | 按实现类和组建立实际有状态实例；无组按调用节点分配 |
| Provider 实例 | 独立机器、source occurrence、缓存、回调字段、反馈与本地 Montage bank |
| Sync Group | 各实际访问源进入角色共同批次，一次同步后再求值 |
| Layered Blend / additive | 按目标骨遮罩和原空间、基底、曲线/属性/root 合成姿态 |

当前 Unarmed/Pistol/Rifle 十四入口已接实际图宿主。原 `ItemAnimLayers` 是一组一个实例；已验证的四种布局为 1/3/4/14 个实际实例。完整姿态包携带 local/component 骨姿态、曲线存在性、typed 属性和 root。Update 与 Evaluate 分开，最后角色统一验证、提交和发布；取消丢弃全部候选。同类 Link 保留实例，换类退休旧实例并从原 CDO 初始化。普通 Main 最终曲线反馈给 Linked 实例，本地 Montage 阶段不复制 Main。

## 原生生命周期依据

安装版 `Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp`：

- `UpdateMontage` 561 行：使用 Main Montage evaluation data 的实例直接返回，保留自身既有阶段。
- `Montage_Advance` 2272 行：先设置 `bQueueMontageEvents=true`，再检查空 bank；没有动画根访问不免除该更新。
- `DispatchQueuedAnimEvents` 947 行：资源 Notify 先执行，再处理 Montage 委托。
- `TriggerQueuedMontageEvents` 2576 行：先将阶段设为 false，再依次派发 BlendingOut、BlendedIn、SectionChanged、Ended。

`Components/SkeletalMeshComponent.cpp` 2114 行复制 Linked 实例列表，再按 Linked → Main → PostProcess 调用原实例派发。3369 行说明跳过 Evaluate 的帧也要派发。本套角色没有额外 PostProcess 实例。

原整 Main 探针在 Before / Updated / After 三边界记录后，只明确调用 Main 派发。其 Linked 阶段会跨帧保持 true；它与普通组件路径不同。本批保留原探针字节，新增独立探针副本，只在原 Main 派发位置插入组件同序的原实例调用及派发后快照；图执行、更新、采样和阈值保持原样。它是受控组件顺序参考，未声称由完整 UE 世界 tick 驱动。

## 实现范围

`AlsMontageRuntime` 的候选 bank 与已提交 bank 分别持有事件排队阶段。Begin 进入候选阶段，即使 bank 为空；Commit 发布，Discard 保留既有阶段。真正派发先清除已提交阶段，再调用现有消费者；pending bank 禁止派发。bank 生命周期清理不是事件派发，不隐式清除阶段。

每个 `LyraItemLayerGraphInstance` 建立自身实际空 bank：本套 Provider 没有本地 Montage 播放入口或 Slot。Main 每物理帧更新全部实例的 bank，与 worker 首次访问和姿态根权重分别处理。实例 bank 与图、反馈和源候选共同验证、提交、取消；退役、外部 owner 和迟到帧不能派发。

普通 `LyraCharacterAnimation.Commit` 在完整角色/模型发布后先派发全部 Linked，再按既有 callback 顺序执行 Main 资源通知，最后结束 Main 排队阶段并调用现有 Emote Montage 消费者。诊断路径消费作者输入明确选择组件派发或原 Main-only 派发；参考输出仅用于断言，不能作为运行状态输入。

## 新原生参考与验证

新增三 Provider × single/per-call 六轨迹，每条 30Hz、12 秒、360 帧，共 2160 帧。0–4 秒和 5–12 秒使用组件顺序，4–5 秒只派发 Main；5 秒在身体源仍隐藏时恢复 Linked 派发。四个播放/停止命令覆盖武器动作、全身动作交叠和隐藏，每 37 帧同类 Link 一次。原生进程实际退出0，资产保存0。

原始快照表明：Main Before=false、Updated/After=true、Dispatched=false。Linked 在组件路径更新后为 true、派发后为 false；Main-only 窗口保持 true 到下一帧，并在恢复组件派发时清除。未访问 owner 也有同样阶段。原 ALS 资源和原图不需要改写。

冻结49份源码后，Debug 和实际 ExportRelease 构建均零错误、零警告。两配置最终28次 Godot 运行均实际退出0，无 Godot ERROR/WARNING；每配置14次：

| 检查 | 每配置次数 | 范围 |
| --- | ---: | --- |
| 新派发阶段参考 | 4 | single/per-call，pre-rig 与最终 Rig |
| 原 Main-only 布局回归 | 2 | three-groups/mixed，pre-rig |
| 原三频回归 | 3 | 30/60/120Hz，十四独立实例，pre-rig |
| 原长待机/转身/左手设置 | 1 | 十四实例，pre-rig |
| 普通十角色阶段检查 | 1 | 60Hz、十四实例，换类、开火/换弹、取消重试 |
| 普通 E 动作阶段检查 | 1 | 60Hz、十四实例，与换类组合 |
| 原 Emote 回归 | 1 | 54轨迹、27720帧 |
| 原 ALS 入口回归 | 1 | 60Hz、1700帧、Pivot/Rest反馈 |

原生矩阵合计32400提交帧及同数逐帧取消重试。新增 Linked 阶段1825200次、Main阶段170640次比较；原八 worker、三预更新、九移动、十二图字段和左手设置，以及完整姿态/曲线/属性/root 原门禁同时执行。新原生六轨迹只有30Hz，另外两频采用原参考回归，不能表述为新参考三频采集。

普通入口合计10560角色帧，玩家1920次取消重试；拒绝pending派发57600次、外部owner147840次、迟到帧147840次、退役实例336次。两个构建的完整报告逐项相同；移除新增检查包装后的默认十角色完整报告与前批相同。两个普通 E 检查均有1激活/1结束/1运动取消。Core Release定向测试56项通过，含空bank/零delta/三频、取消和实例独立性。

`tools/verify_lyra_linked_montage_events.py` 独立核验原/新探针逐字哈希与唯一差异区域、原图/更新不变、六原始分片、作者输入、派发顺序、Main-only保留与隐藏owner、最终日志/计数、源码/实际程序集及报告。869份既有 JSON、710个原UE包和9项项目配置保持；七轮六文件 Debug 恢复通过。结果为 `artifacts/lyra-analysis/linked-montage-events-v1-integrity.json`，`auditPassed=true`，未放宽精度门槛。

初期命名空间编译失败日志保留。原生预审首次误要求single布局有未访问owner：该实例经后处理始终被访问，改为per-call验证未访问owner，single保留访问事实；没有修改运行逻辑。另一次Debug阶段预审发生于Optimize临时替换程序集期间，当前目录哈希因此失败；最终审计在全部恢复后执行并通过。UE模块构建保留原源码的C4996弃用警告，成功原生采集保留原资源加载/依赖警告；Godot最终运行和两次最终C#构建无错误警告。

## 仍开放的边界

本批只新增 `bQueueMontageEvents` 明确检查，私有字段范围从 33 扩至 34/47；其余十三字段包括另外七项图配置、RootMotionMode 和五项引擎标志。本地 bank 为空是当前原资源事实，不是通用 Montage 实现的完成条件。

通用四类 Montage 委托容器、回调中再次播放/停止/重绑，以及非空 bank 权重更新期间的精确入队时点仍待实现和原生验证；本批不宣称完整 Montage 事件派发。`UseMainMontageData=true` 保留跳过自身更新的分支，但三个原 Provider 均为 false，尚无该模式变化轨迹验收。

非空左手 Sequence、default/self/Unlink/部分覆盖整图、同函数多个调用节点、其它 Provider/拓扑和共享持久实例继续开放。完整 Chaos/Jolt 同输入运动原有 314/1680 帧差异未由本批复测或关闭。复杂地形、近景握持、独立游戏导出、跨平台、性能、GPU专项和十分钟测试未验收；音频、道具物理、头颈专项仍暂缓，整个目标保持 active。
