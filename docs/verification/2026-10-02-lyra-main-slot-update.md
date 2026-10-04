# 活动 Slot 与 Main 延迟缓存的联合更新

2026-10-02，当前主目录、安装版 UE 5.8.1 与 Godot 4.7.2 mono。完成五个活动 Slot 的联合 Update 遍历，并接入可复用 Main locomotion 宿主的可选更新路径。资源仍为 ALS logical81/skin68；普通 Demo 和 MainPoseHost 的活动 Slot 姿态接线继续开放。

## 实现与原时序

新增 `LyraMainSlotsTraversal`，复用现有 `LyraMainSlotUpdateOwner` 和同一个 `AlsMontageRuntime.Frame`。没有第二套 Montage 时钟、动画资源或源更新权重历史。

- FullBody84 的真实 SourceContext 控制 Recovery76 的两支；全覆盖时，HitReact、Aiming、Recovery 和下游缓存均不访问。
- HitReact74 的 SourceContext 单独控制 Aiming6；Recovery additive 的更新权重来自 FullBody source×0.65f，不能取 HitReact 的 source 或未经 Slot 处理的外层权重。
- 原 Provider BasePose78→Main Split78→Locomotion83 延迟队列保持。Main Split 的选中完整上下文才进入 PreAim2；不得先按各 Aiming reader 分别更新 Slot。
- Split0 上身先于 base。UpperBody81 继承 root modifier0；base保留原 modifier，UpperBodyAdditive71 按原 Dynamic ApplyAdditive3 alpha访问，零alpha不更新该槽或 additive reference79。
- 每个 Slot 的 SourceWeight、SlotWeight、TotalWeight 在同一角色候选内更新，隐藏节点保存上次历史；显式 Initialize 清除五槽候选历史。全角色取消撤销候选，下次重放不改变物理实例、时钟或选主上下文。

`LyraMainPoseCacheTraversal` 新增活动槽入口，旧已验证的显式 source 权重路径继续使用原行为。`LyraMainLocomotionHost` 可在构造时绑定物理 Montage runtime，Prepare 时传入冻结 frame；真实 Aiming 与 Additives 源权重、LocomotionSM relevance 和十根 source visits 都消费活动槽结果。无 Locomotion cache update 时不更新机器。SkeletalControls 位于 FullBody 外层，继续使用外层 visited 状态；图前 Main/Provider 权重观察仍执行。

物理 bank 与 Main 必须共同预校验，Main/Slot 提交在物理 bank 提交之前；取消需同时取消 Main 与 bank。当前宿主内部 identity 仍沿用既有 frame/character0/generation1；不将此入口声称为已经完成多角色生产身份绑定。

## 原生联合探针

新增 `AlsLyraMainSlotsLibrary`、`export_lyra_main_slots.py` 和外部插件包装脚本。临时 GamePreview 世界中登记真实组件，原 Main class 的五个 Slot81/71/2/74/84、ApplyAdditive3/76、LayeredBoneBlend0 和 Save/UseCachedPose 节点实际执行 Initialize/CacheBones/Update。

Provider 使用原 Unarmed/Pistol/Rifle class 的 BasePose78 及 reader76/75。Aiming 分支权重是明确的受控输入（0/0.5/1）；输入 Locomotion、Recovery 和 additive reference 为记录 Update 的受控 leaf。上身 blend mask 按原骨名映射到 ALS81。动态 additive 执行原编译 exposed handler，从原 Main double 成员读取后量化。跳过缓存上下文通过真实 Engine message 分派记录。

Montage 原45资产/55条 ALS81 Sequence/60轨道、原组名与 FastFeet transient 适配沿用已验证的采样前置；每帧只执行一次物理权重/advance/freeze。槽权重记录的是原节点内部 WeightData，包含未访问节点的实际历史，不能用本帧 Proxy.GetSlotWeight 查询冒充。

