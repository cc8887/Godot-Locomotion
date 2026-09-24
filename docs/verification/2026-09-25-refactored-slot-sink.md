# 完整 Layering 图接入共享 Montage 帧

本批在主目录 main 新增 `AlsRefactoredLayerSlotSink`，把完整 Refactored Layering 图的七个 Slot 回调连接到现有 `AlsMontageFrame` 与 `AlsMontageSlotPose`。没有另建播放时钟、实例分配器或提交历史。输入、骨缓存和跳过缓存更新通知继续转交上游 source owner。

## 生命周期与数据

- 宿主在共享 bank Begin 后绑定冻结求值帧，再运行图 Prepare/Evaluate；图更新后取得 ushort RelevantSlots，供统一通知 owner 使用。图 Commit/Cancel 后 End，再释放 bank 帧。上次提交的通知相关性仍由既有通知运行时保存。
- Slot 权重来自同一冻结帧；图判断源是否参与，适配器严格校验，不使用被遮挡源的占位数据。通过预分配缓冲完成 float 图姿态与 precise Slot 混合器之间转换，保持骨/曲线布局。
- 拒绝重复 Begin、错角色更新、过期帧和不一致权重/源状态；求值重入保护在异常后释放。外部仍负责整帧提交/丢弃。
- 核对本地 UE `AnimInstanceProxy.cpp` 的 `UpdateSlotNodeWeight`：访问过的 Slot 以 MontageLocalWeight 判断本帧通知相关性，不附加 NodeGlobalWeight 条件。实现与零全局权重测试据此修正。此处是源码核对，不是新原生运行 oracle。

## 验证结果

完整真实 94 节点图、原始 Stand/Crouch 基础姿态、共享七个 sequence Montage 实例，在 30/60/120 Hz 各跑三秒，共 630 个提交帧。每帧先求值再丢弃重试，姿态、曲线、相关性、候选实例一致；覆盖淡入、全覆盖、淡出到空 bank，七个区域均被访问。全覆盖 Curves Slot 的六个 Slot 控制曲线为 .5，确认经过实际图尾部 reset/Slot/accumulate/override。

三个频率分别注入一次实际区域采样异常，丢弃 bank 后过期帧读取被拒绝，清理后同帧重试成功。另验证重复绑定、错角色身份和零全局权重相关性。

- Import Montage/Mantling/RefactoredLayer 定向：216 通过、0 失败。
- 修正相关性条件及补异常恢复后，完整图 13 项复跑通过。该数量与上述回归重叠，不能相加。
- Optimize 构建成功、0 warning、0 error。
- 日志位于 `artifacts/refactored-slot-sink/`。没有新 UE 导出、Godot 场景运行、全量 Core/Import 或最终视觉验收。

区域 Montage 的时钟/权重来自真实共享运行时，但测试区域采样使用真实 Crouch 姿态与受控曲线，不是已经导出的七套原生区域 Montage。本批也未接入普通 Demo；仍需真实资源及 UE Slot 覆盖对照、Refactored 宿主完整布局、Head/View 与普通 Mantle 接入。物理稳定性、Flail、最终性能等既有缺口保留；头颈、道具物理、音频仍暂缓。
