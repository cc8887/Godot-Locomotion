# Lyra 请求与 weight 阶段的同步 Montage 回调

2026-10-03，接续 ALS 人物与 Animation Interface/Layer 路线。当前完成同步候选回调、实际动态库存遍历和事务化外部效果；原生双捕获、定向 Core、最终 Debug/实际 Optimize 二十六进程及独立审计均通过。完整目标保持 active。

## 回调、候选与外部效果

新增 `AlsMontageCallbackContext` 和 `BindInstanceMontageCallbacks` / `BindGlobalMontageCallbacks`。实例 envelope 捕获 update/effect 两部分；原来的 `BindInstanceMontageEvent` 保留为仅 effect 的调用入口。原 Emote 的外部 Ability 消费仍在物理成功边界发布，调用的真实物理委托和统一角色流程保持。

当本实例的 Montage 队列阶段关闭时，update 在真实请求/weight 生产点立即执行，可以查询同一 bank、停止、重播和重绑。队列阶段打开时，两部分按原容器排队，在已提交 bank 的实际派发阶段执行。角色、Layer 实例和 bank 的拥有关系没有改变。

update 回调通过上下文的 `DeferEffect` 在原调用位置记录外部动作。候选只修改 bank、绑定、队列和缓存；Cancel 清除候选和效果，Commit 或成功物理边界发布效果。动画 retry 重新执行候选更新，再按既有物理凭据确认并清除对应效果，不能只跳过回调，也不能重播外部动作。独立 effect 参数同样遵守这个边界。实例 update 返回后读取当刻的 global 绑定；每个 global update 执行后捕获其 effect，嵌套调用保留实际次序。

上下文有嵌套 token，内层结束后恢复外层，回调返回后拒绝旧上下文。回调不能 Commit、Discard、提前发布效果或确认其自身事务。失败 Prepare 不得污染随后已提交回调的 Identity/Phase；已提交派发明确使用 CommittedIdentity/PostTick。

这是 typed 回调协议：update 只操作上下文允许的状态，外部动作使用显式效果记录。任意 C# 闭包直接修改世界/对象状态没有自动回滚能力；完整 Blueprint/GAS 回调迁移和对象销毁语义仍待，不能把该协议称为任意脚本事务支持。

## 动态 weight 与实际库存

本机 UE `AnimInstance.cpp:2252` 的 weight 循环每次读取 `MontageInstances.Num()`。回调新建实例可能在同一次循环中更新 weight，并进入随后同一帧 Advance。原固定 `_weighted` 快照和 `advanceCount` 已移除，weight 直接更新实际候选库存，回调后的新状态参与后续更新；容量增长不丢实例。

Advance 也读取实际库存，在终止实例时保留有效实例的访问顺序，不另建影子 bank。当前 bank 仍表示有效单 Section 实例；原始失效槽的内存、任意资源 Notify 改库存和原对象销毁后的安全早退没有在此批验收。

`AnimMontage.cpp:2038` 先保存 PreviousWeight、更新 Blend、调用 BlendedIn，再计算 NotifyWeight。因此新增两个候选字段：PreviousWeight 属于这次 weight 开始时，NotifyWeight 冻结在本实例回调结束时。稍后的其它实例回调即使停止它，也不能重新计算 NotifyWeight。Traversal/NotifyTraversal 消费该冻结值，停止与重播保留原 Blend 数值和已冻结代理姿态。

零时长停止仍先发布 DesiredWeight/active lookup、中断和混合，再执行 Out 实例委托与 global，返回后清除 Playing。自身停止、停止未更新或已更新的另一实例、新实例追加和回调重绑均使用同一 bank。

## 真实 UE 与 Godot 对照

新可选 `LyraMontageImmediateOracle` 复制上一 bank 探针，保留原 shared library 和旧 producer CPP。使用原 Manny、原 FingerGuns、Pistol Fire 与 FingerGuns MW Montage，在独立 GamePreview World 的真实 UAnimInstance 中执行，不保存资产、不修改引擎或原图。

| 九类请求，各30/60/120Hz、三秒 | 要验证的行为 |
| --- | --- |
| in-play | In 回调新增实例，随后同帧 weight/Advance |
| in-play-many | In 新增九实例，越过原八项容量 |
| in-play-root | In 新增原 MW 实例并选择当帧 root owner |
| in-stop-self | In 内零时长停止自身和嵌套 Out 次序 |
| in-stop-other | In 停止尚未完成本帧 weight 的实例 |
| in-stop-earlier | In 停止已经完成本帧 weight 的实例 |
| in-rebind | In 内重绑 Out 与实际 global 广播目标 |
| request-out-play | 请求阶段 Out 回调重播同组 Montage |
| request-out-stop | 请求阶段 Out 回调停止另一实例 |

