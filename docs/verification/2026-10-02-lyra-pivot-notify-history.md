# Lyra Main 的 Pivot 通知历史接入

本批修复普通 ALS 人物的 Lyra Main 接线：此前 `LyraCharacterAnimation` 每帧省略 `pivotNotify`，一直使用默认 `false`。现在从同一角色上一已提交的通知队列读取原来源状态谓词，传给原 Pivot → Cycle 规则（edge 19）。ALS 的 68 蒙皮骨、81 逻辑骨与同一 ItemAnimLayers 组的 14 个 typed 入口保持现有路线。

## 原 UE 行为

独立可选 `LyraNotifyDispatchOracle` 只读加载原 `ABP_Mannequin_Base` 和九个 provider 类。原编辑图唯一相关函数为 `WasAnimNotifyStateActiveInSourceState`，来源 Pivot、目标 Cycle、类型 `TransitionToLocomotion_C`；直接执行原生成 transition delegate 54。原 Main 开启 notify metadata；本机 `FActiveStateMachineScope` 返回的 Main 上下文索引为 **7**，Pivot 状态为 **4**。这不是 baked machine 序号 0，也不是导出 property 数组位置 95。

安装版 UE 5.8 的 `UAnimInstance::ClearQueuedAnimEvents` 在图更新前将上一 `NotifyQueue.AnimNotifies` 复制到 proxy 的 `ActiveAnimNotifiesSinceLastTick`；额外清理时可 append。原 source-state 查询读取这个队列历史，而不是当前 `ActiveAnimNotifyState` 生命周期。它遇到**第一个同类 NotifyState**就返回该项来源上下文的判断，不继续搜索后面的同类项，也不剔除 inactive/reached-end 引用。

探针执行原提取、过滤、历史拷贝、查询和空 Transition NotifyState 的 Begin/End/Tick。来源状态上下文由请求提供；没有执行整幅 Main 图。237 条轨迹、36,920 帧覆盖全部 36 个原 Transition 来源资产的 30/60/120Hz 正向与反向窗口，以及无上下文、错误来源、inactive、低权重、follower、首项错误/正确、结束点、append 和命名处理器移除。两次独立 UE 进程实际退出 0，所有资源结构/字节依赖保持一致。

原 Main 与九个 provider 的 receive/propagate 均为 false，均没有 `AnimNotify_SaveAttack` / `AnimNotify_ResetCombo` UFunction。探针另向 Main 注册实际原 external named handlers，验证三份原资产的命名顺序与移除行为。当前默认实例没有这些处理器；本批没有添加通用 production external-handler API，也没有把命名事件转成 Actor gameplay event。

## Godot 接线

`LyraSourceNotifyBinding` 保留 Linked 来源身份，并携带包围它的 Main 来源状态。Pivot/Cycle/Start/Stop/Idle/Air 和直接 Main Lean 根据真实访问路径登记 Main 上下文；Aiming/Additives 等图外源不冒充 LocomotionSM 来源。当前补齐的是 Main 上下文，通用任意嵌套机器上下文仍开放。

`LyraNotifyQueueRuntime` 在角色提交时保存最终过滤、合并后的 `Queued` 历史。读取不会改变源时钟、RNG、生命周期或通知消费顺序；Prepare/Cancel/retry 不发布历史，队列仍归 Main 角色所有，换 provider 保留上一帧历史。退休清空历史并拒绝继续查询。`LyraNotifyDispatchPolicy` 校验冻结原 query、metadata、接收类和资源 SHA，防止用陈旧合同接线。

逐帧 Godot 对照原规则与生命周期：36,920 帧和同数 retry，8,674 帧 Pivot 查询为 true，22 次实际原 external named handler 观察，201 个 reached-end 历史引用及 1 个 inactive 历史引用。命名部分对照队列和原 external receiver 参考，尚不是通用 production named receiver 验收。

## 验证

实际 ALS/Jolt 六角色场景使用真实输入与胶囊运动，站/蹲、Unarmed/Pistol/Rifle、反向加速、每角色两次换层；每物理帧取消后重试完整 Main 姿态与通知队列。所有 Pivot 输入必须等于上一角色提交的 source-state 查询，edge 19 必须有真实通知历史。下面仅列物理矩阵的采集数量，最终构建与进程结果见本批审核 JSON。

| Hz | 物理帧 | 六角色移动/retry | Pivot 帧 | 通知输入帧 | 通知退出 | 换层 |
|---|---:|---:|---:|---:|---:|---:|
| 30 | 240 | 1,440 | 835 | 177 | 13 | 12 |
| 60 | 480 | 2,880 | 1,675 | 346 | 13 | 12 |
| 120 | 960 | 5,760 | 3,354 | 688 | 13 | 12 |

最终审核：[notify-dispatch-verification.json](../../artifacts/lyra-analysis/notify-dispatch-verification.json)。Debug 与实际 Optimize 各 11 个进程均退出 0、无 Godot ERROR/WARNING，两次构建均 0 警告/0 错误。两种构建的全部六角色物理摘要及普通十角色三Hz完整 JSON 逐对一致；每种构建六角色合计 10,080 移动/retry、39 次通知退出、36 次换层，普通十角色合计 16,800 发布帧。原 Emote/Warp/root 物理及普通 E 输入回归通过，优化后六个 Debug DLL/PDB 的 SHA 恢复核对通过。本批未重跑 Core 全量；主要变化位于 Godot 通知历史、生产接线及角色资源释放。

另一个实际 OpenGL/RTX 5080 GPU 进程退出 0，480 物理帧摘要与 Debug/headless 60Hz 完全一致，采集第 31/151/271 帧三图并全部查看。第 31/271 帧六角色可见，第 151 帧仅三角色在固定镜头视野内；远景人物较小，没有近景姿态/握持或全角色逐帧视觉验收。截图为 `artifacts/lyra-analysis/pivot-notify-render-{31,151,271}.png`，stderr 为空。最终共 23 个 Godot 进程。

## 失败记录与边界

首次探针编译的 graph API/TObjectPtr/protected Initialize 用法错误已修；最初用 transient Package 作 AnimInstance outer 导致 UE cast fatal，实际退出 3，保留日志。随后两次错误的机器索引查找失败也保留；最终按原 class property 找到机器，再调用原上下文计算函数。初次 C# 属性名错误和项目目录 SDK 定位失败均保留，未放宽判断门槛。

首次 Debug 120Hz 玩法断言通过，但退出出现一个 `JoltShape3D` RID 泄漏，整轮标为失败。角色显式持有并释放自己的两个 capsule shape，构造失败也释放；测试地面 shape 同样显式释放。完整最终矩阵在修订后重新执行，不能用首次有 ERROR 的结果作为通过证据。

没有修改 UE 源码、原 uasset、项目描述符或 Config；可选探针从暂存插件目录移到任务 artifacts，不改变原宿主模块 manifest。原 857 JSON 和 709 个原包按 SHA 校验，新增三个 ignored JSON，当前本地总数 860。此前验证探针/资源不重写。

仅关闭本批原 Main Pivot 来源通知历史接入及最终记录所列矩阵。整个 Main 的联合连续 native、通用 named external handlers/任意嵌套机器、通用 Warp delegates/Seek、Shotgun/Feminine 全图、多 Group/self/Unlink、复杂地形/近景握持/材质/性能与线程并行仍开放；音频、道具物理、头颈专项继续暂缓。没有全量、十分钟或人工完整视觉验收；整个移植目标仍 active，没有提交或推送。
