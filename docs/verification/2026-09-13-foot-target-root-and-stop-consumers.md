# 脚锁目标根混合与停止通知缺口（第一百六十二批）

## 本批完成

上一批属于有进展：实际输入原生回放排除了脚锁函数的数值偏差。本批继续
检查输入所有权，修复普通动画与 Ragdoll 同时相关时的独立目标反馈，并核实
原生参考轴。另发现原版停止通知到动态 Montage 的生产链缺失，下一步必须
补该 P5A 前置能力，不能仅改变防扭转参数。

`AlsBasedFootLockFrame.CaptureBlendedTargets` 以最终根的实际普通分支权重，
在局部骨骼空间混合普通分支的 pre-controller 姿势与 Ragdoll 姿势，再重建
IK 脚目标和骨盆方向。`AlsLayeredAnimationFrameRuntime` 仅在两个根分支同时
被求值时提供这两份姿势和权重。

不能直接读取最终根中的 IK 脚骨：V4 控制器会把脚锁 Final 写入 ik_foot_l/r，
直接反馈会把锁定结果误作下一帧动画目标。也不能只混合两个组件空间脚点，
因为父骨骼旋转后的结果不同。新混合只更新 Target/Pelvis 历史，不替换模型
已经计算的 Final。隐藏分支沿用完整替代输出，普通独占路径保持不变。

缓冲区在构造时分配；混合路径跟随原候选/提交/取消事务，不引入第二个时钟
或根状态机。两个骨骼空间、父节点旋转、已改写 IK 骨不回流、取消/重试和
下一帧消费均有专项覆盖。

## 原生参考轴证据

探针在 `UAlsAnimationInstance::InitializeAnimation` 后、覆盖回放状态前，
直接保存实际 Mesh 初始化得到的大腿轴。Godot 使用 Skeleton 参考姿势得到
的两轴与它们的向量差分别为 1.79339e-8 和 8.16633e-8，门槛 1e-6。
故 Mesh/Skeleton 大腿轴来源差异不是第 616 帧跳转的解释。

`artifacts/based-foot-native-162.json` 与 `artifacts/based-foot-editor-162.json`
完整内容相同；1440 例与 66 个约束隔离变体完成。最大位置差仍为
0.0000305176 cm，旋转差 0.0000456589°。原生同样产生 41.10° 跳转。
比较报告：`artifacts/based-foot-comparison-162.json`。

## 已确认的停止播放缺口

原 V4 的 baked Stop 状态机中，Lock/Plant Left 的进入通知都是
`->N Stop L`，右侧对应 `->N Stop R`。原 EventGraph 的两个通知分别直连
`PlayTransition`，不是经过 CanOverlayTransition 的 Overlay 通知。

| 通知 | 原资产 | BlendIn/Out | PlayRate | StartTime |
| --- | --- | --- | --- | --- |
| ->N Stop L | ALS_N_Stop_L_Down | 0.2 / 0.2 s | 1.5 | 0.4 s |
| ->N Stop R | ALS_N_Stop_R_Down | 0.2 / 0.2 s | 1.5 | 0.4 s |

该公共播放函数使用 Grounded Slot、一次循环、BlendOutTriggerTime=0。
原始依据是已导出的 `v4_anim_graph_inventory.json` 中的执行连线，以及
`v4_overlay_transition_inputs.json` 完整原生 EventGraph 中的资产对象和参数。
后一个文件虽然以 Overlay 命名，实际包含这两个停止消费者，不能只依据
文件名认定整份 EventGraph 的消费者都已实现。

当前 `AlsGroundedFrameRuntime.UpdateStopSources` 只观察骨骼缓存；停止状态
更新虽包含通知，尚未接入它们的 Montage 播放分发。当前正式
`v4_movement_source_inputs.json` 的 raw bank 也不包含上述两项停止资产。
`AlsOverlayTransitionCompiler` 的八项绑定只覆盖 Overlay 边事件；现有
`PlayOverlayTransitions` 不能当成已经支持这两个无 Overlay 门控的停止通知。

