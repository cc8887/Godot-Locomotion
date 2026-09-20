# 起步姿势的分段对照与惯性化隔离

第一百五十一批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。
本批完成原因定位和可重复诊断，尚未修改正式动画算法或关闭最终姿势门槛。

## 原生分段捕获

`ExportFullGraphTrace` 增加可选 `captureStages`。在原 BaseLayer 的
Inertialization.Source 与 Root.Result 连接上，在正常 Evaluate 期间
临时包装原节点，直接转发同一 FPoseContext，记录返回姿势/曲线。
每帧恢复原指针；不替换资产，不修改 Update/Initialize/CacheBones，
不对缓存或来源进行第二次求值。原生每个采样点每帧必须恰好求值一次。
两点命名为 MainMovement（惯性化之前）和 BaseLayer（惯性化及 Slot 之后）。

Python 通过 `ALS_FULL_GRAPH_STAGES=1` 请求采样；Godot 对应
`--parity-stages`，读取正式所有者既有输出缓冲区。比较脚本增加可选
阶段参数，仍使用原位置 .001 cm、角度 .02°、缩放 1e-5、曲线 1e-4
门槛，仍要求完全相同的逐帧输入和曲线存在性。

去掉新增 stages 字段后，Godot 新捕获与第 150 批完整 JSON 递归相等，
UE 新捕获与原 `full-graph-ue-148-tail.json` 递归相等。不是只比较摘要
或放宽姿势容差。普通 Editor 重启导出与冷启动导出的全部分段数据也
递归相等。因此本组 1260 帧没有被探针改变输出、来源或状态。

## 同属性分段结果

| 路径 | 超阈值帧/总帧 | 最大位置差 cm | 最大旋转差 ° | 曲线 |
| --- | --- | --- | --- | --- |
| 正式 MainMovement | 0/1260 | .0000652360 | .0000386374 | 全部通过 |
| 正式 BaseLayer | 21/1260 | .0000652360 | .0586701989 | 全部通过 |
| 最终完整根（沿用同值基线） | 30/1260 | .0285769052 | .0586825147 | 全部通过 |

正式 BaseLayer 首次分歧位于起步第 16/31/61 帧附近。主移动输出已在
原严格门槛内，经过惯性化/Slot 的区段产生旋转分歧；最终链还会传播或
产生其他差异。此阶段结果不能直接归因全部剩余根姿势，也不能据此关闭
平台支撑、实际角色同输入或人工视觉验收。

报告 `artifacts/full-graph-parity-151-main.json`（退出 0）、
`full-graph-parity-151-base.json`（退出 1）；输入
`full-graph-godot-151-stages.json`、`full-graph-ue-151-stages.json`。

## 惯性化旋转精度隔离

新增测试辅助 `AlsNativeInertiaDiagnostic` 和捕获参数
`--parity-native-inertia=<native stage JSON>`，仅在测试捕获中使用。
为两份独立惯性化实例供给同一 UE MainMovement 原始姿势及正式 Godot
图产生的惯性化请求。一份使用对应单精度投影，一份使用原始 double
旋转与 `EvaluatePrecise`；输出不反馈给正式所有者，也不参与游戏渲染。
没有将 float 四元数再次归一化后冒称其为 UE 原始旋转。

| 相同原生输入与实际图请求 | 超阈值帧/总帧 | 最大旋转差 ° |
| --- | --- | --- |
| 单精度输入/历史/差量路径 | 17/1260 | .0531560810 |
| 原始双精度旋转输入/历史路径 | 0/1260 | .0000059151 |

两条路径最大位置差均 .0000076380 cm，曲线差为 0。证据
`full-graph-parity-151-NativeInertiaSingle.json`（退出 1）、
`full-graph-parity-151-NativeInertiaPrecise.json`（退出 0），诊断捕获
`full-graph-godot-151-inertia.json`。去掉 stages 后，该捕获与正式第
150 批数据仍完全相同，证明隔离没有偷偷修正正式输出。

本地 `AnimNode_Inertialization.cpp` 与已有第 116 批实现相符：前两次
旋转历史、相对四元数和应用结果保留 double，差量轴/角度/速度有原生
float 边界。正式 BaseLayer 目前调用单精度 Evaluate；Overlay 已有
精确链不能证明 MainMovement/BaseLayer 也已接入。

## 构建、冷启动和重启验证

遵循 `ue-diagnosing-plugin-build-load` 的完整 Editor 构建契约，项目
没有运行中的 Editor 时修改本地插件源，仓库与项目源 SHA256 一致：
`1BF45D3459262E3C77DF5D72514665427368CA84084ED2C817F64F82F73CECEF`。
未复制 DLL，未修改 BuildId，未走旧部署脚本的覆盖插件路径。

完整项目 Editor 4 个 action 构建成功，AlsGodotExporter/AutoTestTools/
BlueprintLisp 审计全部通过；BuildId 保持
`4855b08e-0078-431b-b819-d1a7a48b513d`，fingerprint
`CC596A2A3B406B414C02E0152572EE86D4CD52967404B31E335F61E2C70C2B7A`。
日志前缀 `D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T043226931Z-821b9284084f44ddaf45ef0ec97d184f`。

冷导出退出 0，0 error/0 warning，日志 `artifacts/unreal/full-graph-ue-151-stages.log`。
普通 Editor 使用新二进制导出后退出 0，日志 `full-graph-editor-151-stages.log`；
仍有两条既有 AutomationTest Condition failed，不能称普通 Editor 全日志零错误。
DataValidation 退出 0，0 error/3 warning，日志 `full-graph-datavalidation-151.log`。
隔离 BuildPlugin 成功，包位于 `artifacts/unreal/AlsFullGraphStagesPluginValidation-20260913-151`，
没有部署该包的 DLL。打包后项目插件审计再次通过。

Godot 优化 Debug 构建 0 警告、0 错误；JavaScript 语法和本批空白检查
通过。Python 导出在冷/普通 Editor 均实际执行成功。本批未重复生产
960 帧或渲染截图；正式图值与原基线相等证据只覆盖本组受控回放。

## 下一项正式实现

从真实原始采样、Standing/Detail/Grounded/MainMovement 的姿势混合开始
保留精确旋转，在 BaseLayer 惯性化输入、候选复制和两帧历史之间不中途
截断，并保留 UE 差量的原生 float 边界。验证过程先使用本批分段门槛，
再回到最终根、Sprint、反向、多线程及事务回滚。不能把“给定 UE 姿势
的惯性化通过”当作真实 Godot 图已通过，更不能拿它替换运行时采样。

默认仍 BaseLayer；原 P3/P4 最终视觉和实际 Motor/平台验收、P5A 剩余
通用事件/动作、P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/恢复/完整 Camera、P7 十分钟性能均未完成。既有 Core
23 项失败、Import 分配不稳定和旧 p95 超预算未关闭，音频暂缓。
本批未 commit、revert 或合并。
