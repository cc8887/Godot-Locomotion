# 原版 V4 完整动画图回放导出

第一百四十六批，2026-09-13。仓库 `D:/GodotALS-p5a-events-actions`。

## 原规划归属和本批作用

承接 P3/P4 整链验收，补足此前只有局部状态机/节点原生对照的缺口。
新增 `ExportFullGraphTrace` 和 `tools/unreal/export_full_graph_trace.py`，直接加载
本机 V4 的 `ALS_AnimBP` 和 Mannequin，不替换根节点连接，不使用
ALS-Refactored 的角色轨迹冒充当前 V4 资产的对照真值。

这是完整 AnimGraph 的**受控属性回放**。它不执行 Character Motor、Blueprint
UpdateGraph、通知玩法、物理或 Montage 命令。未提供属性保持原实例默认值或
前次输入值；不会自动从最终曲线执行 UpdateLayerValues/UpdateFootIK 等函数。
因此本批不是完整角色的相同键鼠输入测试，也没有完成 UE/Godot 的配对比较。

## 实现

- 每条轨迹新建临时实例，保持原编译动画图；使用原生 PreUpdate、Update、
  同步 tick、PostUpdate 和 `ParallelEvaluateAnimation`。后者负责正式缓存姿势
  作用域，不能用单独的 Proxy Evaluate 替代。
- 输入为 delta 和显式属性集合。仅允许布尔、有限数值、合法枚举和数值结构；
  结构字段使用 UE 精确反射名。不接受动画节点结构或对象/资产引用替换。
- 输出每帧完整 79 骨骼局部姿势（UE 坐标、厘米、RAW retargeted）、曲线
  presence、上一帧最终曲线、状态机当前状态/时间/权重/过渡，以及来源节点
  身份、时间、缓存权重、同步组和 BlendSpace 样本。
- 来源 `weight` 是引擎的 CachedBlendWeight，隐藏时可能保留上次值，不能
  将其独立解释成当帧实际访问权重。状态 `recorded` 单独输出。
- 最终曲线通过 UE 的 `UpdateCurvesToEvaluationContext` 发布，再供下一帧
  GetCurveValue 读取，包含曲线消失的处理。
- 脚本校验输入回显、骨骼数量、旋转归一化、有限值、曲线历史连续性和姿势
  确实变化；保存请求摘要、引擎版本、AnimBP/mesh 文件摘要、脚本及输出摘要。
  两个资产摘要不是整个依赖资产闭包的摘要，不据此声称全部资源已冻结。

默认夹具在 30/60/120 Hz 分别运行左右两个方向的起步、反向、停步，共六条
轨迹、1,260 帧。VelocityBlend/播放倍率等由夹具指定，不是原角色运动公式
产生的输入；这些轨迹用来验证导出链可用，不用于证明运动速度或起步滑步正确。
`ALS_FULL_GRAPH_REQUEST` 可提供另一份绝对路径请求，支持后续接入 Godot
逐帧属性记录；`ALS_FULL_GRAPH_OUTPUT` 指定输出。

## 已完成验证

冷启动原生导出退出 0，1,260 帧通过脚本语义检查，日志报告 0 error、0 warning。
产物 `artifacts/full-graph-native-146-fixed.json` 及同名前缀 request/provenance；
日志 `artifacts/unreal/full-graph-native-146-fixed.log`。
Python 语法检查和本批文件空白检查通过。

普通 Editor 重复导出 `artifacts/full-graph-editor-146.json`，同样 1,260 帧，
正常退出 0。两个输出的 JSON 对象字段排列不同、文件 SHA256 不同；按属性名
递归比较全部内容，差异为 0，不是通过放宽浮点容差得到的一致。
每帧枚举 20 个状态机和 233 个原生资产节点；这是编译节点清单，包含隐藏节点，
不能与 Godot 共享批次中的活动来源数量直接作减法认定缺失。
普通 Editor 仍记录两条既有 `AutomationTest: Condition failed`，以及旧 AI
组件/导航网格兼容警告；没有将退出 0 描述为普通 Editor 全日志无错误。

完整项目 Editor 构建、三个项目插件审计通过，最终 BuildId
`45610888-9073-4f06-8e52-f179fc364eb9`，输入 fingerprint
`057D5EF0C40B7D20E713C3A53D2ED53EF3DA41C89FC301931B92D7AA0160A6C2`。
隔离 `BuildPlugin` 退出 0，产物位于
`artifacts/unreal/AlsFullGraphPluginValidation-20260913-146/`，没有将该包的 DLL
覆盖项目 DLL；打包后项目审计再次通过。
DataValidation 退出 0、0 error、3 个既有 warning，日志
`artifacts/unreal/full-graph-datavalidation-146.log`。

## 首次失败与恢复

系统 .NET 路径缺少 UBT 所需 .NET 10，使用引擎自带 win-x64 运行时完成构建。
第一次编译发现堆曲线和帧曲线不能直接赋值，已使用原生 CopyFrom。
该次 UBT 同时重建了 NetCore，引擎 BuildId 变化而项目构建未完成，后续审计
正确拒绝旧 receipt。确认生成产物范围后，通过构建技能的恢复流程将旧 receipt、
三个插件的旧映射 DLL/PDB/manifest 移至
`D:/AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260913T023252372Z/`。
没有删除源码或资产，旧二进制可恢复，但恢复时必须保持整组引擎构建身份一致。

首次冷导出因直接 Proxy Evaluate 缺少 CachedPoseScope 触发原生断言，保留
`artifacts/unreal/full-graph-native-146.log`。已改为正式实例求值入口，重新完整
构建、审计并成功导出，没有禁用缓存节点或修改引擎断言。

## 下一步

将 Godot 完整生产入口的已计算属性、来源身份和最终姿势映射到此请求，先做
UE/Godot 相同动画属性的配对，找出首个分歧帧；再向前核对角色/全局属性计算，
向后量化真实接触窗口的脚部相对平台位移。继续起步、A/D 换髋、胸髋/手臂
与平台多帧验收，通过后切完整默认入口。

默认仍为 BaseLayer。本批未修改 Godot 动作算法，不能关闭交错步、上身或
滑步问题。原 P5A 剩余通用动作/通知、P5B Overlay/道具、P5C Mantle/Roll/
Root Motion、P6 物理恢复/完整 Camera 和 P7 十分钟性能仍继续保留；音频暂缓。
已有 Core 23 项失败、Import 分配不稳定、旧 p95 超预算未在本批重验或关闭。
