# 分层动画统一帧所有者

日期：2026-09-13。第一百二十七批，承接双手 IK 组合。

## 变更

新增 `AlsLayeredAnimationFrameRuntime`，独占一个角色的 BaseLayer、Overlay、
状态通知/过渡请求、BasePoses/LayerBlending、Aim/脊柱及双手 IK。库与 Godot
图节点仍由调用方管理；此类不直接修改 Skeleton，也不取得物理发布权。

此前这些阶段由 `OverlaySharedFrameSmoke` 分散协调。新类集中完成：

- 建立并校验移动/Overlay 共享来源绑定；合并真正需要的曲线名。
- 使用自己保存的上一已提交最终曲线，冷启动保留空曲线。
- 先准备外层 Aim 与分层缓存请求，在 BaseLayer 收集来源的回调边界准备
  Overlay，再由同一个批次同步来源。没有每帧分配的闭包收集器。
- 按 BaseLayer → Overlay → 分层 → Aim/脊柱 → 左右手求值。
- 求值后解析原 Overlay 状态通知，并向同一物理蒙太奇所有者申请过渡。
- 校验所有子候选后提交；最终手部曲线进入下一帧反馈。
- 任何求值异常自动取消所有子候选；下游也可以在完整求值后拒绝整帧。

类名和边界不表示完整 AnimGraph 已完成：当前所有者截止 Foot IK 输入，
仅提供明确控制输入的 Prepare 接口。它还没有替换生产端使用的
`AlsProductionMovementRuntime`，也没有新增 Ragdoll 根分支。

## 验证

`OverlaySharedFrameSmoke --owned-frame` 建立第二套独立库、图和新运行时，
与既有真实组合执行同一输入。30/60/120 Hz 共 1,260 帧：

- 每帧最终 79 骨骼的局部姿势及曲线逐项精确相同。
- 共享同步状态、来源事件数量/身份/顺序、Overlay 过渡命令、物理蒙太奇
  候选及通知状态相同。
- 每帧第一次完整求值后模拟下游拒绝；已提交的反馈、Aim、BasePoses、
  蒙太奇和通知历史不变，同帧再次准备/求值结果相同，再提交。
- 10 次准备之后的无效组件变换引发求值异常，自动取消后可以直接重试。
- 提交后的反馈精确等于最终手部输出曲线。
- 既有组合场景检查继续通过：左/右手相关 828/105 帧、部分权重 156 次，
  Overlay/LayerBlending/Aim/手部各阶段晚期失败检查保持。
- Debug 优化构建 0 警告、0 错误。

证据：`artifacts/layered-owner-first.log`，末尾为
`LAYERED_FRAME_OWNER_OK frames=1260 exact_pose_curves=1 shared_sources=1 events=1 retries=1260 evaluation_failures=10`。

这个对照验证的是所有权收拢没有改变已实现组合的行为；两边复用了既有
节点实现，因此它不是独立 UE 全图输出对照，也不是最终视觉验收。

## 下一项及保持的范围

生产 BaseLayer 当前拥有全局 Ground/Air/Aiming 属性更新，外层组合的更新
上下文又决定 BaseLayer 的实际图权重。因此生产接线需要明确拆分全局属性
更新与图来源更新的边界，避免重复更新 aiming 历史或使用上一帧候选值。
随后完成最终根初始化/重入、缓存生命周期、Worker/Demo 最终发布，闭合
脚部/平台并执行 UE/Godot 多帧、人工验收。

默认 Demo 仍是 75/109 BaseLayer 输出，视觉问题未关闭。原 P5A 剩余项、
P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera、
P7 十分钟性能预算全部保留。既有全 Core 23 项失败、Import 分配稳定性和
p95 2.559ms 超过 2.5ms 的记录未关闭。音频继续暂缓。
