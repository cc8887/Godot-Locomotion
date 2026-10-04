# ALS 人物上的 Lyra SaveCachedPose 统一所有权

继续主目录的完整 Lyra 移植，ALS skin68/raw69/logical81、十四入口及原图拓扑保持。本批将固定 Main 78/83 和 Provider 78 的阶段历史、实际延迟更新权重与 Main 内姿态缓存连接到同一实际 owner。完整目标仍 active。

## 实现

`LyraPoseCacheLifecycle` 由实际 Main 或 Linked Provider 持有。Initialize 与 CacheBones 使用这份节点历史；Prepare 复制到角色候选，实际 deferred traversal 在访问子源前写入选中的 GlobalWeight，Evaluate 同步同一节点的候选计数。角色整体预校验后 Commit，失败 Cancel；没有第二份阶段历史或独立播放器。

Main 每次执行 PostGraphUpdate 清零其两个缓存的 GlobalWeight；Provider Aiming 函数实际访问时才执行对应清零与延迟更新，隐藏函数保留原历史。最大权重上下文、首次同权重优先、skipped 消息和原 Provider→Main 队列顺序仍使用既有真实遍历。原 SaveCachedPose 的 UpdateCounter 在本机 Update/PostGraphUpdate 中未同步，保持无效；不能从播放器历史推断新的相关性重初始化规则。

姿态数据仍归单次求值作用域，保留骨姿态、曲线存在性、typed attributes 与 RootMotion 完整通道。作用域捕获真实 Main/Provider 与当前候选身份；同 counter 的新作用域也重新采样。嵌套求值改变同 owner 的计数后，外作用域恢复时重新求值；同步发生在子源前。关闭、取消或提交后的视图拒绝读取。

普通 MainPose 与真实 SlotComposition 都使用该 owner 选择器。逻辑别名 181 映射到实际 Aiming Provider 的节点78，Main节点78/83保持Main身份。Main阶段控制器借用同一Main owner，Provider阶段直接使用本实例owner。初始self/Unlink路径的空SkeletalControls根不遍历这些Update/Evaluate输入，Main阶段历史仍保留。

## 原 UE 对照

临时探针加载原 Main 与 Unarmed/Pistol/Rifle 编译类的真实 SaveCachedPose 节点，以受控 leaf 代替子图，原 Initialize/CacheBones/Update/PostGraphUpdate/Evaluate 均调用本机引擎实现。原 `UAnimInstance::ParallelEvaluateAnimation` 创建实际 FCachedPoseScope；探针没有复制作用域实现或修改引擎。

两独立UE进程最终cache-owner-v2与-repeat的requests/native/closure逐字一致。三类共九个节点、90步：同初始化计数异帧、同骨缓存计数异帧、重复缓存、短计数回绕、空更新、同权重、同计数新作用域及两种嵌套计数。Godot对照1620个计数字段、源回调计数、GlobalWeight和受控root标量输出，并实际取消后重跑：36次retry、234次源求值、12次嵌套执行和234次非法生命周期拒绝。

探针的节点计数是受控输入。引擎完整求值入口会先增加Main Proxy计数，最终探针在受控根进入时设置请求计数；这不是自然组件计数的等价验收。受控leaf根标量仅证明缓存生命周期；ALS81完整通道由后述既有完整Main参考与合成回归核验。

## 验证与资源保护

最终Debug与实际ExportRelease Optimize均0错误0警告，关联Core187项通过。两构建各27个主矩阵场景加四布局完整Main，共62个Godot进程，全部退出0且无Godot ERROR/WARNING。实际owner原生90行/1620计数字段、MainCache2520帧、CachePose1260帧、Slot47610帧、MainPose/反馈/合成各11340帧、MainRig7560帧、原阶段、初始self/Unlink及普通十角色/Emote通过。四布局每构建4320帧及同数retry保留原最终Rig/字段门槛，初始/解绑计数和普通完整报告同Debug/Optimize及既有基线。

