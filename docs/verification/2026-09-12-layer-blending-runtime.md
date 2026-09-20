# 动态上身 LayerBlending 图与运行时（第 102 批）

日期：2026-09-12。工作区：`../GodotALS-p5a-events-actions`。
承接 P4 动态上身分层，以及 P5A 前置的缓存和 Slot 语义。本批完成原生图数据、
严格编译和独立组件运行；共享定义已加载这些数据，默认 Demo 尚未改用新分层
输出。不能据此宣布双臂、侧身、换髋或滑步修复完成。没有 commit/revert/merge。

## 具体实现

`tools/unreal/export_layering_inputs.py` 从当前 ALS V4 AnimBP 导出 122 个 authored
图：LayerBlending、BasePoses、AimOffsetBehaviors、OverlayLayer、主 AnimGraph
及所需输入函数。正式文件为 `assets/config/v4_layering_inputs.json`，含 492 个
相关原生节点条目、缓存排序、CDO、骨架文本和 14 个原生 FFloor 样本。
导出覆盖范围不等于这些图都已实现；本批严格消费的是 LayerBlending 和
UpdateLayerValues/曲线辅助函数。

`AlsLayeringInputModel` 按原执行顺序读取上一已提交曲线，保留缺席/存在的区别、
Hand IK 与 Arm 权重相乘，以及 Arm_MS = 1 - Floor(Arm_LS)。例如 LS=0.99 时
MS 仍为 1，不能改成连续互补或额外钳制。编译器同时校验同帧 setter 读取、
函数作用域、宏连接和原生 float 到整数的边界。

`AlsLayerBlendingCompiler` 严格编译 80 个实际姿势节点，包括三个上游输入、
11 个保存缓存、34 个读取缓存、7 个 Slot、六个按曲线切换的 TwoWayBlend，
以及动态局部/网格空间加法与逐骨骼组合。保留原生分支过滤深度、负深度排除、
左右臂独立 LS/MS 权重、手指/手部虚拟骨掩码及真实 deferred cache 更新顺序。

`AlsLayerBlendingRuntime` 使用候选/已提交双状态，分别处理初始化、骨架缓存、
Update、独立 Evaluate scope、Commit/Cancel。普通节点不擅自共享求值，只有
原图 UseCachedPose 共享缓存；重复 Evaluate 不再次推进来源更新。初始化、
骨架缓存、更新或求值异常均丢弃候选，来源 owner 须同步取消其自己的候选。

曲线不直接沿用某个上游：动态加法保留曲线差分；逐层 Override 与 BlendByWeight
遵从不同语义。最后的空骨骼掩码仍恢复曲线；其输入通过 VB Curves 保留基础图
与 Overlay 的曲线贡献。缺少这根虚拟骨骼会被明确拒绝，不静默跳过恢复。
不相关分层子姿势使用真实参考姿势，以支持网格空间父骨累积。Slot 来源完全
隐藏时则传完整布局的确定性占位数据及 `sourceEvaluated=false`，Slot owner
必须忽略占位原子；UE 此时创建的是未求值 SourceContext，不能把占位姿势
描述成原生有效来源输出。

## 验证与证据

| 检查 | 结果 | artifacts 证据 |
| --- | --- | --- |
| UE Editor 完整构建及插件审计 | 退出 0，既有构建有效，三个项目插件通过 | `layering-ue-build.log` |
| 原生节点/cache inventory 导出 | 退出 0，包含 TwoWayBlend 的 BlendNode 默认政策 | `layering-inventory-export.log` |
| 最终冷导出 / 普通 Editor 重复导出 | 均退出 0，各 122 图，assets_saved=0；前后审计通过 | `layering-export-scoped.log`、`layering-editor-repeat.log`、`layering-editor-*-audit.log` |
| Core 输入、分层掩码和曲线 | 44/44；掩码/网格空间相关测试使用既有 UE 原生算子夹具 | `layering-core-initial.log` |
| Import 编译、输入与完整图运行 | 53/53，含 13 项运行/重复导出检查 | `layering-import-isolated.log` |
| Godot C# 构建 | 0 警告、0 错误 | `layering-godot-build.log` |
| 真实生产 single / parallel | 各 600 帧，上一批摘要保持 | `layering-production-single.log`、`layering-production-parallel.log` |
| 实际渲染键鼠回归 | 360 帧，真实 Alt/A/D、4 次鼠标事件，生产反馈身份通过 | `layering-keyboard-mouse.log` |

