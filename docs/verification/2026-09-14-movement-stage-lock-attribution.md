# 跑步停止突转的实际阶段定位（第一百八十三批）

## 结论

上一批修复分发恢复并通过矩阵，属于有进展。本批继续 P3/P4 整角色验证，
用 `--strafe --run` 复现第 616 帧左脚 41.100025° 突转。新完整 Rig 不能
消除该问题，原 30° 门槛保持失败。第 179 批通过的回放不能覆盖这个跑步
配置；不能继续把当时的 13.095° 当成当前跑步问题已消失的证据。

新增真实阶段诊断显示：该帧脚部处理前源姿势仅变化约 1.821°，进入 Rig
的脚锁目标已经跳变约 41.10004°。完整 Rig 与最终 Skeleton 基本原样传递
这个变化。UE 原生脚锁函数用同样输入也产生 41.10001°，不是 Godot 下游
脚踝约束或 Skeleton 写回独自引入的错误。

## 诊断接线修复

旧 `FootPose.Uncorrected*` 在完整动画控制器 Apply 后采样；启用完整脚部
入口时，它已经包含脚部处理。日志把它标为 `animation` 会误导归因。
现在显示为 `controller_output`，分析脚本对应改为 `ControllerOutputDegrees`。
旧合同字段名保留，不将它重新解释成真正的 pre-foot 骨骼。

`AlsRefactoredFootRigPose` 增加实际 pre-foot 左右脚、实际输入目标，连同
已存在的 post-rig 脚姿势一起只在 Commit 发布；Prepare/Evaluate/Cancel
不能覆盖已发布数据。均为固定值类型字段，不在默认路径增加逐帧对象分配。

已有 `--based-foot-lock-trace` 现在也能接通 Refactored 完整脚部所有者，
通过实际 `_locks` 的 Previous/Input/Result 获取输入，不由诊断重算一套
候选。默认仍不分配这份可选跟踪对象，取消与提交身份保持。

生产渲染样本加入 `RefactoredRigFeet`。新增
`tools/diagnostics/analyze_refactored_movement_capture.mjs` 校验阶段目标与实际
脚锁 Result 完全一致，保存旋转阶段报告及可供 UE 回放的输入。
同时增加起步/停止接触图，便于检查时间连续的实际截图。

## 实际回放与观察

所有运行启用完整分层、Based 锁脚、Refactored 移动/姿势曲线及完整脚部参数。

- `artifacts/movement-183-strafe.log`：严格运行，第 616 帧退出 1。
- `movement-183-strafe-diagnostic/`：保留 720 帧、120 张截图及一个失败，退出 1。
- `movement-183-strafe-stages/`：新增阶段与锁输入跟踪，720 帧、120 图，同一
  失败退出 1；检查了起步、左右方向和停止接触图。不是用户人工验收。
- 跟踪前后逐帧比较 Input、Result、FootPose、Cycle、Rig、Locks 完全一致，
  `FOOT_STAGE_TRACE_NONINTERFERENCE_OK frames=720`，没有通过诊断改变动作。

最终阶段报告 `artifacts/movement-183-final-stages.report.json`：

| 第 616 帧左脚 | 数值 |
| --- | ---: |
| pre-foot 组件空间旋转变化 | 1.8210025° |
| 脚锁目标组件空间旋转变化 | 41.1000401° |
| post-rig 组件空间旋转变化 | 41.1000369° |
| 最终世界旋转变化 | 41.1000495° |
| 锁量 | 0 → 1 |
| 上一 Final 对大腿轴夹角 | -158.69965° |
| 当前动画目标对大腿轴夹角 | -2.13495° |
| 当前目标与上一 Final 距离 | 3.19851 cm |

满锁时按原规则捕获上一 Final，而不是当前目标。上一 Final 已在 90° 大腿
约束之外，触发约束旋转，随后还触发 40° 脚掌限制。这里 3.20 cm 的位置差
跨过了组件原点附近的角度边界，不能按绝对位置差很小判断影响很小。

第 2 帧从未有效的初始目标到有效目标也有较大“目标角度差”，但最终姿势
连续，报告保留其有效性边界，不把它当作可见脚部突转。

回放有 73 帧 `WaitingForFeet`；第 314/493 帧开始两次换髋，`Feet_Crossing=0`。
可证明等待和曲线门控在本配置实际运行，尚不证明等待时间/完整动作等同 UE。
接触图是连续截图；脚掌真实支撑滑移验收仍需场景配对，不能用低位脚代理判定。

## UE 原生函数配对

完整 Editor-target 构建零动作、退出 0，四个项目插件审计通过，BuildId
`8531669e-23fc-4bc3-9bab-fd5876472ee1`。构建记录前缀：
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T165708848Z-dfba2e164eba42e6b682fe924920b162`。

使用已有 `AlsBasedFootLockProbe`，没有修改插件 C++。首帧脚变换尚未有效，
原生探针只接受有效输入，因此请求明确排除第 1 帧，保留 719 帧/1438 次
左右脚求值。没有伪造 Valid=true。每例加载实际捕获的上一状态，属于函数
配对，不是 UE 整角色或原生状态连续推进。

`artifacts/movement-183-native.json` 与普通 Editor 重启得到的
`movement-183-editor.json` 字节一致，SHA-256：
`467E4F45BF329C2DAB606CF645E0E565FEEBE323933FE391A8CC78BCDFA99889`。
两个进程退出 0。正常 Editor 仍有两条已有 AutomationTest `Condition failed`，
不能称整个日志无错误；本次导出与关闭成功。

位置最大差 `0.000015258789 cm`（门槛 .001 cm），旋转最大差
`0.0000546415°`（门槛 .02°），锁量最大差 `2.88788e-8`（门槛 1e-6）。
第 616 帧原生突转 41.10001°；诊断隔离大腿约束得到 68.69966°，两约束
都不执行时变化近零。隔离只解释机制，不作为关闭约束的生产方案。

导出 Python 改为在普通 Editor 中延后退出，沿用已验证的 Slate 回调方式。
未改插件配置/二进制，无需对未变插件重复打包或 DataValidation。

## 自动验证与剩余工作

Import 两个脚部帧类专项 16/16 通过，包含新输入/输出诊断发布、改变源姿势、
取消保持、可选跟踪启停。首轮测试误用了不存在的 `FromSingle`，已改为实际
四元数构造函数；不覆盖首错。TRX：`artifacts/tests/foot-diagnostics-183.trx`。
Godot 优化 Debug 构建零警告/错误；正式 parallel 960 帧通过，结果
`B6345BBBADCB7487`、完整姿势 `66E084F8FF0938FE` 保持。Python 语法与
`git diff --check` 通过。本批没有重跑无关的全套 Core/Import。

下一项收敛到停止阶段的版本一致性：V4 的状态/Slot/曲线与 Refactored 的
目标捕获时序必须按一套完整合同对照。当前最终曲线别名相等不能证明两版
曲线语义、资产或捕获条件等价；第 162 批已证实停止资产曲线存在差异。
先补该上游来源/图合同及完整角色对照，再决定生产入口的接线；不任意延迟
锁曲线、不改满锁阈值、不放宽大腿限制，也不继续用下游 Rig 补丁宣称修复。

默认完整入口、起步/换髋/上身/脚部整体验收、既有 Core 23 失败、Import
1 跳过与未归因栈溢出仍开放。原 P5A–P7 全范围继续，音频暂缓。
