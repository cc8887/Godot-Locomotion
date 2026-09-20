# 蹲姿正式来源与共享播放链补完

日期：2026-09-11；完整性修复计划第四十五批。
工作区：`D:\GodotALS-p5a-events-actions`，分支 `feature/p5a-events-actions`。
保留用户已有修改，未执行 commit、revert 或合并。

## 已完成与未完成

蹲姿来源已从“仅有原生导出”进入正式编译表、生产 P5 来源快照、物理布局和 Godot
资产库。来源的共享 Sync、动态速率及通知链已有实际资源验证。
蹲姿三台状态机的规则、缓存依赖、完整姿势混合，以及 Main 的生产更新/提交尚未接通。
旧近似蹲姿路径仍不能视作原版等价，本批不关闭滑步、上身、交错步或人工视觉验收。

## 正式数据与身份

`assets/config/v4_locomotion_source_graph.json` 采用第四十四批已两次冷导出验证的
`v4_grounded_dependencies.json`，新 scope 为
`Main Grounded with Standing and Crouching source dependencies`。
两文件 SHA256 均为
`B0B5DFB8BFDE3059FA25BF9E8685AAC14CF71FD9F0C795C67A46A224AE589EF2`。

对比上批保留的 `artifacts/grounded-dependencies-legacy-main.json`：
原有 92 张图、35 个同步资产、6 台编译状态机内容逐项一致。
新正式表总计 124 图、45 个同步资产、9 台状态机。

| 来源 | Player ID | 原生编译节点 | Sample ID |
| --- | --- | --- | --- |
| Crouching Idle 显式采样 | 43 | 105 | 65 |
| Rotate Left / Right | 44 / 45 | 113 / 116 | 66 / 67 |
| Stop 两个独立显式采样 | 46 / 47 | 120 / 121 | 68 / 69 |
| Lean BlendSpace | 48 | 134 | 70..74 |
| CLF Walk F / B / L / R | 49..52 | 135..138 | 75..78 |
| CRF Walk R | 53 | 139 | 79 |
| Cycle WalkPose 显式采样 | 54 | 141 | 80 |
| CRF Walk L | 55 | 142 | 81 |

保留原 0..42 播放编号和 0..64 采样编号，新增域枚举追加在末尾。
总计 56 个播放器、82 个采样身份；P5 完整布局 118 项，包括 64 个 SourceSample
和 18 个 SourceEvaluator。原生未绑定资产节点由 190 降至 177，并不代表全图已完成。
同步资产表重编译为 45 项，54 个 marker，73 个 notify 定义/策略。
内部 SequenceIndex 根据完整资产表重建，不作为跨版本持久播放身份。

## 代码行为

- `AlsCrouchingSourceCompiler` 按两台内容机器的编译状态顺序读取来源，严格核对
  图所属状态、节点身份、动画及骨架、动态输入、自上下文、循环、速率变换、
  显式采样、Lean 样本和同步策略。六条移动序列不是四向近似；三个 WalkPose
  采样身份以及两套 Lean 来源不会按相同动画资产去重。
- `AlsLocomotionSourceCompiler` 追加新来源，复用现有 Sync/Notify 编译器及
  SourceAware P5 快照。Godot 使用该正式表创建资产库，没有另外导入一套资源。
- `AlsLocomotionSourceRuntime` 增加可缺省的 crouchingPlayRate；只有提交蹲姿移动
  来源时才要求它存在。动态连接覆盖序列化 DefaultPlayRate，再除 PlayRateBasis，
  不借用 StandingPlayRate。无效值/错误域/错误来源种类等在写输出前原子拒绝。
- Rotate 输入支持 Standing 与 Crouching 各自的来源，仍按左右输入控制循环。
  `AlsStandingStateGraph` 与其回归检查增加 Standing 域筛选，修复新增蹲姿后
  全局 Single(RotateLeft/Right) 匹配到两个来源的问题。
- `AlsStandingCycleGraph` 将共享候选状态和栈上缓冲扩展到 56/82，并集中固定容量
  常量。这不是新时钟，也没有提前把蹲姿当成活动来源更新。
- `AlsLocalPoseClip.SampleSourceSeconds` 检查源 float 时长必须等于 Godot double
  时长的 float 表示，时间必须在源范围内；仅在有效浮点末尾比 double 末尾略大时
  钳到资源末尾。原严格 double Sample 方法不变。

新 Godot 资源 smoke 对 17 个样本检查起点/终点及超出源末尾一个 float ULP 的拒绝。
定位到的实际例子是 Lean：源末尾 0.033333335，Godot 长度 0.03333333333333333。
未改来源时间、播放速率、动画数据或放宽核心时间守卫。