渲染记录中第 610 帧进入 Stop/Plant Left，第 611 帧 Standing 已转 Not Moving，
第 616 帧进入满锁。缺失停止播放会使该区间的姿势与曲线不完整；是否足以
解释并修复 41.10°，必须在实际接线后复跑验证，当前不作已修复结论。

## 两版停止资产的直接检查

`export_foot_lock_source_contract.py` 导出 Refactored Standing 原生图和四项
停止资产，每项 120 Hz / 281 点，共 1124 个样本。骨姿态使用 RAW、retarget，
不提取 Root Motion、加法动画恢复为完整姿势；曲线保留引擎 API 返回值。
加法曲线可为负值，不能在资产采样阶段统一截到 [0,1]。

两版本并非简单改名：在 1.325 s，V4 左停止的 FootLock_L 为 -1，Refactored
对应 FootLeftLock 约为 -0.2499991；右停止也有相同幅度差异。完整样本的
最大差为 0.750000894。这是单片段提取对照，不能直接当成完整图最终曲线。
停止图自身还会写入锁曲线，Slot 混合后的最终曲线才应进入脚锁更新。

有效冷导出：`artifacts/foot-source-contract-162-final/`；普通 Editor 的
显式求值选项和资产加法元数据导出：`artifacts/foot-source-contract-162-editor/`。
两次四资产的 1124 个姿势/曲线样本完全相同；四项均为 RotationOffsetMeshSpace，
加法基准类型 AnimFrame、第 0 帧。原生记录包含各自实际基准资产路径。
审计工具：`tools/diagnostics/audit_stop_source_contract.mjs`；报告
`artifacts/stop-source-contract-audit-162.json`。首次导出因 Python 大小写判断
把原生 FName 骨名误判缺失而失败，原日志保留；修复为按 FName 的忽略大小写
语义查找，没有补造骨骼或输出。

## 验证边界

- Import Foot IK 54 项通过，Core 脚锁/Root/Ragdoll 53 项通过；107 项合计。
  日志在 `artifacts/tests/foot-root-feedback-162-final.trx`、
  `artifacts/tests/foot-root-model-162.trx`。
- 优化 Debug 项目构建 0 警告/0 错误。实际生产 single/parallel 各 960 帧通过，
  摘要仍为 52FD88C3010C8E84，完整骨骼摘要 363B466D6421AB00。
  该场景覆盖移动/跳落/蹲姿与代际恢复，不是物理 Ragdoll 混合验收。
- UE 完整 Editor-target 构建、依赖审计、冷回放、正常 Editor 重启、项目
  DataValidation、带 ALS 依赖的隔离 BuildPlugin 和打包后审计均 exit 0。
  本批构建 fingerprint：
  B7A8441C62CB65CE00E4A99EC00155C290BC221799F392572B6369CDFF3B7B31。
  包位于 `artifacts/unreal/AlsBasedFootPluginValidation-20260913-162`，未部署 DLL。
  旧 AI/NavMesh 等项目兼容警告继续保留，不宣称引擎所有日志无警告。
- 本批没有修改普通独占移动路径的脚锁规则，未重新关闭第 616 帧视觉失败。
  根混合目标修复通过的是组件与生产回归，不是完整物理恢复/人工验收。

## 下一步实施

先补 Stop 状态通知的有序收集、初始化/相关性规则与统一候选提交，将两项
停止资产的真实原始动画、加法基准、曲线和通知加入资源闭包。复用共同
Grounded Slot Montage 所有者，按原参数在图求值后分发，并与 Overlay/Turn
同帧播放顺序、组互斥、中断、晚期失败重试一起验证。随后复跑横移/停止
720 帧与平台测试，确认视觉问题是否消除。

默认完整根、P3/P4 实际接触/相位与人工验收仍未关闭；继续 P5A 剩余通用
分发、P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 物理恢复/完整
相机、P7 十分钟性能。音频暂缓，完整目标不缩减。
