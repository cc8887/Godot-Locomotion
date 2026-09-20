# 停止→原地转身→待机整图对照（第 187 批）

## 结论

上一批修复了六方向 BlendSpace 共享输入滤波历史的实际缺口。本批补齐
UE 整图探针的原地转身消费者，将真实 Motor 回放延长到 1080 帧，覆盖
停止、转身、淡出及待机。MainMovement/BaseLayer、共享 V4 曲线、Montage
播放和来源时钟全部通过。没有修改生产动画算法或放宽姿势门槛。

这完成 P3/P4 当前固定横移场景的主移动后段对照，不代表完整上身、脚部、
平台和全部玩法已验收。默认完整入口仍未切换，原 P5A–P7 范围保留。

## 原始控制函数和更新顺序

`ExportFullGraphTrace` 增加显式 `runIdleControls` 模式，要求同时开启
原 Stop 通知分发。临时创建原 ALS_Base_CharacterBP 角色，绑定到临时
AnimInstance 的 Character 属性，仅设置本次观测到的角色 yaw。

在 Grounded 不移动分支中调用原始 `CanRotateInPlace`、`CanTurnInPlace`、
`RotateInPlaceCheck`、`TurnInPlaceCheck`；后者调用原始 `TurnInPlace`，
自行选择动画、调用 IsPlayingSlotAnimation、创建动态 Montage、更新
RotationScale。仅桥接原 UpdateGraph 的移动/静止分支与重置操作；移动
分支 DoOnce 历史在离地期间暂停。没有把 Godot 的转身命令或选中资产
注入 UE，也没有执行完整 Blueprint UpdateGraph。

探针拒绝外部写入 Rotate_L/R、RotateRate、RotationScale、ElapsedDelayTime，
由原生实例持有这些状态及曲线反馈。Godot 采集另外记录这些候选输出用于
比较，输入只含实际全局属性、控制器朝向、视角模式、AimYawRate 和角色 yaw。
Godot、UE 骨骼/场景坐标的现有区分保持。

按引擎 `AnimInstance.cpp` 的 UpdateAnimation：PreUpdate → UpdateMontage →
UpdateMontageSyncGroup → UpdateMontageEvaluationData → Blueprint 全局更新 →
图更新/求值 → 通知。探针在原控制函数调用前冻结 Montage；新转身请求
到下一帧才参与姿势。本批第一次构建后在运行前发现顺序问题，依据源码
修正并重新完整构建，未将错误顺序用于配对通过结果。

`--production-idle-capture` 配合现有 `--production-graph-capture` 启用这套
输入；旧 Stop-only 模式仍保留。场景增加 `--capture-frames=1080`，默认
720 不变，允许 720–3600 且要求截图间隔整除。比较器新增完整转身门槛：
双方必须实际播放转身、末帧全部 Montage 结束、不能排除不支持的后段。

## 结果

| 检查 | 证据 |
| --- | --- |
| 诊断不改变生产动作 | 原 720 帧输入、结果、Cycle、脚部与锁历史相同；1080 回放前 720 帧图快照与短回放完全相同 |
| 实际渲染 | 1080 帧、90 PNG，通过；最大单帧脚旋转 13.266°，未改阈值 |
| 原始停止与转身 | 第 610 帧 Stop R；第 685 帧 RotationScale 首次改变，第 686–796 帧转身求值；之后 284 帧待机 |
| 动作完整生命周期 | 86 次 Grounded Stop + 111 次 Turn，共 197 次求值，资产/Slot/播放位置/权重全部一致；末帧均无 Montage |
| 两阶段全部 1080 帧 | 位置最大差 0.000042817 cm，旋转最大差 0.000068045°；门槛仍为 0.001 cm / 0.02° |
| 共享 V4 曲线 | 最大差 4.76837e-7；门槛 0.0001 |
| Idle 控制 | 布尔值相同，数值最大差 1.69501e-7；显式记录 float/double 比较门槛 1e-5 |
| 来源 Tick | 2640 次，时间最大差 0，权重最大差 2.98023e-7 |
| 并行事务 | 新采集开关下 360 帧、两次取消、一次提交等待通过；pose=51AEDD1A58842239、result=A033A3C197DDDF19，与上一批相同 |
| UE 重复 | 720 与 1080 两组均完成冷启动和普通 Editor，全部退出 0，解析后完整输出相同 |

此场景没有实际 Rotate_L/R 激活，也没有 crouching Turn，不将原函数已经
接入视为这些分支已完成整图验收。全局输入仍来自 Godot，不能据此独立
证明 UE Character Motor/旋转积分。DynamicTransitionCheck、其他玩法通知、
真实 UE 物理不在本探针范围。

六条 Refactored 专用曲线仍无 V4 同名输出：FootLeft/RightIk、
FootLeft/RightLock、PoseGrounded、PoseMoving。有限范围报告逐项列出；
严格全量曲线比较仍失败，没有把缺失视为零。严格 720 帧 MainMovement
比较现已是 0 姿势失败/720 曲线存在性失败。最终脚部和手部后处理也没有
因为主移动通过而记为通过。

## UE 构建、部署和日志

使用 `ue-diagnosing-plugin-build-load` 的完整 Editor 构建与四插件审计。
最终构建/审计均退出 0；打包完成后再次审计通过。

- BuildId：`8531669e-23fc-4bc3-9bab-fd5876472ee1`。
- 最终构建日志前缀：`20260913T182312572Z-a23a6dbfe569434fa2fed8f568b9d7df`。
- 输入指纹：`2D9D5AD0C21ED4746F811D718EA57AC97C62AB896F937DDC5F39534DD3F53B8E`。
- C++ 源文件仓库/部署/包三份 SHA256 均为
  `96ED30C69ED2568409448619E3FB5382F2B29E87D5509EE0C1C5AD462ECD21F0`。
- DataValidation：退出 0，0 errors、3 条既有 warning。
- 独立 BuildPlugin：`artifacts/unreal/idle-full-187-package`，成功，约 2 分 30 秒。
  编译曾因内存不足被 UBA 终止并自动重试，随后成功；不是一次无重试构建。
- 普通 Editor 保留两条既有 LogAutomationTest Condition failed，不能称干净日志。
- Godot 构建 0 warning/0 error；Python 语法、Node 语法与 diff 空白检查通过。

缺失的 systematic-debugging/verification-before-completion 技能仍未找到，
使用现有源码、完整构建、审计和运行产物验证，没有据技能缺失暂停实施。
本批不重复计入旧 Core 23 项失败或旧缓存测试 79/68 骨布局失败的清理成果。

## 产物与下一项

- `artifacts/movement-187-complete/`：1080 帧输入、图/曲线/来源/控制快照及 90 PNG。
- `movement-187-complete-native.json`、`-editor.json` 及对应日志、provenance。
- `movement-187-complete-scoped.json`、`-clocks.json`。
- `movement-187-idle/`、`movement-187-scoped.json`、`movement-187-main-strict.json`：720 帧原范围。
- `movement-187-validation.log`、`graph-dispatch-187-idle.log`。

下一项转入 Aim、LayerBlending 到最终脚部/手部之前的阶段配对，定位剩余
上身和脚部差异；继续 Rotate、蹲姿、接触/平台及人工验收，之后切换默认
完整入口。P5A 通用动作、P5B Overlay/道具、P5C、P6、P7 均保持原范围。
