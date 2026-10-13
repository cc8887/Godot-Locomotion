# Mantle 修复合入 main

用户要求将已修复并启用的攀爬资产及相关实现合入主干。本次分别在 Godot-Locomotion 与独立 Kimodo-Pipeline 的 main 上形成范围明确的提交；未推送远端。

## 提交范围

制作端包含模块化风格文案、稀疏接触条件、源／目标失败反馈、目标预检、相邻帧蒙皮修复和可续跑完整入口。消费端包含接触预备／释放、保留体型与肘弯的显示适配、原事务回滚、实际倍率时钟、ViewBlock 语义、twist 及其必要模型基础。共享文件只选 Mantle 相关片段，其他任务的跑跳、蹲停、Viewer 和工具迁移内容继续留在原工作区。

当前默认指针已由后续移动任务合入为 `res://assets/generated/als_nonprop_upright_joint_active_20261013_v10/resource-pack.json`。本次核对六项 Mantle 的全部 runtime raw tracks 与先前启用的攀爬修复合成包一致，2223 项载荷字节摘要均通过；保留这个更新的活动包。原激活过程见[活动包记录](2026-10-13-mantle-active-pack.md)，当前组合包及其剩余范围见[最新默认接入](2026-10-13-sprint-loop-integration.md)。

## 提交内容验证

证据目录：`artifacts/tests/mantle-main-merge-20261013-v1/`。两个仓库均用独立 Git index 生成精确提交树，源码和测试从该树提取到忽略的测试产物目录；编译过程未引用其他未提交源码。生产测试仅通过显式配置和数据目录连接读取本机原生参考与已有真实候选。

- Godot Optimize 编译：0 警告、0 错误。
- Release Core 相关回归：348/348；Import ViewMask：5/5；接触审计工具：8/8。
- Pipeline Mantle：汇总 92 个不同测试通过，含真实 Kimodo 条件加载、两种真实风格片段与完整相邻帧蒙皮修复。首次环境缺失报错与可选数据跳过均在补齐运行库／数据路径后逐项复验；没有降低断言。
- 活动包六项姿态逐项比较及完整载荷校验：通过，详见 `active-pack-check.json`。

首次并发执行 Core／Import 构建发生共享输出文件锁冲突，Import 串行重跑通过，两个日志均保留。生产测试首次使用轻量 Python 且没有真实候选目录，两个 Kimodo 加载测试及三个数据相关测试入口报错；加载器在 Kimodo Python 中复验通过，数据相关测试在相同提交源码和现有真实数据下重跑通过，原跳过的 R5 对照也已通过。详细日志在 `pipeline/` 与 `godot/` 子目录。

本次是提交完整性及局部回归验证，没有重新生成动作或重跑完整 Godot 场景矩阵。此前真实游戏验证及其输入边界见[通用风格交付记录](2026-10-13-mantle-style-generalization.md)和[活动包验证](2026-10-13-mantle-active-pack.md)。活动包仍允许诊断模式，完整生产／人工风格／多 seed／长期性能验收保持开放。

## 本机资源与复现边界

生成的动画包遵循原 Git 忽略规则，模型与测试所需原生导出也依赖本机资源。main 的代码和默认指针提交不代表干净克隆附带这些二进制资产。当前主目录已具有并验证这些资源。本次保留了原有 71 项已暂存迁移删除；未整仓暂存、未覆盖工作区、未重新选择旧活动包。

可复用制作方法见[独立制作端方法论](../../../Kimodo-Pipeline/docs/architecture/mantle-style-generalization.md)。
