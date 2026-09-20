# Lean 原生采样与真实加法组件

日期：2026-09-11；完整性补完第四十九批。

## 完成范围

补齐原计划 Cycles 所需的 Lean 原生网格、轴策略、加法基准以及实际采样执行。
本轮优先解决了上一批缺失的原生数据，脚根缩放仅完成源码核查，未实现。
完整 Cycles 缓存、主蹲姿和 Main 尚未接入 Demo，不宣称滑步、换髋或上身已修复。
未 commit/revert/merge，原有修改保留；未改 UE 资产或已确认的键鼠控制。

## 原生数据与实现

`AlsBlendSpaceTrace` 新增 `-Lean` 只读选项，旧默认 WalkRun 路径保持。
正式数据 `assets/config/v4_lean_sampling.json` 来自冷启动 UE 的
`UBlendSpace::FilterInput` 与 `UpdateBlendSamples`，包含：

- LR/FB 范围 -1..1，分区 4/4、不循环，25 个三顶点网格记录。
- 两轴模式 SpringDamper（枚举 5），但 InterpolationTime=0，因此无轴平滑。
- TargetWeightInterpolationSpeed=0，无逐骨骼覆盖，不进行样本权重平滑。
- 五个独立样本 0/F/B/L/R，均为局部加法、AnimationFrame 基准类型 3、基准帧 0。
- 共同基准 `ALS_N_Run_BasePose`，不是 LeanPose_0。保留样本与资产速率、时长和身份核对。
- 441 个静态输入（包括越界输入、边界和同权重）及 30/60/120 Hz 共 840 帧连续输入。

运行时 `AlsLeanBlendSpace` 按原生网格的展平索引与顶点顺序累积，保留
GetEditorElement 在边界只检查展平索引的行为；网格坐标使用 double，中间角点
权重按原生转 float。去重、非稳定同权重排序、低权重删除、归一化顺序均对照
原生轨迹。初始化时复制并校验网格，热路径无分配、不持有额外滤波或播放时钟。

`AlsLeanSamplingCompiler` 核对源表、骨架、五个样本和共同基准，拒绝未支持的
轴滤波、循环、网格变化、逐骨骼平滑、Mesh Space 和错误加法类型。Standing
Cycle 和 Crouching 可各自编译，player 身份独立；本批没有把 Standing 旧生产
Lean 路径替换为该组件。

`AlsLeanPoseSampler` 验证实际 Godot 骨架与时间域，借用五个 sample 的共享来源秒数，
先对实际绝对动画求局部加法差值，再按原生样本顺序混合并应用到基础姿势。
曲线亦减去共同基准再叠加。只借用 Animation，不在 Dispose 时释放共享资源。

## 真实资源与证据边界

`CrouchingSourceSmoke` 中 Lean 的五个样本权重已从等权夹具改为实际原生网格求值，
经共享 Sync 推进来源时间，再供姿势/曲线采样。其他来源及上游方向、Stride 输入
仍为受控夹具，仍未按完整缓存相关性停止/恢复更新。

420 帧均产生 Lean 姿势变化；263 帧单样本结果与原动画减 Run_BasePose、再应用到
输入姿势的直接计算一致，重试一致，所有五样本均参与。中间帧使用真实动画混合，
但没有 UE 完整最终骨骼轨迹对照。这里的基础姿势来自已有 Stride 组件，仍跳过尚未
实现的 DiagonalScale，不能将它当成完整 Cycles 或可玩 Demo 视觉验收。

## 验证

证据目录：`artifacts/test-results/crouching-cycle-native/`。

| 检查 | 结果 |
| --- | --- |
| 新增 Lean 专项 | 23/23 |
| 原生静态/连续权重 | 441 + 840，权重误差不超过 1e-6，样本顺序一致 |
| Core 常规，排除已有 P5A golden/schema 两类 | 1882/1882 |
| Import 全套 | 1017/1017 |
| Lean/蹲姿相关 Release | 146/146 |
| Godot 构建 | 0 warning / 0 error |
| 真实蹲姿来源/Lean | 420 帧、7140 次来源采样、30 通知、263 单样本直接检查 |
| Standing/Pivot/Detail/Sprint | 5040 / 5040 / 1890 / 1260 帧与重试通过 |
| Main 资源组件 | 来源 1050、受控混合 420、中断 104，活动求值零分配 |
| 单/多线程 Worker | 各 180 帧、10 事件，摘要一致 |
| 双模式 late_source_event | 失败候选回滚、无回调泄漏 |

生产摘要未变：result `A9DF0647AFC3574C`，full_pose `04D4A5651B87E0E4`，
pose `2DED5435A66BCAEC`，root `309E8D0E0BEEB2CB`。
未改旧 oracle/阈值，未运行本批全套 P4 脚本、移动截图或十分钟性能验收。

## UE 构建和加载

按 `ue-diagnosing-plugin-build-load` 技能，先整个项目 Editor 目标构建及插件审计，
再冷导出、独立包和验证。技能提到的额外 superpowers 调试/完成技能当前未提供，
本批使用源码、构建日志与实测逐项核验。

首次工具启动失败保留：系统 .NET 没有 10.0，UBT 退出 -2147450730，日志
`Saved/Logs/PluginBuild/20260910T232458655Z-71c3d0030dcd41c19080be6977dbc100-ubt.log`。
使用引擎自带 `Engine/Binaries/ThirdParty/DotNet/10.0/win-x64` 作为本次构建的
DOTNET_ROOT 后通过，没有修改系统 .NET 安装。

- 完整构建与插件审计退出零；构建日志 `Saved/Logs/PluginBuild/20260910T232547099Z-75fa6f3565bb46398a8d111821c05bd5-ubt.log`。
- 构建指纹 `2E09A69C46A84AAC54F68ABD009197FF98ACD90B39941699105014F304DC4B1C`。
- 独立 BuildPlugin 包 `artifacts/unreal/AlsLeanSamplingPluginValidation-20260911` 成功，随后项目插件再次审计通过。
- 两次 Lean 冷导出均退出零、0 warning/error，正式 JSON 同 SHA256：`82A3DE78BA6B29B63917556C20C99C497073F5EAABAB1C1DAC2756DFE41D0241`。
- 旧默认 WalkRun 冷导出与原 fixture 同 SHA256：`816EECB220D05798E8359988A01C3C2E78A5FBEA3F721FCDF34822C5E8A5396B`。
- 仓库与项目部署的 Commandlet 源码同 SHA256：`9FFC1FAD17FB52F5AF7FA9014511518FE147B24A84C9723202C6A9D8C9EDB82D`。
- DataValidation 688 资产，退出零，保留既有 ActionsComp/Navmesh 等三条资源警告。
- 普通 Editor PID 34380 初始化完成、CloseMainWindow=True、进程退出且日志正常关闭；读取到的 ExitCode 为 null，因此不写数值退出零。仍有两条既有 AutomationTest 错误，不称为无错误冷启动。

本批构建、导出、测试与普通 Editor 进程均已退出。

## 下一步

1. 实现并验证 ik_foot_root 的组件空间缩放及部分 alpha 的局部回混；不能只用局部比例乘法冒充完整骨骼控制器。
2. 用实际相关性权重组合方向/Stride/DiagonalScale/Lean，接 CLF 主姿势与 Save/UseCachedPose 生命周期。
3. 完成 Main 生产上游、最终曲线和动态分层，然后继续原 P5A 至 P7；不重复从 Lean 网格导出开始。
