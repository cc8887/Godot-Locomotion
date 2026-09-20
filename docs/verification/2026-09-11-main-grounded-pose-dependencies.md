# Main Grounded 姿势与原生依赖补完

日期：2026-09-11；完整性计划第四十四批。
工作区：`D:\GodotALS-p5a-events-actions`；保留现有未提交修改，未执行 commit/revert。

## 结论与范围

已完成 Main 内容链组件、QuickFeet 逐骨骼权重和 ChangeStance 曲线依赖，
并从当前 ALS V4 资产补出蹲姿原生子图。它们属于基础移动完整性修复，不是新增玩法。
本批没有接入实际 Demo：Standing/Crouching 缓存仍是测试夹具，上游权重、蹲姿
正式来源及 Main 的统一更新/事件提交仍待完成。因此不宣称滑步、交错步或上身已修复。
不改相机、键鼠方向、既有生产姿势路径、UE 原始期望或数值/分配阈值。

## 实现

- 原生导出新增 `-IncludeGrounded`，包含 Main、Standing、Cycle、Stop、Detail，
  以及 `(CLF) Locomotion States`、`(CLF) Locomotion Cycles`、`CLF_Directional States`。
  导出 124 图、45 个 syncAssets、9 台编译状态机，蹲姿 8 个 SequencePlayer、
  4 个 SequenceEvaluator、1 个 BlendSpace。输出只读，不保存 UE 资产。
- 新数据 `assets/config/v4_grounded_dependencies.json` 单独保存；旧正式来源数据
  与 `-IncludeMain` 输出完全一致。正式播放表仍为 43 节点/65 样本，新增蹲姿
  13 节点尚未绑定，不把“已导出”记作“已运行”。
- `AlsGroundedPoseDependencyCompiler` 检查来源、骨架拓扑、profile 模式及条目归属，
  将 79 个逻辑骨中的 15 个 QuickFeet 条目映射到 68 个物理骨。逐项因子来自原生，
  不在运行时硬编码腿部名单。拒绝虚拟/重复映射、非有限数、未支持的曲线策略。
- `AlsGroundedPoseBlend` 实现 WeightFactor 的独立正反权重、端点最小值钳制和归一化。
  33 个原生 FAlphaBlend 采样案例乘 15 条目，共 495 组对照，误差不超过 1e-6。
  这是原生函数级探针，不是完整 AnimBP 播放轨迹。
- ChangeStance 使用实际关键帧和插值：0、0.40000000596、1；中间切线
  2.730307579。复用现有曲线求值器，保留非线性曲线，不以线性近似替代。
- `AlsMainGroundedPoseCompiler` 严格读取 8 个状态的内容链、缓存身份、源播放身份
  和真实引脚曲线值；拒绝额外节点、断链、动态覆盖和未支持的生命周期函数。
  Standing 写 BasePose_N，Crouching 写 BasePose_CLF/Weight_Crouching，
  From Roll 写 FootLock_L/R。两个 conduit 不产生姿势。
- `AlsMainGroundedPoseGraph` 借用既有 playerId 来源时间及两个缓存姿势，按活动过渡
  栈逐层混合，QuickFeet 仅影响骨骼而非标量曲线，最终一次归一化旋转。
  三个源采样器不拥有共享 Godot Animation 资源，不新增时钟或事件队列。
  该类当前仅由 `MainGroundedPoseSmoke` 调用，尚未进入 Controller。

## From Roll 自动退出的证据边界

本机引擎是 UE 5.9，当前资产 From Roll evaluator 的显式时间与动画长度均为 1.5 秒。
`AnimNode_SequenceEvaluator.h` 的 GetAccumulatedTime 返回显式时间，
`AnimNode_StateMachine.cpp` 根据剩余时间调整自动过渡时长。
因此当前末尾姿势的自动退出将标注 0.8 秒的 QuickFeet 过渡缩短为零。
Core 测试按上述源码规则验证零活动过渡，不为覆盖 profile 而修改真实输入。

另一个独立受控案例使用 0.7 秒时间观测来覆盖持续活动的 QuickFeet 及中断混合栈。
这个观测并非实际 From Roll evaluator 的时间，日志明确标为 `profile_probe_blends`。
目前没有新增 UE 完整 Main 状态机逐帧执行探针，不能据此断言其他 UE/ALS 版本
同样退出，也不能将这个规则当作已证实的起步滑步原因。

