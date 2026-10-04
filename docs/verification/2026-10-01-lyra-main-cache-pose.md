# Lyra Main 缓存姿态与可复用求值宿主

2026-10-01，在当前主目录推进原完整 Main。此次新增三级缓存的 Evaluate 生命周期及可复用 Main 姿态宿主，不改变已有更新调度或原生比较阈值。完整 Lyra 移植目标仍开放。

## 实现

`LyraMainPoseCacheScope` 按一次 Evaluate 分配缓存寿命，节点身份为 Main Locomotion83、Split78 和 Provider Aiming BasePose78（全局181）。同一寿命内第一次访问执行源，随后读取完整81骨姿态、曲线存在性/flags、typed属性和RootMotion属性；重新打开寿命重新求值，即使图遍历计数未改变。它不拥有源时钟、Update历史、Sync或骨架发布。返回视图校验所属候选和寿命；结束、重新打开、源故障后拒绝旧视图或继续读取。

`LyraMainPoseHost` 使用现有 Main 和同一 ItemAnimLayers 实例，接原三级缓存访问与实际14入口。执行 LocomotionSM→LeftHand→Locomotion cache→上身split→Split/BasePose cache→Aiming→Recovery additive0.65f→原图入口RootYaw旋转→SkeletalControls。输入姿态只读、各输出独立，最后反馈和所有历史随同一角色事务提交或取消。缓存只复用缓存节点输出，不把最终 SkeletalControls 的查询结果跨 Evaluate 复用。

`LyraLocomotionResources.CreateMainPoseHost` 提供生产资源工厂入口。这里“可复用宿主”表示代码可以由后续角色接入；普通Demo尚未替换。

## 原生验证入口

首次外部插件构建在链接阶段失败：安装版Engine没有导出 `UE::Anim::FCachedPoseScope` 构造/析构符号，日志保留在 `artifacts/lyra-analysis/main-cache-pose-ue-build.log`。探针已改为在原 `UAnimInstance::ParallelEvaluateAnimation` 内执行原 SaveCachedPose / UseCachedPose，由Engine创建真实作用域和内存寿命，没有本地替代实现，也没有修改Engine。

探针用三个Provider原编译缓存节点，源边界是受控真实ALS81 Sequence，Split输入为明确passthrough。每个有效帧运行两个Engine Evaluate寿命；诊断根在缓存访问前固定相同EvaluationCounter，隔离验证作用域失效。每次寿命第一读后修改返回副本的根位置/Distance并改变源输入，第二读应仍返回原缓存。下一寿命换输入应重新采样。完整原Main、缓存初始化/重入历史与活动Montage不在这个组件探针范围内。

首次Engine入口构建还发现RootNode为private成员，并有旧Evaluate重载弃用警告，日志 `main-cache-pose-ue-build-engine-entry.log` 保留。按已有Aiming探针的成员指针访问方式仅替换瞬态Proxy根，RAII恢复；改用包含属性容器的当前 `FParallelEvaluationData` 重载。最终完整外部插件构建 `main-cache-pose-ue-build-engine-entry2.log` 成功实际退出0，未部署到GASP58项目插件目录。

两次独立UE进程实际退出0，均1260帧/4320姿态/2160源求值；每有效帧两个Engine寿命，每寿命两次公开读取，另180帧不求值。原包508项和此前691份JSON字节保持，第二次捕获与第一份immutable数据语义一致。日志 `main-cache-pose-ue-export-engine-entry.log` / `main-cache-pose-ue-export-repeat.log` 均0错误、812警告（原动画脚步GameplayTag及既有Editor/DSL环境警告），没有ensure。未保存任何UE资产。

Godot缓存场景实际退出0：4320姿态/349920骨、4320曲线比较、17280 typed属性，位置最大 `1.3556291398472036e-13 cm`、四元数最大 `5.083065404868406e-16`、缩放差0。RootMotion presence与TRS另逐值比较；原门槛保持位置1e-8cm/四元数1e-10/缩放1e-12。2160缓存寿命、6482次错误身份/旧视图/失败作用域拒绝通过；三个缓存源每寿命只求值一次。日志 `main-cache-pose-godot.log` 无ERROR/WARNING。

