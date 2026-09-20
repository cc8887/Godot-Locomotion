# 停止通知、Montage 与整图连续对照（第一百八十五批）

## 本批结论

第 184 批已经完成 Plant 来源和选脚合同，属于有进展。本批继续补完整性
验证中的实际缺口：旧 UE 整图探针不推进 Montage，也不执行停止通知，
因此无法验证已经接入 Godot 生产图的 Stop 播放。现在加入原始通知消费者
与连续原生 Montage 历史，六组 2100 帧的停止通知、播放和姿势对照通过。

本批修改验证入口，没有调整生产 Stop 参数、替换资产或关闭脚锁约束。
第 616 帧 41.10° 突转仍未关闭。受控 V4 图对照通过不等于实际 Motor、
Refactored 脚锁和场景的整链验收通过。

## 实现

`ExportFullGraphTrace` 增加显式 `dispatchStopNotifies` 模式。一个 trace
始终使用同一个真实 ALS_AnimBP 实例，不逐帧重建，也不覆盖它的状态机、
曲线历史、Montage 播放位置或混合权重。

- 调用原生 PreUpdateAnimation，重置通知分发标记和事件队列；调用原生
  UpdateMontage、UpdateMontageSyncGroup、UpdateMontageEvaluationData，
  然后更新原始动画图。
- 调用原生 PostUpdateAnimation，按引擎顺序清除 NeedsUpdate、翻转缓存
  并合并通知。仅调用 Proxy PostUpdate 不足以满足真实通知分发条件。
- 求值和最终曲线发布后，从原生通知队列保留 `->N Stop L/R`，经原生
  DispatchQueuedAnimEvents 执行原始 Blueprint 消费者。播放参数和触发
  时间不是 Godot 注入，也没有在探针里重写 PlayTransition。
- 每帧记录用于本次姿势的冻结 Montage 求值数据，以及本帧末停止通知。
  引擎自行保留播放/淡出历史，并在事件分发阶段清理结束实例。

受控 Actor 不执行脚步声音、其他玩法通知、Character Motor、完整
Blueprint UpdateGraph 或真实物理；这些限制写入输出 scope。旧无 Montage
模式保持独立，不能将两种模式的证据混称。

私有的两个引擎入口通过参考项目现有 AlsPrivateMemberAccessor 调用，
没有修改引擎源码、内存布局或将真实实例强制转换成伪造派生对象。

Godot 的 `AlsFullGraphParityCapture` 新增 `--parity-stop`：六组回放延长
到五秒，覆盖停止动画完整淡出，增加真实通知/物理 Montage 的序列化。
原测试 sink 错误地要求 Grounded Slot 永远 Passthrough，停止动作实际
播放即报 Escaped physical Grounded slot。本批为整图回放增加独立观察器，
核对权重和帧身份确实来自同一个物理 Montage 所有者，不再假定零权重。

## 数值与连续历史验证

30/60/120 Hz、初始左/右横移，共六组 2100 帧。Godot 每一帧执行取消和
重试，验证姿势、曲线、来源时钟、事件及已提交历史保持。所有用例运行
真正完整 V4 动画所有者；全局动画属性由 Godot 候选提供给 UE，因而这是
图与播放链对照，不是独立验证全局输入公式。

| 频率 | 停止通知帧 | 每个方向的实际 Stop 求值帧数 |
| --- | --- | --- |
| 30 Hz | 76 | 44 |
| 60 Hz | 151 | 89 |
| 120 Hz | 301 | 178 |

两边各自的原始图生成同一方向、同一帧的通知；实际动画资产、Slot、播放
位置、权重逐帧相同，时间和权重最大差均为 0。末帧都已结束播放。
比较器还逐帧核对 UE previousCurves 来自它自己的上一最终输出，没有
按 Godot 结果重置 UE 历史。

沿用已有姿势/曲线门槛，没有放宽：位置 0.001 cm、旋转 0.02°、scale
0.00001、曲线 0.0001。MainMovement、BaseLayer、最终姿势全部 2100 帧
通过；最终最大位置差 0.000072582 cm，旋转差 0.000478388°，曲线差
2.38419e-7。比较器记录 6466 个输入正负零编码差，非零输入不使用容差。

主要产物：

- `artifacts/stop-full-185-godot.json`、`.request.json` 与 `-godot-final.log`。
- `stop-full-185-native.json`、`-lifecycle.json`、`-pose.json`。
- `stop-full-185-main-movement.json`、`-base-layer.json`。
- `stop-full-185-editor.json`、`-editor-lifecycle.json`、`-editor-pose.json`。
- `stop-full-185-repeat.json`：冷启动与普通 Editor 解析后值完全相同；
  6114 个对象的键顺序不同，正负零差 0，故原文件并非字节一致。

## 构建与工程验证

Godot 优化 Debug 构建 0 警告/错误。首次回放因旧 Passthrough 断言失败，
原 `stop-full-185-godot.log` 保留；修正后 `-godot-final.log` 退出 0。

UE 完整项目 Editor 构建与四插件审计通过，BuildId 保持
`8531669e-23fc-4bc3-9bab-fd5876472ee1`，输入指纹为
`53EA1C3A903047C5814FEDDD643ED79775610AEDA277B46ECC9FC39E30D07E60`。
构建日志前缀 `20260913T173556674Z-2db0da6450f343e99415a74aa17b6bf1`。
首次编译的私有访问、重载及 TObjectPtr 推导错误保留于前一构建日志
`20260913T173255695Z-b62641cc1af64d4d95dbca7200a11f3f`，已修正。

冷启动和普通 Editor 两次导出均退出 0。正常 Editor 的两条既有
AutomationTest Condition failed 仍存在。DataValidation 退出 0、0 错误/
3 警告，仍是旧 AI PawnActionsComponent 和 Navmesh 版本问题；未修改资产。
Node/Python 语法及差异空白检查通过。插件在全新
`artifacts/unreal/stop-full-185-package` 中隔离打包成功，耗时 2 分 16 秒，
退出 0；检查包内插件描述符与 DLL 已生成。打包后项目四插件再次
AUDIT_PASS，没有将打包产物覆盖部署到项目。仓库和项目部署的探针源码
SHA-256 均为 `E171D6590A703D905BC6159DA4F2BC0F29721904C8C202B4581FD8EB4BAC27A4`。
本批启动的 Editor、commandlet、构建和打包进程均已退出。

## 下一项

现有证据不支持继续修改受控 V4 Stop 图的播放参数来解决 Refactored 满锁
突转。下一项应直接从真实 Demo 抽取相同帧身份下的动画输入和分阶段输出，
保留实际减速、角色/组件变换、父状态混合、最终曲线和脚锁上一 Final。
先验证真实 Motor 场景的 pre-foot 上游，再对照 Refactored 捕获及虚拟骨
约束所需的完整版本语义。不能以本批控制属性回放代替实际场景。

默认完整入口、起步/换髋/上身/脚部整体验收、Core 23 项历史失败、Import
跳过与未归因栈溢出仍开放。原 P5A–P7 完整范围继续，音频暂缓。未 commit、
合并或回退用户现有改动。
