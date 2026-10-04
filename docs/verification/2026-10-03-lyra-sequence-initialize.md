# ALS 人物上的 Lyra Sequence 源初始化

2026-10-03开始，最终验证跨至2026-10-04；主目录直接推进；ALS skin68/raw69/logical81、原十四入口及完整移植目标保持。此记录在最终审计完成后由发布器更新验证段。

## 实际行为

实际 Provider 的阶段遍历现在进入各真实 Sequence 宿主，覆盖三类 Provider 各26个 SequencePlayer/Evaluator 节点的执行入口。初始化只访问原图选中的叶节点；未访问的机器状态不会因此提前执行 Setup、推进时钟或产生通知。资源、播放器ID与epoch仍由现有角色/实例绑定提供。

SequenceEvaluator 保留内部与ExplicitTime，仅重置Marker索引并标记待重初始化；SequencePlayer重设原起始时间，Idle Recovery使用原TurnTime绑定。Marker距离、Delta历史及已有缓存权重保留。Air原固定源和Additives恢复源消费原资源绑定；动态Start/Stop/Pivot/HipFire继续由首次Update原回调选择资源。

HipFire待初始化标记随候选提交/取消；隐藏帧的重新初始化留到实际Update，取消不会提前消费。原空LeftHand evaluator增加实际宿主内的出现位置历史，权重非零的null-source Update清除待标记但不推进时钟、登记Sync或制造动画资产。其更新也随角色事务提交取消。

原状态进入路径的候选初始化保留；本批没有将所有后续状态进入改为直接修改已提交状态。Idle阶段与候选更新共用其源初始化规则，其他现有受控规则继续接受原生连续回归。

## 原生参考

新增可选LyraSequenceInitializeOracle，在临时GamePreview世界加载原Main、原三类Provider与Manny164，完成普通Link后，对26个实际叶节点逐个设置两组内部时钟、Marker距离/索引、缓存权重、full-weight与Delta种子，再执行原Initialize和CacheBones方法。合法派生成员指针只读写原protected字段，没有复制初始化算法、修改引擎或保存资产。

两次独立UE进程原始requests/native/closure逐字相同，共78节点、156初始化记录；CacheBones快照与初始化后相同。原full-weight被清除、marker索引重置，距离/Delta/缓存权重保留，Evaluator内部时间保留，Player归零；动态explicit setter按原节点是否可写保留结果。原加载/GameplayTag各744条Warning保留，无Error/Fatal/Ensure。

这是受控叶节点初始化参考，不是自然世界启动、完整动画更新或ALS几何缓存验证。managed场景直接把种子写入实际宿主字段，无生产seed API；比较内部/公开时间、资产映射、Marker、Delta以及记录内已有权重，并检查首次Update、隐藏、拒绝和取消。外置Cycle/Hip缓存权重不逐项比较，原private bReinitialized和full-weight字段未在managed逐位验收；实例ID只沿既有播放器/epoch模型消费，不对照UE哈希值。

## 验证与保护

最终Debug/实际ExportRelease Optimize均0错误0警告；关联Core134项通过。每构建29个主矩阵场景加四布局完整Main、共33个进程，两构建合计66个进程全部退出0、无Godot ERROR/WARNING。新156记录、八类旧源组件、原阶段、初始self/Link/Unlink、取消重试、普通十角色/Emote与四布局final Rig均通过。初始/解绑计数与graph-phases-v1逐项相同，普通十角色/Emote完整JSON也相同。四布局每构建4320帧及同数retry继续对照既有原生参考，原字段34/47和32/47范围保留。

独立审计为artifacts/lyra-analysis/source-initialize-v2-integrity.json：33份当前冻结源、其余本批1463条基线受控文件、870份资源JSON、710个原包、9项宿主/配置和16份原UE源码副本保护通过；三轮Optimize六文件恢复精确，Optimize主DLL与Debug不同。新原生参考使用source-initialize-v1及-repeat标签，最终运行和恢复用source-initialize-v2标签。没有新GPU/全量managed/十分钟/性能或完整物理等价验收。

首轮测试类编译发生Node.Name同名及不存在WorkerFields访问，修为AssetName和实际worker.State。首轮运行误将合成Main Lean ID当作Sequence path导致索引异常，改为实际Sequence/Recovery资源域；失败日志source-initialize-v1-debug-first.log保留。隐藏reset标记修订前的v1矩阵与最终v2明确分开，最终验收使用修订后产物；没有放宽误差门槛或删除断言。

## 保持开放

本批推进Sequence源启动字段；完整源private状态/full-weight、BlendSpace滤波/样本初始化、骨控制初始化、阶段与Update/Evaluate缓存统一、后续完整重初始化、RequiredBones/LOD和Rig Construction仍开放。self scalar/部分绑定/重复函数调用/其它Provider、原字段34/47、UE/Jolt物理314/1680帧差异、近景握持和地形/性能保持；音频、道具物理及头颈暂缓保留。完整目标active，无提交推送。
