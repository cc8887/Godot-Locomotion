# Overlay 跨图来源与资产通知事务

第 119 批，2026-09-13。仓库 `../GodotALS-p5a-events-actions`。

## 实际变更

新增正式的组合来源定义：原有 75 个移动播放器身份、109 个采样身份保持
不变，追加 Overlay 的 148 个独立节点，合计 223 个来源、257 个采样身份。
122 个 Overlay teleport evaluator 同样获得各自的 occurrence/authority，
但不获得推进同步时钟或派发资产通知的权限。

组名合并后共 10 组：原移动图的 8 组保留编号，Overlay 的 Locomotion
复用原移动组，SecondaryMotion 和 IdleAdditive 追加。三个步枪手臂来源
的 AlwaysFollower 角色从正式绑定传到同步 tick，并由通知映射检查角色
一致性。旧来源的默认 CanBeLeader 不增加序列化字段，原有来源摘要保持；
组合来源的摘要同时覆盖追加数值表及 Overlay 图、时钟、通知数据摘要。

完整原生节点 inventory 和 P5 occurrence 编译已经接受该组合定义，
不通过把新增节点标成“未绑定”绕开身份检查。角色的 Main Movement、
Grounded、Air、Landing、Overlay 来源收集器能够使用同一候选缓冲。
缓冲容量调整为 223/257/10；时钟、epoch、组历史和资产通知均由原来的
Main Movement 事务拥有，Overlay 收集器没有另一套已提交时钟。

`AlsMainMovementFrameRuntime` 在一次共享 tick 前接受额外图的来源，
tick 后把最终时间交给各图采样。Main Movement 被 Slot 隐藏时，仍收集
活跃的 Overlay；两图都不参与时执行空批次，退休同步组和通知状态。
`AlsBaseLayerFrameRuntime` 与 Overlay 姿势运行时提供提交预检查，让
外层能够确认两图都求值成功后再提交。晚期上身失败不会提前发布移动结果。

`AlsMovementGraphDefinition.WithSharedOverlaySources()` 提供组合绑定
变体，并重新绑定依赖 occurrence handle 的 Turn/Montage 通知表，不能
拿旧布局的蒙太奇通知 handle 配合新来源表。

## 原生 Notify 数据补采

真实 UE 的 29 个 Overlay 直接来源动画均为 **0 个资产 Notify**。
`assets/config/v4_overlay_notify_inputs.json` 明确记录每个资产的空通知表，
不把“尚未读取”当作“没有通知”，也不为动画伪造事件。Overlay 状态机
通知不属于这些动画资产，其最终图消费者仍待实现。

新增只读导出脚本复用原 Roll 的 native notify 读取函数。原脚本增加
显式入口保护以供导入；其原功能也在真实 UE 中重跑，两资产四通知，
输出与正式 `v4_action_notify_inputs.json` 字节一致。

冷启动与普通 Editor 的 Overlay 通知数据 SHA256 均为：
`FBB81BDF00475F89849402B3F5A2B8137970E4DEBF264CA2D390D1C00D584131`。
Roll 重复输出 SHA256：
`A710F887C3FB3F82602152205B6454D1D90E2B3C828DB3752225D9844E8CC184`。

使用 ue-diagnosing-plugin-build-load 的全项目构建契约完成 Editor 构建、
插件审计与精确输入状态文件后才启动 UE。冷/普通 Editor 导出均退出 0，
未保存资产，未修改原生插件或配置，因此没有重跑插件打包/项目资产验证。
BuildId 保持 `a4192a27-77ab-4bb5-9996-c71b2d55f53d`，fingerprint 保持
`28DA3AAF4178852D44C6BF2F6EC0D9D739D7080CF0DA62B7C2E7C4B3A5EE30CD`。

## 验证证据

新增 `overlay_shared_frame_smoke.tscn` 使用真实完整 BaseLayer 与完整精确
Overlay 姿势图，共享同一来源批次，输入和 Slot 覆盖窗口受控。
30/60/120 Hz 共 1,260 帧通过：

