# Aim 原生图定义与独立转换曲线

日期：2026-09-12，第一百零八批。工作区 `../GodotALS-p5a-events-actions`。
承接完整性修复路线的 P4 Aim/上身来源依赖；资产与规则基线为真实 ALSV4
AnimBP。未提交、回退或合并既有改动。

## 完成与边界

新增只读 UE 读取接口、正式导出配置、Core 图定义与 Import 严格编译器。
`AlsMovementGraphDefinition.AimPose` 在正式 Worker/Demo 加载阶段编译它。
三个状态机共九状态、十九条转换、七个独立 evaluator、四条曲线，以及
79 个逻辑骨的 Head profile 已保留。共享转换栈增加按活动转换索引选择
曲线的入口，支持被打断但尚未退休的转换继续使用自己的曲线。

当前只完成定义加载与所需基础能力。尚未实现按角色持有的完整 Aim 嵌套
状态更新、七个来源的实际姿势采样和生产最终层连接。导出包含 ALS_N_Look
原生 BlendSpace 文本，但采样网格及原始 Aim 动画来源尚未编译闭合。
没有新截图或上身视觉通过结论。

第 110 批核实更正：三个 Aim Sweep 及 ALS_N_Pose 已存在于移动原始源库，
重新导出的原始文件字节一致；当时缺少的是 Aim 专用绑定、网格与消费链，
不是原始动画资产。后续进展见 `2026-09-12-aim-source-sampling-runtime.md`。

## 原版执行细节

- 使用编译后的状态顺序与各状态出口优先级；不按编辑器节点编号猜顺序。
  三个状态机均从状态 0 开始、每帧最多三次转换、保留首次更新及重入策略。
- 125°/130°之间存在转换门控区间；部分规则读取上一帧特定状态权重，
  比较严格的 `!= 1`，回看超时规则为当前状态停留时间严格 `> 2`。
  这是 Aim 行为，不等同于下身换髋规则，也不能替代下身时序验收。
- 转换持续时间、混合模式、曲线引用是三个独立字段。即便有曲线引用，
  HermiteCubic 模式也不能误用 Custom 曲线。每条重叠转换独立保存曲线，
  权重先到 1 不代表持续时间已结束；清理仍遵守 UE 转换退休顺序。
- 头颈使用 WeightFactor profile：neck_01/head 为 2，其余骨保持默认。
  转换 alpha 为 0.5 时，头颈 incoming/outgoing 为 0.8/0.2，身体为 0.5/0.5。
  由 UE 原生逐骨权重函数的 33 组样本验证，不将它简化成身体统一权重。
- 两个 SequenceEvaluator 使用显式秒数，五个 BlendSpaceEvaluator 使用
  归一化时间；相同资产在不同状态中的编译身份保持独立。BlendSpace 的
  动态位置来自平滑瞄准的 pitch，不能误接 yaw。中性输入分别为 0.5 秒
  或 0.5 归一化时间。
- 历史名为 InRange_FloatFloat 的 UE 节点实际使用 double 引脚；范围比较
  保持 double。evaluator 输入遵守实际 float 转换，不混淆两者。

## 数据与实现

`tools/unreal/export_aim_pose_inputs.py` 调用新增
`UAlsAnimationGraphLibrary.ReadBakedStateMachines`，读取编译状态机、编辑器
策略、FRichCurve、原生曲线采样与 BlendProfile 结果。接口不保存资产、不
修改 CDO。Python 先前无法读取受保护 BakedStateMachines 属性，原失败日志
保留；新增接口直接读取 IAnimClassInterface，解决反射访问限制。

`AlsAimPoseCompiler` 同时核对既有 layering 导出与本次原生图连接，检查
资产、播放身份、父子所有权、出口规则、共享规则委托、优先级与全部相关
策略。T3D 父节点嵌套子图，解析器增加直接层级属性/引脚模式，避免把子图
内容误认为父节点输入；原有调用默认保持原模式。

四条曲线各保留 201 个原生 GetFloatValue 样本。UE 快速浮点优化的采样域
为 `i * (1f / 200)`，管理端按记录时间对照，曲线容差 2e-6。Head profile
原生权重容差也为 2e-6。冷/正常 Editor 对照另对完整原生数值结构做精确
比较，不以文本字节一致替代语义校验。

