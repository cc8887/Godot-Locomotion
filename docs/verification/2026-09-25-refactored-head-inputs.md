# 实际 Head 设置与 Look 姿态源

从原 `AB_Als_C` 默认对象只读解析 Settings，导出 `AIS_Als_Default` 的五个 Head 半衰期：pitch .1、yaw .1、switch sides .2、first-person pitch/yaw .01 秒。新增 `AlsRefactoredHeadSettingsCompiler` 校验父类、设置资产、图文件哈希、字段闭包和数值范围，输出上批 Core 模型的参数类型。

`BS_Als_Look` 实际三个采样点依次为 Forward(0)、Down(-90)、Up(90)。三条动画均为 1 秒/31 键、RotationOffsetMeshSpace 加法、AnimFrame 参考；共同引用 `A_Als_Stand_Pose` 第 0 帧（该源 2 键、1/30 秒）。输入导出包括 BlendSpace 原文、四个源原始键/策略/曲线及完整 Skeleton 元数据。没有更改或保存 UE 资产。

现有原生 `ReadRawBlendSpacePose` 对 pitch -120/-90/-45/0/45/90/120 × normalized time 0/.137/.5/.773/1 导出 35 个姿态，各 79 骨，供下一步采样器数值对照。当前只验证了该参考的来源、哈希、完整性和有限值，**尚未有 C# Look 姿态比较结果**。

## 图回调顺序核对

已有原生编译图中，Root(property0)→ApplyMeshSpaceAdditive(property2)，其 additive→OnBecomeRelevant InitializeHead(property3)→OnUpdate RefreshHead(property4)→BlendSpaceEvaluator(property5)。UE `AnimNode_CallFunction.cpp` 在递归 Source.Update 前调用相应回调。因此重新相关时必须先初始化标记、再 Refresh、再取 HeadState.PitchAngle/YawAmount；HeadBlendAmount 控制是否访问该加法分支。这里只核对原图与源码，尚未实现该完整宿主回调流程或新增运行 oracle。

## 验证与重复导出差异

- Import Head/Layer/BasePose 定向 41 通过、0 失败；补原生参考来源校验后 Head 7 项复跑通过（其中6项重叠）。
- Optimize 构建 0 warning、0 error。没有新的 Core 全量、Godot 运行或视觉验收。
- 使用 UE 构建技能，完整 Editor target 与插件审计通过，0 actions；fingerprint `C1E13C8BE16C15A0E4427F4D37F21FB14262557178C0FCAC14AC46DF96161D8C`，日志前缀 `20260924T180611631Z-b7ab4ecbf29945f9964c895a5fdba8c3`。未改插件/配置，无新 DataValidation/打包。
- 冷导进程退出 0，0 Error/Warning。普通 Editor PID39164 等待真实进程退出0；保留两条旧 Condition failed 与五条既有 AI/导航/材质/console/Crowd 警告。
- 两次输入 **非字节一致**：递归字段比较仅三条加法源 `evaluation.rootLockFirstFrame.scale` 的九个分量不同（冷1/编辑器0）。该字段来自已有 `ExtractRootTrackTransform` 辅助调用；所有原始键、其他策略、设置及 BlendSpace 原文一致。三源均 enableRootMotion=false、forceRootLock=false、rootMotionRootLock=RefPose。本批未修改底层导出 helper，也未确定异值的引擎内部根因。
- 两次参考仅 `inputsSha256` 因输入不同而变化，35 组姿态、sample权重/时间、曲线及骨名逐字段一致。不将这一结果表述为输入全量确定性通过。
- 冷输入 SHA `357BF870E34419F0EAB4F2317FCB780ED7B06E1116D4AF788E89844E5CAD1589`；冷参考 SHA `9D5E10CC9148876D0F28B0654AA075F42C63D31CA980E7128407161BAA08EE6D`。
- 编辑器输入 SHA `A35F3A7435A62BA6AD0E084AAAAB7019473DB1F822E8AD475B7753F814AB0A00`；编辑器参考 SHA `2FA06DDA615F20092A4ADA1073E7E6F7D5586B82CFB3A76D1C07D80467465A48`。两份保留于 `artifacts/refactored-head-inputs/`，正式资源保留冷导出原文。

下一步实现 Look 的原始键加法参考/网格空间混合采样并与这35姿态对照，补 View/Spine/Head 连续状态 oracle 及实际图回调。后续采样器必须遵循根锁政策，不可把上述加载状态相关辅助值当加法基准。普通 Demo 尚未接新 Head；其余宿主/Mantle/物理/Flail/最终预算缺口以及用户暂缓项保持。