## 验证

| 项目 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema | 1863/1863 |
| Import 全套 Debug，最终专用工作线程分配测量 | 906/906 |
| 新蹲姿/Rotate 首轮专项 | 35/35，随后增加默认值覆盖及错误轴两个测试 |
| Core 来源、共享同步、初始化和事件相关 Release | 83/83 |
| Import 蹲姿、混合批次、来源视图和 P5 快照相关 Release | 82/82 |
| Godot 编译 | 0 警告、0 错误 |
| 新蹲姿真实资源，30/60/120 Hz | 420 帧，7140 次姿势采样，30 个通知，六条移动来源发生姿势变化 |
| Main 真实资产组件 | 1050 来源帧、420 受控混合帧、中断栈 104 帧，热路径 0 B |
| Standing / Pivot 实际 Controller | 各 5040 帧及相同重试 |
| Detail / Sprint 实际 Controller | 1890 / 1260 帧及回滚 |
| Cycle Worker 单/多线程 | 各 180 帧，10 个事件，摘要一致 |
| 来源事件/整帧晚期回滚 | 两种模式均通过，无通知泄漏 |
| 旧 P4 全验证脚本 | 通过，活动姿势/曲线零分配 |

Import 混合批次测试现在同时包含 Standing/Detail/Crouching 来源，使用同一 Locomotion
同步组，覆盖来源重入、退休、时间与样本身份回填和相同重试。
Rotate 来源的 P5 测试分别覆盖站姿和蹲姿，不把左右播放与通知合成一个身份。

新 Godot smoke 使用实际生产 SourceAware 绑定与资源库，但其活跃来源/权重仍为
测试输入；Lean 五个均权样本只是来源与资源覆盖，不是原版方向/Lean 姿势混合。
没有新增完整 UE AnimBP 逐帧轨迹或人工移动截图，不宣称 Demo 视觉改善。

现有 Worker 摘要保持：result=A9DF0647AFC3574C，full_pose=04D4A5651B87E0E4，
pose=2DED5435A66BCAEC，root=309E8D0E0BEEB2CB。
没有修改相机、输入、UE nativeActual、动作质量阈值或既有 P4 oracle。

## 失败与限制

首错日志/TRX 保留于 `artifacts/crouching-sources-*` 和
`artifacts/test-results/crouching-sources/`：

- import-first：旧计数和单 Lean 假设失败，68 通过、3 失败；按新增真实来源更新。
- import-expanded：旧 Rotate 用两个输出槽接四个来源，869 通过、1 失败；
  改为按域分别验证各自两个来源，而非删除蹲姿覆盖。
- 新测试首次构建命中 xUnit2031，改用 Assert.Single 的谓词重载后运行。
- Godot 首次 Standing 失败为全局 Rotate 查找歧义，已修复生产域所有权条件。
- 新资源 smoke 首先错把 Lean 五个样本当作五条通知；最高权重策略实际只产生
  一条 Lean 通知 tick，已按返回数量传递。随后暴露 float/double 末尾差异并补
  受检适配。最后修正测试按数组下标回填时间的错误，改按 PlayerId 回填共享 Sync
  按组排序的历史；这些失败没有通过修改 Sync 或 Notify 策略来绕过。
- Import 两次全套在新速率测试的测试线程测得 4200 / 3872 B，单独四种速率复测
  均通过。将这项分配测量放入专用工作线程，仍热身 100 次、测量 2000 次并断言
  精确 0 B，最终全套 906/906，Release 同样通过。
  这是测量环境隔离，不是已查明/修复原并发测试线程分配差异的证据；旧失败保留。

本批未修改 UE 插件，也未新增 UE 构建/冷导出；所用数据的完整原生构建、审计、
独立打包和两次导出证据见第四十四批报告。此前 UE 两条 AutomationTest 错误及
资产警告仍未解决。本轮启动的测试/构建进程均已退出。

## 后续

1. 实现蹲姿 Locomotion States / Cycles / Directional States 的原生规则、
   曲线、Stop/Rotate/方向分支及缓存调用上下文，复用本批正式来源，不另造播放器。
2. 将 Main 的真实权重、站蹲过渡、缓存重入/初始化与 P5 动作反馈接到 Controller，
   再接最终曲线、动态上身分层和完整脚部约束。
3. 按原 P5A/P5B/P5C/P6/P7 范围继续通用事件/ActionPlayer、Overlay/道具、
   Mantle/Roll/Root Motion、Ragdoll/Get-up/Pose Recovery、完整相机和十分钟性能预算。
   音频仍暂缓；完整目标保持，不以来源闭环替代实际 Demo 质量验收。