两独立 UE 进程均退出0，完整 requests/native JSON 各自字节相同。27轨迹、5670帧、405次观察：195 weight、30 requests、180 dispatch；其中27回调返回、189实例回调与189 global。原生快照只读八个公开库存字段和 root owner，没有假称读取私有 NotifyWeight。该缓存另有源代码依据和独立 Core 门禁。

Godot 执行自己的请求和回调，原参考仅作断言。每帧先取消再重建候选；回调在原点捕获库存值，通过效果记录在成功边界投递，逐项比较即时和排队回调、前后库存及 root owner，保留八字段原 `1e-7` 门槛。首次结果为5670 retry、405 callback/effect、106368检查；没有用延后时刻的库存替代真实回调时刻值。另验证冻结代理不因派发后新请求改变。

最终 Core549项通过，0失败、0跳过，包含本批九项真实 bank 更新/嵌套/容量/取消/凭据/过期上下文/异常/已提交身份门禁和关联旧测试；不是 managed 全量。两构建零警告、零错误。

## 最终生产矩阵与审计

最终108份冻结源为 `immediate-callbacks-v1-frozen-sources-v2.json`。V1冻结保留为增加失败候选身份门禁前的历史。最终 Debug/实际 Optimize 各十三个 Godot 进程均退出0，成功标记齐全，无 Godot ERROR/WARNING。

| 每构建的检查 | 实际范围 |
| --- | --- |
| 新同步回调原生对照 | 27轨迹、5670帧及同数retry、405 callback/effect、106368检查 |
| 上批发布后 bank 回调 | 12轨迹、5880帧及同数retry、171回调、33021检查 |
| 原事件容器与 Emote | 21轨迹/8823参考行/8820retry；Emote54轨迹/27720帧及同数retry |
| 三频六角色实际移动 | 30/60/120Hz，共10080移动及各10080次移动前/动画retry |
| 原 Warp 与 root 物理 | 各60Hz、六角色、2880移动及两类retry |
| 普通十角色与 E 场景 | 每角色十四实例，共5280角色发布帧，换类/复用/武器/事件及retry |
| 原 ALS 普通入口 | 60Hz、1700帧 |
| 完整 Main 最终 Rig 对照 | 三Provider、十四实例布局、1080帧及同数retry，检查当前34/47 Linked字段 |

三频实际移动及两份普通场景的完整报告跨构建相同，也与上批最终对应报告相同。完整 Main 使用受控物理输入；它不证明 UE/Chaos 与 Godot/Jolt 世界运动等价。

`tools/verify_lyra_montage_immediate_callbacks.py` 核查上述终态日志、Core TRX、双捕获、108源文件、实际程序集、原探针与成功package源码、两轮六文件Debug恢复，以及869份JSON、710个UE包和9项项目/Config文件。最终 `immediate-callbacks-v1-integrity.json` 为 `auditPassed=true`、`synchronousCandidateBankMutation=true`、`transactionalCallbackEffects=true`；`fullMontageEventDispatch`、`resourceNotifyTermination` 和 `goalComplete` 仍为false。

原生 package 为 `package-immediate-callbacks-v1`；capture 标签为 `immediate-callbacks-v1` 与 `immediate-callbacks-v1-repeat`。新增脚本默认 package 指向实际成功产物。复跑应使用新的 RunTag，禁止覆盖已有捕获；当前 Godot 参考固定为经过核查的 V1。

```powershell
& scripts/capture-lyra-montage-immediate.ps1 -RunTag <new-tag> -PackageName package-immediate-callbacks-v1
& scripts/verify-lyra-montage-immediate.ps1 -Configuration Debug
& scripts/verify-lyra-montage-immediate.ps1 -Configuration Optimize
```

上述矩阵标签已经使用时，需复制脚本并设置新的证据标签；不能删旧证据后重跑。Optimize 在 finally 恢复六份 Debug 文件；禁止同时运行两种配置。

## 整体进度和下一步

继续使用 ALS68 skin/69 raw/81 logical、生成十四 Interface 入口、按类/组或调用点的真实实例、角色共同 Source Sync 和唯一骨架发布。当前 Linked 字段范围仍34/47。

本批实现指定 typed 请求/weight 同步 bank 更新和效果边界；不关闭任意 Blueprint/GAS 状态与世界事务、弱对象/销毁、重新 Advance、Section 跳转/循环及 Ended 的 active NotifyState 结束顺序。主 Montage 共享模式变化、非空左手源、余下配置/引擎字段、default/self/Unlink/部分覆盖和同函数多调用点的通用整图、其它 Provider 保持开放。

完整 Chaos/Jolt 同输入运动的既有314/1680帧差异、近景握持/脊柱变形、地形/平台、独立导出、跨平台和性能仍待；音频、道具物理、头颈专项按原要求暂缓。本批没有GPU/近景、managed全量、十分钟性能或人工全矩阵验收，完整移植目标尚未完成。
