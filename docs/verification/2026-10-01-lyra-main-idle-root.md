# Main Idle 根曲线反馈与有序回调

2026-10-01，在主目录实现并验证。Godot4.7.2 .NET、安装版UE5.8.1、GASP58。继续沿用ALS模型/81逻辑姿态。关闭**Main Idle StateResult回调及共同Scope的反馈事务**；完整Main机器自主选边/权重、最终混合、祖先惯性、统一通知、生产Layer与普通Demo仍开放。

## Main和Provider反馈的区别

Main `LocomotionSM` Idle的StateResult8更新函数读取上一已提交的**整个Main**曲线 `RemainingTurnYaw / TurnYawWeight`。这不等于Linked Idle Provider读取的自身TurnYawWeight反馈，也不能在本帧Idle姿态求值后立即执行本帧根更新。

当上一Idle权重大于0且当前机器状态不是Idle时，原回调只将TurnYawCurveValue置0，保留当前RootYawMode。其余可见更新将Mode设为Accumulate；原double NearlyEqual阈值0.0001使近零weight清除TurnYawCurveValue。非近零时两个float曲线转double后相除，只有上次TurnYawCurveValue非0才将曲线差从RootYaw减去，调用原SetRootYawOffset。根角度经过原float NormalizeAxis边界与站/蹲CDO ClampAngle，并同步AimYaw；禁用时两个值都清零。

`LyraMainIdleRoot`执行这一计算；`LyraMainUpdateHost.ApplyGraphRootYaw`更新当前不可变候选与AimYaw，原观察/宏只执行一次，旧宏候选随替换失效。最终RootYaw和图的Mode随原Main事务提交，取消不发布。

## 按原根顺序更新

原十根Scope曾先准备全部地面根，再准备Idle/Air；源登记虽有序，根回调不具备完整交错顺序。这在Main Idle写RootYawMode后已不能保留。本批在原Ground Scope增加外层遍历回调，地面、Idle与Air的prepare按同一十根顺序执行：Idle根反馈先于其Provider子图；之后Start/Stop回调按实际位置覆盖Mode。Lean仍复用同次Main旋转观察，源登记仍一次共同Sync。

Scope按角色持有MainTurnYaw及上一Main曲线反馈。外层最终Main求值完成后调用StageMainFeedback，验证当前候选、完整曲线布局、已求值根与有限值，缺失曲线取0；重复stage和update-only发布反馈拒绝。反馈只在整帧成功提交时替换，隐藏、仅更新、晚期故障和取消都保留上一反馈。Provider自身反馈继续按自身pose提交，二者没有合并。

当前共同Scope测试对反馈使用受控Idle姿态作为外层输出，验证事务与回调消费；**完整机器最终混合尚未接入，此夹具不构成真实最终Main反馈对照**。Main机器当前/上一权重与访问仍为外部输入。

## 实际UE回调捕获

新增只读 `AlsLyraMainIdleLibrary`。在真实Main实例上运行原StateResult8的Update，其子链暂接空节点，以隔离主根回调；显式设置机器当前状态和proxy双缓冲上一Idle权重，向真实Main proxy发布受控曲线。使用typed成员访问，没有引擎布局offset、引擎源码修改或资产保存。原链接在销毁前还原。

三Hz共2520帧，覆盖隐藏、淡出、非Idle但上一Idle权重0、站蹲、启用/禁用、三种输入Mode、正负curve weight、0及原NearlyEqual阈值两侧、反向/大幅曲线差和角度边界。捕获before/after的TurnYawCurveValue、RootYawOffset、AimYaw double位模式及Mode。实际Godot逐位通过：753次SetRootYaw调用、83隐藏、912淡出、328零weight；每帧取消重试，753旧宏候选拒绝。

两次独立UE执行正常退出0，结果相同。508原包与643份既有JSON SHA256保持；新捕获位于 `artifacts/lyra-analysis/main-idle-root-native.json`，未写入既有资源JSON。UE两份日志各20条warning；无Error/Ensure/Assert/Fatal。外部插件构建成功、无C++编译warning/error；没有修改UE或GASP原资产。

## 共同Scope和回归

受控十根3780帧、11064姿态，1008全访问、252全隐藏、543仅更新、1212父inactive访问、20交错顺序、三个真实同步组。Idle在Start前/后各126次检查最终Mode；3021次反馈事务、23次实际反馈变化，34743坏操作拒绝。含NaN反馈、重复stage、带pose反馈的update-only拒绝、晚期取消恢复和既有异代/坏Sync/样本门禁。仅更新继续断言Warp、Provider反馈与Main反馈保留/原初始化重置。

既有真实四地面原生3780帧/8400姿态/680400骨回归通过，全部原门槛不变。Debug/ExportRelease Optimize 0错误0警告；最终Godot各退出0且无ERROR/WARNING。没有本批Core全量、完整Main机器新原生轨迹、普通Demo/渲染/性能验收。

编译中的Span局部函数捕获和smoke Rotation输入参数遗漏已修正，失败日志保留；运行时逐位门槛未放宽。加强顺序夹具另外增加Idle+Start两根访问，以直接隔离Mode顺序，其余十根全访问覆盖保持。

证据：`main-idle-root-ue-{export,repeat}.log`、`main-idle-root-godot-first.log`、`main-idle-scope-joint-ordered-final.log`、`main-idle-ground-regression.log`、`main-idle-debug-final-scope.log`、`main-idle-optimize-final.log`和`main-idle-ue-build.log`，均位于 `artifacts/lyra-analysis/`。复核脚本 `tools/verify_lyra_main_idle.py` 输出 `main-idle-final-verification.json`。

下一步将已验证Main机器与十根owner放入同一候选，消费自己的相关源、真实Sync及通知历史，落实源cached weight清除/初始化，生成真实访问并求值最终混合及原惯性；再接14入口、装备生命周期、通知/Montage、其余后处理和普通Demo。