- 1,176 次观测到步枪 follower 已推进，所在组由真正移动来源领导。
- 42 帧隐藏 Main Movement 但仍更新 Overlay，来源未被错误清空。
- 来源通知共 33 个；所有 1,260 帧取消重试后，事件身份与顺序、来源
  时钟/epoch/组历史和两图姿势/曲线一致。
- 17 次在移动图已求值后注入 Overlay 来源异常，已提交的移动身份和
  来源缓冲不变；随后同帧重试与未失败路径一致。

这是跨图运行与事务验证，**没有将它称为完整 UE 最终动画图对照**。
当前测试选择明确的收集顺序，并分别验证两图输出；尚未运行最终
LayerBlending 的全部缓存/更新遍历，也没有将两个输出合成为最终上身。

4 个新 Import 测试通过，覆盖 223/257 身份及 authority、原移动 ID 保持、
原生 inventory 覆盖、通知数据完整性，以及真实 WalkRun/Rifle 绑定的
一次同步和通知事务。低权重移动 leader 仍优先于高权重 follower；随后
提高移动权重越过原有通知阈值，验证真实脚步通知，未修改原版阈值。

相关 Core 回归 634 项通过。全量 Release Import 1,935 通过、1 既有跳过；
最终绑定/时钟/Overlay 姿势专项 20 项通过。Godot 优化 Debug 构建零警告、
零错误。原 BaseLayer 3,360 帧回归通过，包括 271 隐藏帧和晚期回滚。

默认 Worker 单/并行各 600 帧通过，业务摘要 `EAAF62E4D0A80A76`，
完整姿势 `EE519FBE375F4A2B`，根 `A4F6C26CBAB8A0E7`，各 28 个事件，
lag/stale 为 0。该回归保持旧默认入口；不能冒充默认上身已经切换。

原 Overlay 自有时钟的 886 帧 UE 姿势回放再次通过，26 个播放器时间
误差为 0，最终四元数距离仍为 `7.90745e-7`；原始夹具 SHA 保持不变。

初次联调暴露原 8 组容量不能容纳实际 10 组，已依据真实组表扩容。
测试还改用既有逐字段/span 比较函数，因为 Godot 所用运行时拒绝对
InlineArray 调用 ValueType.Equals。失败日志均保留，没有改期望姿势或
放宽数值门槛。

主要日志：

- `artifacts/overlay-shared-frame-comparison.log`：完整跨图联调结果。
- `artifacts/overlay-shared-binding-tests-final.log`：新绑定及共享 tick 测试。
- `artifacts/overlay-shared-final-targeted-tests.log`：最终 20 项专项。
- `artifacts/overlay-shared-core-regression.log`、`overlay-shared-import-regression.log`：相关 Core 与全量 Import。
- `artifacts/overlay-shared-base-layer.log`：原 BaseLayer 3,360 帧。
- `artifacts/overlay-shared-worker-single.log`、`overlay-shared-worker-parallel.log`：默认 Worker 各 600 帧。
- `artifacts/overlay-shared-overlay-native-regression.log`：原 Overlay 886 帧原生回归。
- `artifacts/overlay-shared-editor-build.log`、`overlay-shared-notify-native-console.log`、
  `overlay-shared-notify-editor.log`、`overlay-shared-action-notify-console.log`：UE 构建/导出。

## 当前边界与下一项

默认 Worker/Demo 仍使用原 75/109 BaseLayer 路径；本批组合变体已经在
完整两图联调中使用，但还未作为默认最终动画图发布。下一项是原版
最终图的统一初始化、缓存与更新遍历，Overlay 状态机通知消费者，再
将 Aim/Overlay/BasePoses/LayerBlending 和脊柱/手部修正接到正式最终姿势。
最后继续最终曲线反馈、完整脚锁/pelvis/平台及同输入/同脚相位多帧人工对照。

新增候选缓冲的容量成本需要纳入最终十角色预算。本批没有宣称联合最终图
的多角色性能或最终上身视觉已通过。既有全 Core 23 项失败、p95 2.559 ms
超过 2.5 ms 门槛，以及 P5A–P7 其余范围仍保留；音频暂缓。
