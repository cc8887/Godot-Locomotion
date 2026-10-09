# Kimodo 完整 ALS 资源生成规格分析（2026-10-07）

## 本批范围

用户要求先依据 ALS 原理分析资产作用，再给出各项 Kimodo 生成约束。本批交付设计与逐资产规格，没有批量生成、覆盖运行资产、实现资源版本切换或提交/推送。修改前检查 Git 状态，保留既有源码、资源工具、文档与诊断修改。

设计入口：[生产设计](../architecture/kimodo-als-resource-production.md)。逐项阅读：[资产清单与约束](../architecture/kimodo-als-asset-catalog.md)。结构化规格：[JSON](../architecture/kimodo-als-asset-contracts.json)。可复核生成器：`tools/unreal/build_als_kimodo_asset_contracts.py`。

## 静态证据与方法

- V4：当前 `assets/generated/als_v4/als_manifest.json` 的 126 AnimSequence 身份；对应 `assets/config/raw_sequences` 中原评价策略、基底、时序。
- Refactored：当前 `assets/config/refactored_animation_sources.json` 的 127 AnimSequence 身份与各资产的 raw/evaluation/curves；关联 AnimBlueprint 和 BlendSpace 引用。
- 图：`v4_layering_inputs.json`、`v4_anim_graph_inventory.json` 及 Refactored compiled node 的 sequence、explicit frame/time、loop 和 sample point。固定数字索引可直接列出；动态表达式不能冒充已完整反解的索引语义。
- 代码：Aim sampling/input model、Default Overlay、Refactored Rifle 状态图和实际资源 reader。确认 Look 三行的俯仰与时间 yaw 参数、Prop Aim 的时间 pitch 参数、固定 Pose frame 及 additive baseFrame。两套命名空间的当前 reader 不能仅靠一个 FBX 根目录统一切换。
- 原 WalkPose_F 与 Refactored Walk_Forward_Pose 的 35-key 旋转轨道变化为零；因此 key count 不能证明这类资源是移动循环。
- Kimodo：本机 `G:/Github/kimodo` 的 constraints.py、generate.py 与官方 JSON/约束/limitations 文档。确认 Y-up 米制、root2d XZ、heading 向量、fullbody 的 joint-position 条件、稀疏目标建议及公开 API 没有已实现的 contact conditioning。提示词草稿不是质量改善的实验结论。

## 覆盖与结构校验

执行：`python tools/unreal/build_als_kimodo_asset_contracts.py`，并用 Python AST、JSON 和哈希核对做独立结构检查。结果写入 `artifacts/kimodo_als/generation_contract_analysis_20261007.json`。

| 检查 | 本批结果 |
| --- | ---: |
| 唯一动画 source 身份 | 253（V4 126 / Refactored 127） |
| 功能分类 | 27；未分类条目 0 |
| Markdown 逐项约束段落 | 253 |
| 非 Sequence 目录身份 | 200；不等于独立物理文件数 |
| 规格输入文件 SHA-256 核对 | 245 |
| 清单内 additive base 缺失 | 0 |
| additive baseFrame 越界 | 0 |
| 连续两次生成输出字节一致 | 是 |

所有条目有作用、条件目标和验收要求。NoCurves、T pose、Bow 道具 rig、Land Additive、Rifle Arms 明确为确定性或母动作派生；这些条目没有独立 Kimodo prompt。其他提示词是待受控比较的候选。132 个 motion-candidate 方法条目不等于 132 次独立调用；跨命名空间同义动作仍可共用 master。

输出 SHA-256：

- JSON：`b5352a6b6b5f6d0e6ba36648adff908ebdfd84e044d217175b62950204de0622`。
- Markdown 清单：`ad08a9b8406af88e322c16349550f07bc65c85b30dd5a0841271ab6b3df3a03f`。

生成器只读当前动画/图文件，写上述设计清单。复跑前后核对输入 evidence hash，无生产动画重写。规格为 `design_only_not_executable_constraints`：pose、grip、ledge 目标需要批准的新 master 和实际 socket 数值，不能直接把该 JSON 传给 Kimodo。

## 验收边界

这是源数据/代码分析与规格结构验证。没有在本批执行 Kimodo 候选比较、UE 重定向、FBX/raw 新资源导出、新套件 Godot 全链、人工视觉或性能验收。此前 idle 诊断的 Godot 结果只用于解释设计动机，不能给本规格或未来资源签发运行验收。

后续应先建立统一骨架/Stand/Crouch/master/grip 包，完成 Default idle + Pose + Aim 最小组合，然后依次扩展步态、过渡、空中、持物和恢复动作。既有原生算法对照阈值保持；新表演另设并冻结接触、循环、参数中性、相位与最终蒙皮质量门。
