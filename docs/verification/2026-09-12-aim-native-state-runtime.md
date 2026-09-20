# Aim 原生状态运行对照与转换完成条件修复

日期：2026-09-12。移植完整性推进第一百一十二批。

## 结果与修复

真实 ALS V4 AnimBP 的 Aim 三个嵌套状态机、七个 evaluator，现已在 UE 内
执行并逐帧对照 Godot。新增 `v4_aim_state_native.json`，不是以另一份移植
算法生成期望值。30/60/120 Hz 共 2,520 帧通过状态、来源输入、79 骨姿势
和曲线 presence 对照，全部候选帧取消后重试一致。

对照发现此前共享 `AlsTransitionStack` 的完成语义不完整：原实现仅在
Remaining 减到零时清理转换；UE `FAnimationActiveTransitionEntry::Update`
由 `FAlphaBlend::IsComplete` 决定结束，比较的是混合曲线输出与终点输出。
内部 AlphaLerp 用剩余时间递增，姿势 Alpha 则另用 Elapsed/Duration。
两套时间不能合并，也不能以姿势 Alpha == 1 或固定 epsilon 代替完成判断。

实际轨迹在 30 Hz 第 19 帧首先暴露差异：0.3 秒的 Input 转换在 UE 中已
结束，旧 Godot 仍保留一项转换。此外 AO_SwitchSidesOut 在时间尚未耗尽时
就达到终值。修复增加独立 BlendProgress/Complete，并同步修改 Aim、Grounded、
LocomotionDetail、StandingDirection 的更新/事件消费者，保留未耗尽的真实
Remaining。两个旧 Aim 测试误将“必须等满时长”当作原版行为，已纠正；
新增终点非 1 的平台曲线、常量曲线及旧转换清理检查。

## 原生探针边界

`AlsAimStateTraceLibrary.cpp` 使用真实 GeneratedClass 实例、原编译节点和
原规则。在临时 Actor 下创建 SkeletalMeshComponent，给状态初始化提供
合法 World。只重接该临时实例的最外层 Root.Result，隔离 Aim 子图；退出
恢复链接并销毁临时 Actor，不修改引擎源码或保存资产。

正常 Proxy.UpdateAnimation/EvaluateAnimation 管理初始化、缓存骨骼、遍历
计数及同步 Tick，未访问/替换私有 RootNode。导出直接记录原生状态、转换栈、
标量/上一帧记录权重、来源输入/缓存权重和 RAW retargeted 姿势/曲线。
状态机 StatesUpdated 不含单一稳定状态的直接 Update 路径；消费者按引擎
该路径补入当前状态，未重新计算转换规则。相同输入明确提供给两个实现。

该探针未模拟完整角色运动或推进整个 World；它证明 Aim 子图在给定上游
输入下的数值行为，不证明完整 AnimBP、正式最终层或视觉效果已一致。

## 已完成验证

- `aim-state-native-smoke-3.log` 与 `aim-state-native-smoke-editor-repeat.log`：
  各 2,520 帧、2,437 姿势、83 隐藏帧、27 零权重帧、20 inactive 帧，
  4,922 来源更新、531 个叠加转换机器帧，七来源全覆盖。
  最大位置误差 6.288158e-7 米、四元数分量距离 4.4379843e-7、缩放误差 0、
  状态/曲线数值误差 1.4901161e-7。所有帧事务重试一致。
- 冷启动与普通 Editor 各自生成结果，SHA256 均为
  `751FCD7C9A6D184F5FCAC7D85EDE3D7BF8C548DF6B86CF4D48B19E7020615E8F`。
  正式夹具约 32 MB，保留全部帧/骨骼精度，绑定四项正式定义及来源哈希。
- `aim-state-native-core-regression.log`：770 项移动 Core 测试通过。
  `aim-state-native-import-accepted.log`：1,863 项通过，1 项既有 LayerBlending
  普通 Editor 对照测试跳过；未将跳过记作通过。
- `aim-state-native-pose-regression.log`：630 个原生混合姿势、十角色单/并行
  各 6,000 帧重试通过，10,000 热帧分配 0。
- `aim-state-native-base-layer-regression.log`：3,360 帧统一输入协作、3,058 帧
  Aim 实际姿势求值、302 隐藏帧和 12 次晚期失败回归通过。
- `aim-state-native-godot-build-accepted.log`：构建 0 警告、0 错误。
- `aim-state-native-base-worker-single-full.log` 与 `...parallel-full.log`：
  正式完整移动 Worker 各 600 帧通过，结果 EAAF62E4D0A80A76、完整姿势
  3103E3B355BF1F3B、Root A4F6C26CBAB8A0E7，事件 28、lag/stale 0，保持
  前批基线。较早未带 full-movement-coverage 的 180 帧日志也保留。

## 构建与失败记录

遵循 ue-diagnosing-plugin-build-load：完整项目 Editor 构建、三插件审计、
冷导出、普通 Editor 重启、DataValidation、隔离插件打包及包后审计完成。
最终构建 `aim-state-native-editor-build-4.log`，BuildId 为
`c0769292-e3e4-4c02-b4c2-805f5bd40b0f`。隔离包为
`artifacts/unreal/AlsAimStatePluginValidation-20260912-112`，实际构建 Editor
DLL 1,308,160 字节；新增 cpp/修改头文件在仓库、项目插件和包中哈希一致。
DataValidation 实际退出 0，0 errors / 3 warnings；普通 Editor 实际退出 0，
保留原有 Condition failed、旧导航/ActionsComp 等日志，不宣称零告警。

首次编译访问控制错误和 unity 命名冲突、部分构建后的凭据不一致，以及
首次冷导出的无 World 崩溃日志均保留。已核实并隔离的项目生成文件可从
`../AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260912T121236993Z`
恢复；没有删除源码/资产。弃用 TickAssetPlayerInstances 会多切一次 Sync
缓冲区的问题一并通过正常根节点入口消除。

首次全 Import 回归有两项旧 Aim 语义断言失败和一项分配断言失败；纠正
前两项，并按分配验证模式关闭 tiered compilation 后全部非跳过项通过。
未更改该分配断言或放宽数值容差。

附加十角色旧 P4 matrix 的单/并行状态和姿势摘要一致，但多线程性能门槛
未通过：并发运行 p95 3.444 ms，独立复测 2.559 ms，高于 2.5 ms。
日志 `aim-state-native-production-parallel.log` 和
`aim-state-native-matrix-parallel-isolated.log` 均保留失败；未提高预算或继续
重复运行以挑选一次通过。这个结果仍待性能阶段分析，也不等于 P7 十分钟验收。

## 后续完整性工作

Aim 原生状态对照已完成；Aim/Overlay/BasePoses/LayerBlending 的正式最终
上身接线仍未完成。接下来实现真实 Overlay 来源与状态、外围最终层、脊柱/
手部修正，将最后生成的曲线统一反馈至角色旋转与脚部。之后闭合 Foot IK、
Foot Lock、pelvis 和平台约束，以同输入、同脚相位的 UE/Godot 多帧和人工
对照验收上身、换髋和起步滑步。不能将本批结论称为这些视觉问题已解决。

原规划 P5A 剩余动作/事件、P5B 全 Overlay/道具、P5C Mantle/Roll/Root
Motion、P6 Ragdoll/Get-up/Pose Recovery/Camera、P7 十分钟预算仍全部保留。
音频暂缓；本批未 commit/revert/merge，保留工作区既有改动。