正式配置 `assets/config/v4_aim_pose_inputs.json` SHA256：
`3BF4F401C56EB5BF58A6BF12BCBE05CD601B2CC28B06EC17C8E7371C7A668D60`。
普通 Editor `artifacts/aim-pose-editor-repeat.json` SHA256：
`59AA6FA952A7D40C5DBF5038AB3C4C17A4DD11088F0E632854D6F221837252AC`。
两份各 25 个源图，非消费节点文本存在差异；不能称为文件字节一致。

## 验证

日志均在 `artifacts/`，以下命令进程实际退出 0：

- `aim-pose-import-verified.log`：121 项相关 Import 测试通过，其中新增
  19 项覆盖真实拓扑、采样输入、规则边界、独立曲线与 15 类源数据变更拒绝。
- `aim-pose-core-final.log`：54 项转换栈、Grounded、瞄准输入和逐骨混合回归。
- `aim-pose-build-verified.log`：Godot C# 构建 0 警告、0 错误。
- `aim-pose-frame-verified.log`：普通 Editor 定义重复编译通过，三份原生
  数据结构 bakedMachines/curves/blendProfiles 精确一致。基础帧 3,360 帧、
  隐藏 302 帧、晚期故障 12 次、保护拒绝 24 次，重试一致。
- `aim-pose-production-single.log`、`aim-pose-production-parallel.log`：
  实际 Worker 各 600 帧，事件 28，lag/stale 均 0。结果
  `EAAF62E4D0A80A76`、完整姿势 `3103E3B355BF1F3B`、根运动
  `A4F6C26CBAB8A0E7`，与上一批一致。这个一致性验证既有生产行为未回归，
  不代表尚未接入的 Aim 姿势已验证。

## UE 构建与加载

按 UE 插件构建诊断技能执行整项目 Editor 构建、冷导出、普通 Editor、
DataValidation、隔离 BuildPlugin 与包后审计。相关日志：

- `aim-pose-editor-build-profile.log`：完整目标成功、三个项目插件一致；
  构建指纹 `4FD01511F21519ECD7010716515F7A6360AE7F6CBEFDC47C277C60CAAA703F75`。
- `aim-pose-native-final.log`、`aim-pose-editor-profile.log`：冷/正常导出
  成功，均不保存资产。普通 Editor 保留既有两条 AutomationTest
  `Condition failed`，因此不称为全项目日志无错误。
- `aim-pose-data-validation-final.log`：0 errors、3 warnings；警告来自旧
  PawnActionsComponent 与旧 NavMesh 资产兼容性。
- `aim-pose-plugin-package.log`：隔离 Win64 插件打包成功，目录
  `artifacts/unreal/AlsAimPosePluginValidation-20260912-108`。
  这是插件 Editor 验证包，不是完整游戏 Shipping 包。
- `aim-pose-post-package-audit.log`：AlsGodotExporter、AutoTestTools、
  BlueprintLisp 全部 PASS；仓库、项目插件、隔离包中新增接口两文件哈希一致。

构建期间引擎重建 NetCore 并更新 BuildId。先确认具体生成物，再通过标准
wrapper 将九项旧收据/DLL/PDB/manifest 移至可恢复备份：
`../AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260912T100415747Z`。
随后完整重建并审计成功，当前 BuildId 为
`7ae81a57-ce2c-4a46-86dc-e0b5a2e6a21e`。没有手改 BuildId、部署包内 DLL，
或删除源代码/资产；原始构建失败日志保留。

## 下一步与完整性验收

1. 实现真实 Aim 嵌套状态历史、相关性重入、独立时间、每条过渡曲线及逐骨
   求值；接入原生 BlendSpace 网格和 Aim 原始来源，纳入候选/提交事务。
2. 完成 Overlay/BasePoses/LayerBlending 的生产最终姿势与曲线输出，反馈
   真正最终曲线，保留 presence 与上一已提交帧语义。
3. 闭合 Foot IK/Lock/pelvis/平台，再以相同输入、帧率、起始脚相位对照
   UE/Godot，多帧截图与人工验收上身、换髋和支撑脚滑移。
4. 继续 P5A 通用动作、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
   P6 物理恢复/完整 Camera、P7 十分钟性能预算。音频仍暂缓。