三种 Provider 复制原完整三频率轨迹：40,320正常物理帧，加7,290零delta/反向/时间跳变等计算边界，共47,610帧。原Aiming内部实际播放器、LocomotionSM内部状态、完整 Linked Layer source时序、Sync 以及 Evaluate 不属于这个原生探针；它验证的是 Slot/算子/cache 联合 Update 闭包。

两个独立 UE 进程退出0，第二次完整 JSON 语义与首次相同。每份日志原记录共800条 Warning、0条 Error，主要为已有 GameplayTag、Editor 和 transient压缩依赖警告；未把它称为UE零警告。原664包、770既有 JSON 字节SHA保持，新旧Slot/采样/混合探针及 source/package镜像哈希一致。未保存 uasset、修改 Engine 或部署 GASP58 插件。

## 验证结果

| 范围 | 结果 |
| --- | --- |
| 联合原节点 Update | 47,610帧、580,701次有序分派，weight/root modifier/active/shared逐位同 |
| 缓存与源覆盖 | 42,444个跳过上下文；10,431帧 visited Main 无 Locomotion 更新；1,107隐藏帧 |
| 分支与历史 | 29,013次 inactive source；36,072次上身root modifier0；12,381次动态 additive 槽跳过 |
| 事务 | 每帧取消重放，共47,610次；2,736次异代/重复遍历拒绝；命令不改变冻结bank |
| 真实 Main 更新 | 三Provider三Hz正常物理轨迹40,320帧，同一14入口组与一次共同Sync；40,320取消重试 |
| 真实 Main 覆盖 | 10,269完全覆盖帧无底层player登记；总11,214 Locomotion隐藏、29,106实际更新 |
| 真实 Main 源 | 共105,961登记player、45,267 Aiming出现位置更新、29,680 Additives状态更新 |
| 构建/实际宿主 | Debug与ExportRelease Optimize各0警告0错误；两配置均实际运行上述联合与真实宿主测试，无Godot错误警告 |
| 回归 | 原缓存2,520帧；Main真实最终曲线反馈11,340帧/9,762姿态；Core动作/Mantle相关243通过0失败0跳过 |

Optimize运行时实际加载三个ExportRelease DLL；结束恢复原六个Debug DLL/PDB，并逐文件SHA验证。`tools/verify_lyra_main_slots.py`检查导出、旧资产与探针哈希、所有实际退出记录、构建和TRX，生成`artifacts/lyra-analysis/lyra-main-slots-verification.json`，终态退出0。

真实宿主观察输入来自已有玩法轨迹，以相同频率循环，用原64秒物理Montage轨迹推进；不读取native机器、时钟或Sync输出作实现。完整联合 Update原生对照仍覆盖计算边界；真实Main只取每种配置首条64秒固定物理轨迹。首次将计算边界一次1090秒delta传入Main，共同Sync报InvalidSyncGroup：其marker处理容量不支持该跨度。该失败与诊断保留，未改变Sync算法、阈值或原生数据。

## 保留的失败与交付边界

保留`main-slots-build.log`（探针变量PI与UE宏冲突）、`lyra-main-slots-ue.log`（UE Python旧语法不接受新式f-string）、`main-slots-host-smoke-build.log`（repo固定SDK工作目录）、`main-slots-host-smoke-build-fixed.log`（测试字段/匿名名称错误）、`main-slots-host-godot-first.log`与`main-slots-host-godot-diagnostic.log`（上述1090秒时间跳变）。均修正采集或测试调用后完整重跑；未覆盖失败证据。

仅关闭活动Slot的联合更新及Main更新宿主入口。MainPoseHost仍使用inactive槽：下一项是以同一完整资源bank在实际Main位置接五槽Evaluate，允许FullBody全覆盖时无Locomotion求值仍发布最终反馈，然后与原联合姿态对照。

Main惯性75、最终ControlRig73、缓存完整初始化/重入、原Linked源注册/通知时序联合原生验证、统一Notify/Montage/root物理消费、完整Provider换类、多角色身份、普通Demo、渲染和性能及整链验收仍开放。用户未提交内容保持；没有提交或推送，整个目标未标记完成。