独立审计通过27份冻结实现源、其余2198条基线文件、870资源JSON、710原UE包、9宿主配置和31份本机原UE逐字源码副本；三轮Optimize六程序集备份恢复通过，实际Optimize主DLL与Debug不同。原生两进程既有加载/GameplayTag Warning各744条，无Error/Fatal/Ensure。最终runtime v4、native v2/v2-repeat、package-v5；失败v3的27源与十二程序集审计通过。

MainPose既有11340帧夹具新增实际owner断言：Prepare/Evaluate/反馈不能提前发布缓存历史，真实延迟更新权重必须进入实际节点，取消恢复，update-only保持Evaluation计数，Commit精确发布候选且不影响其它角色。该检查保留原独立合成参考、所有姿态通道和原精度门槛。

固定资源、原包、宿主配置及本机引擎来源保持逐文件SHA校验。没有保存原资产、重导人物或动画、修改引擎/原项目源码配置、提交或推送。

## 失败证据

保留package-cache-owner-v1/v2/v3的失败包和构建日志：Unity局部名称与共享引用类型、FCachedPoseScope非DLL导出、ParallelEvaluateAnimation实参类型错误。最终通过引擎原导出的求值入口创建作用域。

v1/v1-repeat原生采集逐字相同，但Main根入口的隐式计数递增使其不对应请求节点计数；不用于最终请求对照。v2首轮Godot失败日志保留。最终原生v2明确控制节点收到的计数，保留自然计数范围限制。

runtime v3已逐项通过90行原生计数/权重/输出，但最后覆盖断言未计入取消后的真实嵌套重跑，错误要求6次。改为实际12次，保留27源、Debug/ExportRelease十二程序集、失败报告和日志；没有修改生产缓存算法或数值门槛。最终runtime v4。

独立审计首轮将既有SlotComposition夹具的预期帧数误写为11340；实际夹具源码要求47610帧，两构建均精确达到该数量。修正审计器的库存断言并保留首轮失败日志，没有修改夹具或生产实现，也未重新运行已通过矩阵。

## 保持开放与下一步

复跑时使用新的证据标签，保留已有输出：

```powershell
& scripts/capture-lyra-cache-owner.ps1 -PackageName package-cache-owner-v5 -RunTag cache-owner-new-native
& scripts/verify-lyra-cache-owner.ps1 -Configuration Debug -EvidenceTag cache-owner-new-runtime
& scripts/verify-lyra-cache-owner.ps1 -Configuration Optimize -EvidenceTag cache-owner-new-runtime
```

Godot原生对照场景读取本批固定的v2参考。新的捕获用于独立复核，不会自动替换已有参考；只有确认边界与保护清单后再推进新的资源版本。

本批完成固定Main/Provider缓存的共同历史与Main组合路径，不能扩大为通用完整阶段调度。直接Aiming组件夹具的输入仍为受控prepared Pose；任意函数重复调用/部分绑定和其它Provider的通用求值作用域还需接实际执行器。当前生产计数由既有宿主求值序号和物理观察帧建立，原Proxy绝对计数/GFrameCounter映射仍需连续原生核验。

下一步统一实际角色各阶段的counter推进与重试身份，再接完整后续图重初始化、RequiredBones/LOD和Rig Construction。保持当前源采样、原遍历与单一骨架writer；对真实绑定切换、隐藏恢复、重复调用、update-only及取消重试取得原生阶段/求值联合参考。

非零Aiming参数原生传播、self scalar/部分绑定、所有Source/Foot/Leg私有存储、Linked字段34/47与多组32/47、新ALS默认最终native仍开放。UE/Jolt既有314/1680物理差异、复杂地形、近景握持、GPU/全量/十分钟/性能不由本批关闭。音频、道具物理及头颈继续暂缓；完整移植目标保持active。
