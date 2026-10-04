# Lyra 原 Montage 与 Main Slot 更新

2026-10-01，在主目录实施，UE 5.8.1 外部 exporter / Godot 4.7.2 .NET。继续使用 ALS 人物路线。本批完成原 Montage 资源合同、一个物理实例的多槽快照及五个原 Slot 的源更新语义。**尚未把动作姿态混入 Main，也未接普通 Demo；整个 Lyra 移植继续开放。**

## 原资产与必要支持

只读加载 Main 的五个命名槽所引用的 45 个 Montage、55 个 Sequence；共 60 条槽轨道，其中 15 个资产同时驱动 UpperBody 和 UpperBodyAdditive。全部实际资产为单 Section、每槽单段、一次循环，Section 从0开始且无下一节。原 DefaultGroup / AdditiveGroup 分别包含32/13资产。

导出保留段时间与范围、RateScale、additive 类型、Section、槽组、混入/混出曲线与模式、BlendProfile、自动混出/触发时间、Marker、曲线、Notify 和 RootMotion设置。实际资产使用原线性/Cubic/Hermite三种混合选项，BlendMode均为标准模式；六个资产持有 FastFeet 混出 Profile，四个资产有根运动，旧式 root translation/rotation 两个标志均关闭，SyncGroup均为None。Profile是后续姿态求值的必要数据，不能由全局 Montage 权重验证视为已实现。

Main 的槽节点仍为 UpperBody81、UpperBodyAdditive71、FullBodyAdditivePreAim2、AdditiveHitReact74、FullBody84；AlwaysUpdateSource均为false。Lyra银行使用自己的五个名字映射，未改 ALS 十三个槽的全局约定，两个角色运行时不会共用同一个可变 bank。

新 ignored 资源为 montage_catalog_v1/v2.json、montage_slots_v1/v2_requests.json 与对应 native.json，共六份。v2补充旧式RootMotion标志和可选组内替换命令；v1原字节保留。最终v2捕获保护609个原资产路径、697个既有JSON；没有保存/修改原UE资源或Engine源码。

## 运行时实现

AlsMontageRuntime 的 authored asset/物理实例新增不可变 AdditionalTracks。每个实例只做一次时钟、混合、组仲裁与取消提交，冻结求值快照向每条轨道提供同一实例身份和权重。StopSlots从第二条轨道找到并停止整个实例；重复槽、非法段参数和别名轨道冲突拒绝。同一Montage别名按完整轨道内容校验，允许不同不可变数组持有相同内容。

LyraMontageCatalog按原名字和metadata构造全部45个定义，保留完整JSON元数据供后续姿态/通知消费者；拒绝未经支持的资产形状、模式、旧式根运动排他标志与Montage同步。未将双槽资产拆成两个播放实例。

LyraMainSlotUpdateOwner绑定指定MontageRuntime，只读取其冻结帧。初始化清零SourceWeight，隐藏节点保留上一已提交权重；实际访问时复用现有AlsSlotSourceUpdate规则，保留SourceWeight、SlotWeight、TotalWeight及子图完整上下文。非additive权重影响源权重，additive槽仍保留基底；源权重减小或槽满权重时按原CVar标记inactive。槽节点无独立时钟；五槽历史随角色候选提交/取消。

这是可供实际Main接入的更新消费者，当前MainPoseHost仍使用inactive槽边界。AdditionalTracks新增的是求值轨道快照；现有Montage的整实例Traversal/NotifyTraversal尚未扩展为Lyra全部逐轨道资源通知消费者，不能据此宣称统一Notify或根运动物理消费已完成。

## UE 原生与 Godot 连续对照

外部探针创建临时GamePreview角色/注册组件，装载原Main和Manny网格，关闭PostProcess与初始pose tick。调用真实Montage_UpdateWeight、Montage_Advance、UpdateMontageEvaluationData及Proxy.PreUpdate；对原五Slot调用真实Update_AnyThread。Slot.Source临时改为记录实际更新上下文的叶节点，未计算姿态。Play/Stop命令在冻结快照之后执行，新实例下一帧才推进。

两套各30/60/120Hz、各13440帧：

| 数据 | v1允许同组叠加 | v2组内替换与叠加 |
|---|---:|---:|
| 槽权重行 | 67200 | 67200 |
| 实际源Update | 57256 | 58919 |
| 物理实例检查 | 19323 | 13932 |
| 双轨道实例求值帧 | 5161 | 3038 |
| hidden帧 | 569 | 569 |
| full槽行 | 12588 | 9892 |
| 总权重超过1的行 | 5387 | 962 |

45个资产全部实际Play，包含非零起始、不同rate、自然结束、重播、隐藏、重初始化、不同父权重/RootMotion modifier和inactive路径。实例位置、Playing、混合权重、Slot三个权重及源上下文Weight/RootModifier/Active均逐位同，未增加误差阈值。每帧在执行命令后完整取消并重试，保持实例序号、时钟和槽历史；每套13440次retry/旧视图拒绝及1514次错误bank/角色/代际/重复节点调用拒绝。

v1已重复UE采集，两次实际退出0；v2目录和轨迹均重复UE采集，语义数据严格相同，实际退出0。当前probe源码与source/package镜像SHA一致。日志见 artifacts/lyra-analysis/lyra-montage-{catalog,slots}-v2-ue*.log。项目既有GameplayTag/Editor警告仍存在；最终成功采集没有Python/ensure/assert错误。

## 回归与失败记录

- Core相关194项全部通过，其中新增3项覆盖双槽停止、20实例/40轨道容量增长、别名内容及冲突；原191项Montage/Slot相关测试保持通过。
- 当前MainPoseHost实际最终反馈回归11340帧/9762姿态、174重复Evaluate、207晚期取消、129查询故障及45反馈故障、1578仅更新帧通过；该宿主仍inactiveSlots=true。
- 最终Debug和ExportRelease Optimize构建均0错误0警告；新Python/PowerShell语法通过。
- 完整验证入口为 python tools/verify_lyra_montage_slots.py；结果写入 artifacts/lyra-analysis/lyra-montage-slots-verification.json。

首次目录导出因禁用AnimationData，加载原MF_Emote_FingerGuns时因数据模型缺失assert退出3；仅在这两条新导出wrapper恢复插件依赖，保留原失败日志 lyra-montage-catalog-ue.log。首次smoke构建的FileAccess歧义/uint类型错误已修，失败日志保留。首次native对照在30Hz/203帧发现采集Montage_Play(false)而Godot默认stopGroup=true，改为消费实际命令后v1通过；随后新增v2明确控制该参数，补验证默认组内替换。原native数据、公式和门禁未放宽。

## 继续推进

现有logical_controls/locomotion_extras目标库没有这55条动作序列。下一步按原additive基底闭包完成Manny→ALS目标资源、weapon/VB逻辑通道、曲线/typed属性与Notify绑定，导出原FastFeet目标骨映射；再完成全通道SlotEvaluatePose及Main真实访问顺序/缓存上下文，接同一Main角色事务。原source/动作通知队列、RootMotion物理消费、主惯性/最终ControlRig、换类与普通Demo/渲染/性能及完整联合native验收均保持开放。本批无新Godot渲染、真实碰撞或人工观感验收。