新增 `main_cache_pose_v1_requests.json` 347187字节、`main_cache_pose_v1_native.json` 60617037字节，仍位于ignored资产目录。它们依赖原资产哈希，不能格式化或用历史导出覆盖。

## 已完成宿主验证

首次宿主场景实际退出0：三Provider×三Hz移动/转身输入共11340帧、9762姿态；每个缓存源只求值一次（共29286源求值），174次同候选重复Evaluate、207次取消重试、84次晚期足部查询故障、3138次坏操作拒绝和1578次仅更新帧通过。全部姿态/曲线/typed属性/RootMotion与保留的独立手工合成路径精确比较；每帧提交后的Main/Linked/Sync历史及另一角色隔离比较通过。日志 `artifacts/lyra-analysis/main-pose-host-godot-first.log`。

最终资源工厂入口构建后，诊断组合完整复跑实际退出0，保持上述覆盖。日志 `main-pose-host-godot-final.log`；诊断曲线明确标记 `actualFinalFeedback=False`。

另以相同三Provider/三Hz11340帧直接消费实际最终输出曲线，无诊断曲线或named control注入：9762姿态全通道与独立手工参考精确同，每帧Main/Linked/Sync历史相同，提交后58572个曲线值与最终输出逐项相同。174重复Evaluate、207取消重试、1578仅更新、129查询故障和45个未知控制曲线的晚期反馈故障恢复通过，3318无效操作拒绝；另一角色历史保持。实际退出0、无ERROR/WARNING，日志 `main-pose-host-feedback-godot.log`。

首次直接反馈测试在最后覆盖断言失败，原因是沿用了诊断夹具84个查询故障的预期；原Foot布尔更新消费上一Main `DisableLegIK`，真实反馈的启用次数为129。两个失败日志 `main-pose-host-feedback-godot-first-failed.log` / `main-pose-host-feedback-godot-diagnostics.log` 保留。最终测试逐帧核对独立参考的查询启用条件，区分实际/诊断覆盖计数，未修改运行时Foot规则、输入或原姿态阈值。

`StageFinalFeedback` 若晚期控制曲线校验抛错，会将整个宿主候选标记失败，拒绝提交或在未取消前重试；45次该边界错误的Cancel恢复已通过。源候选和Linked候选一起取消，不发布半帧反馈。

上述两项是Godot组合回归，不是新Main整链UE oracle。足部查询为解析平面，不能当作真实Godot碰撞验收；直接最终曲线测试关闭了宿主默认反馈路径的验证缺口，但未关闭最终ControlRig输出边界。

最终Debug与ExportRelease `Optimize=true` 构建均0警告0错误、实际退出0；Python语法和PowerShell解析通过。`tools/verify_lyra_main_cache_pose.py` 核对资产/旧fixture/探针及外部源与包哈希、两次真实UE退出、缓存和两种宿主真实Godot退出、最终编译门禁。成功标志 `LYRA_MAIN_CACHE_POSE_FINAL_VERIFIED` 明确 `production=false whole_goal=false`。

## 范围与后续

当前五个Slot仍使用inactive source/reference边界，node75主惯性和node73最终 `CR_Mannequin_FootPlant` 尚未接入。Provider SkeletalControls内FootPlacement与最终ControlRig是不同节点，前者通过不能代替后者。原Foot初始化前提仍沿用已有显式探针策略，初始v1重复性失败未关闭。

完整Main活动Slot/Montage、缓存初始化/重入、主惯性、RootMotion物理消费、最终ControlRig、全类换绑、统一Notify、Godot真实碰撞和单一骨架发布、普通Demo、多角色、实际渲染、性能及整链原生验收继续保留。原ALS模型/68骨蒙皮与raw69/logical81保持，用户其它未提交修改不纳入此次交付。