完整图测试使用原生探针的 79 骨层级和受控输入姿势/曲线/Slot；30/60/120 Hz
验证动态差分重建、上游实际更新顺序、11 个缓存只求值一次、局部 Slot 完全
覆盖、冷帧缺席曲线、失去相关性后重入、四阶段异常同帧重试、Cancel 和再次
Evaluate scope。独立暖后工作线程 100 帧全阶段分配为 0。它不是完整 Overlay
玩法或匹配 UE 输入的最终 AnimBP 骨骼逐帧对照，也不是十分钟性能验收。

单/并行 result=`DFA5F7A4F3296FA3`，full pose=`88AAD97FC78B8895`，
root=`A4F6C26CBAB8A0E7`，事件 28，lag/stale 0。生产输出保持是共享定义加载
的回归证据，不是新分层已进入 Demo 的证据。键鼠回归仍为 Alt 步行，无新
自由观察模式；walking=180、Alt 松开后移动=90、left=150、right=120。

普通 Editor 与冷导出的 LayerBlending、BasePoses、UpdateLayerValues、两项
曲线辅助函数原文一致，默认值/骨架/14 个取整样本一致；相关图严格编译结果
也一致。其余 22 个图存在重建文本差异，尚未证明全部语义一致。inventory
与 cache order 复用了同一冷导出输入，不称为普通 Editor 独立重导。
重复导出测试通过 `ALS_LAYERING_REPEAT_FILE` 显式指定真实导出；未提供时
默认单元套件将该集成项标为跳过，不依赖被 Git 忽略的 artifacts 文件。

正式 JSON SHA256：`252FEF5D29F05C07721FDB4EA3C01ED9C0E47A948ED4D47AB1F691C1FA59200E`。
重复 JSON SHA256：`AD5CDA3F1D13C079AECD0887EE9875AC55E10F5343A036CDDA60C941713DEE6F`。
UE BuildId=`369675c0-434c-4633-b2aa-532acf57bb8f`；完整构建 prefix=
`20260912T053835947Z-c7bb768ba1ac40df85b02f58810fb7de`。遵循 UE 构建/加载技能
先审计再启动；本批没有原生插件或资产修改，未重复 DataValidation/插件打包。

保留首错：Python FFloor 名称解析失败；一次输出成功后 Python 关闭期访问
异常（退出 3），不能称为成功运行；随后将原生对象生命周期收在函数作用域，
最终冷/普通 Editor 均退出 0。原生日志仍有既有 UnifiedErrorTest 两条自测
错误及可选模块旧 manifest 提示，不写成日志零错误。另保留 params 构造的
CS8752、错误的 Slot 空 span 测试假设，以及合并套件中一次 7936 字节计数。
分层编译关闭时合并套件通过；之后按既有 Import 测试方式隔离工作线程并暖机，
默认配置也 53/53，通过标准仍为零分配。未将该波动未经证明归因于 JIT。

## 下一项和完成标准

1. 补 BasePoses 的真实上游 owner。两个固定 0 秒 evaluator 必须有独立初始化、
   相关性与 tick 身份；MultiWayBlend 保留 Desired/CachedAlpha、归一化顺序、
   阈值和全零时参考姿势，不能复用 idle owner 或伪造 BasePose 曲线。
2. 在每个序列采样阶段补完整逻辑骨架。N/CLF 两个资产已导入，正式骨架有
   79 根逻辑骨、68 根实体骨、11 根虚拟骨；当前 clip/binder 仅采样实体骨。
   按源/目标组件变换构造无显式轨道的虚拟骨，再参与混合/差分/缓存；不能
   全填 identity，也不能在最终混合后重建全部虚拟骨替代原生语义。
3. 完成真实 OverlayLayer、AimOffsetBehaviors 及主图外围接线。三个上游均
   提供完整骨架和曲线，并共用来源/事件/提交所有权后，再替换 Demo 的旧
   上身路径，将反馈来源从 BaseLayer 移到真正最终输出。
4. 随后闭合脚部曲线 presence、Foot IK/Lock、pelvis 和平台消费者，执行真实
   支撑脚滑移、横移侧身、A/D 换髋及 UE/人工验收。继续原 P5A 剩余动作链、
   P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、P6 恢复/完整 Camera、
   P7 十分钟预算；本批不缩小任何原目标，音频仍暂缓。