## 验证

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema，最终复跑 | 1863/1863 |
| Import 全套 Debug | 870/870 |
| Main 内容和依赖 Debug 专项 | 19/19 |
| Core Grounded 相关 Release | 30/30 |
| Import Main/Grounded 相关 Release | 53/53 |
| Godot 项目构建 | 0 警告、0 错误 |
| Main 真实资产组件，30/60/120 Hz | 来源 1050 帧，受控混合 420 帧，其中中断栈 104 帧 |
| Main 活动姿势/曲线热路径 | 热身 200 次、测量 2000 次，0 B |
| Standing/Stop/Rotate、Pivot 实际 Controller | 各 5040 帧及相同重试 |
| Detail / Sprint 实际 Controller | 1890 / 1260 帧及回滚 |
| Cycle Worker 单/多线程 | 各 180 帧、10 个事件、首次事件第 25 帧，摘要一致 |
| late_source_event / late_transaction | 两种模式均通过，无通知泄漏 |
| 旧 P4 全验证脚本 | 通过，活动姿势和曲线零分配 |

Worker 摘要保持：result=A9DF0647AFC3574C，full_pose=04D4A5651B87E0E4，
pose=2DED5435A66BCAEC，root=309E8D0E0BEEB2CB。
没有新增视觉截图或人工验收。保持旧摘要仅说明旧生产路径回归未变，不说明新组件已接线。

失败记录不覆盖：

- 首次 Grounded 导出退出 9：使用 `(CLF)` 前缀漏掉无括号的 `CLF_Directional States`。
  改为明确的三台状态机身份，再做完整 Editor 构建和冷导出，未绕过数量检查。
- 首次 Main smoke 未覆盖中断栈：夹具缺少 From Roll 自动退出时间观测。
  补实际末尾退出断言，并把活动 profile 的受控观测单独命名，未改状态机规则。
- Core 首轮 1847 通过、1 失败：既有
  `CandidateCopyLifecycleAndRepeatedReadsAllocateNothing` 测得 2320 B。
  单独复跑通过；新增 15 项 Core 测试后的全套复跑 1863/1863。
  原因未定位，不将复跑通过称为间歇性风险已修复，原零分配断言保持。

测试证据目录：`artifacts/test-results/grounded-dependencies/`；
Godot/UE 日志前缀：`artifacts/grounded-dependencies-`。

## UE 构建和加载

按 `ue-diagnosing-plugin-build-load` 技能，导出前完成整个项目 Editor target 构建
和项目插件审计，没有拷贝 DLL 或修改 BuildId。该技能列出的额外调试/验收技能
当前未提供，采用本地源码、日志和测试逐项核验。

- 最终 Editor 构建/全插件审计退出零，日志
  `Saved/Logs/PluginBuild/20260910T211658254Z-efe2381046844f47977590571fbd303a-ubt.log`。
  构建指纹 `4A7AC6936761EEC48D43ADA59B5667AA15A769AA59C60797FB5C0CA3AC15ED42`。
- 最终独立 BuildPlugin 成功，输出
  `artifacts/unreal/AlsGroundedDependenciesPluginValidation-20260911-fixed`。
  初次包在选择器修正之前生成，保留但不作为最终源码的构建证据。
- 打包后审计通过 AlsGodotExporter、AutoTestTools、BlueprintLisp。
- 两次 Grounded 冷导出与正式 JSON SHA256 相同：
  `B0B5DFB8BFDE3059FA25BF9E8685AAC14CF71FD9F0C795C67A46A224AE589EF2`。
- 旧 IncludeMain 冷导出与原正式文件 SHA256 相同：
  `D839633353E989817F118C3BF9342A47431F02C1013198F3E44F55C7EA14FF98`。
- 仓库和项目部署的 Commandlet 源码 SHA256 相同：
  `4EDAAE459C38AD4BF648CE879ACD87F48E6486752F09B62EBFDBE2776BE42F30`。
- DataValidation 检查 688 资产，退出零，保留既有 ActionsComp/Navmesh 等三条资源警告。
- 普通 Editor PID 17196 初始化成功，CloseMainWindow=True，取得数值退出码 0。
  仍有两条既有 `LogAutomationTest: Error: Condition failed`，不能称为无错误冷启动。
  该进程及本轮测试、构建、导出进程均已退出。

## 下一步与原计划归属

1. 先实现蹲姿 13 个正式来源、三台机器规则、缓存依赖和曲线消费，复用共享 Sync/P5。
   与 Main 内容组件连接时同步覆盖真实上游权重、相关性/重入、动作反馈及失败回滚。
2. 补最终曲线消费和动态 Layering/Add/LS、Lean、YawOffset/RotationScale、
   完整 Foot IK/Foot Lock/pelvis correction。它们是 P3/P4 完整性欠账，优先于新玩法。
3. 补 P5A 通用事件/状态、ActionPlayer/Montage/Slot 的生产接线与统一提交。
4. 按原 P5B/P5C/P6/P7 推进 Overlay/道具、Mantle/Roll/Root Motion、
   Ragdoll/Get-up/Pose Recovery/完整相机、十分钟 Release 性能预算。音频仍暂缓。

每项分别记录源数据、运行时消费、Demo 实际路径和对照验证；只有同输入下的状态、
动画时间、权重、曲线和最终骨骼结果对齐，并通过连续截图及人工验收，才关闭视觉问题。
