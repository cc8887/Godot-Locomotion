# 整图配对定位脚部空间和曲线存在性缺项

第一百四十七批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

后续纠正：第 148 批发现本批捕获的来源列表混淆紧凑索引与 formal ID，
不能用于来源时间/权重一致性结论；骨骼、曲线和输入配对不受影响。
修正及站立末端缺项见 `2026-09-13-standing-cycle-tail.md`。

## 实际修复

整图配对发现完整所有者输出的是 FBX 骨骼局部姿势，但 Foot IK 控制器和
全局 FootLock 属性采集使用了常规 Godot 坐标转换。原始来源编译器实际使用
UE -> FBX 的 Y 反射：位置 `(X,-Y,Z)*.01`、旋转 `(-X,Y,-Z,W)`。
不能把该骨骼姿势按 Godot 世界轴 `(Y,Z,-X)` 处理。

新增显式 `AlsFootIkPoseSpace`。完整所有者选择 Fbx，控制器按该基处理
组件空间锁脚、骨骼空间膝目标、双骨 IK 输入/结果；世界空间脚部偏移和
pelvis 保持 Godot 世界轴转换。全局属性采集同步转换 FBX 组件位置/旋转，
并从组件到世界旋转中移除固定的 FBX 基，再计算 UE 组件旋转下的速度补偿。
通用 Core 的 Godot 基入口仍可使用，不能将这两个坐标契约混为一谈。

原生 `ModifyCurve/Scale` 会把命名曲线加入输出，即使输入没有该曲线。
此前站立/蹲姿的静止和 Rotate 状态错误地使用保留缺失的贡献缩放。
新增 `ModifyScale` 并替换这四处 `RotationAmount` 写入；普通姿势贡献
`Scale` 继续保留缺失，避免把两种操作混用。

## 配对工具与边界

`AlsFullGraphParityCapture` 经 `layered_frame_input_smoke.tscn --
--full-graph-capture --parity-output=<绝对路径>` 调用真实完整映射所有者，
保存其地面、Aim、分层、脚部、换向/Pivot 等属性以及最终姿势/曲线/来源。
六条轨迹覆盖 30/60/120 Hz、两个横移起始方向、起步、反向和停步，共 1,260 帧。
场景采用显式速度/加速度和受控物理观测，不是实际 Motor 的键鼠轨迹。
Godot float 提升为精确 double 输出，骨骼姿势按原导入器的逆变换恢复 UE
骨骼局部轴和厘米。UE 使用上一批完整 AnimGraph 导出入口读取这些属性。

`scripts/compare-full-graph-traces.mjs` 检查同帧输入、按骨骼名对应的局部姿势
和曲线存在性，不按姿势自动调整时间。位置阈值 .001 cm、角度 .02°、缩放
1e-5、曲线 1e-4。UE JSON 把负零写成零，该编码区别单独计数（3,526 处）；
其他数值输入要求完全相等。来源时钟也已记录，但尚未进行完整自动对应校验。

首次导出误用了世界轴转换，已修正并保留失败文件。修正导出后第一帧骨骼可
匹配，第二帧启用脚锁时出现腿链/膝目标差异，从而定位到上述运行时基错误。
上一批的预设夹具还把 VelocityBlend 的 F/B/L/R 顺序当作方向枚举顺序；已
纠正为原枚举 Forward/Right/Left/Backward。此前预设是混合了右向权重和后向
枚举的受控输入，不能解释为真实左右换向。当前配对直接读正式方向模型输出，
没有使用该错误预设。

## 当前 UE 配对结果：尚未通过完整姿势门槛

- Godot：`artifacts/full-graph-godot-147-fixed.json`，同前缀 request 文件。
- UE：`artifacts/full-graph-ue-147-fixed.json`，冷导出退出 0，0 error、0 warning。
- 差异报告：`artifacts/full-graph-parity-147-fixed.json`。比较命令正常以 1
  退出，表示剩余姿势不一致；不能称为通过。
- 全部 1,260 帧曲线 presence 一致，数值最大差 `2.384185791015625e-7`。
- 初始静止阶段姿势匹配。首次超阈值是 30/60/120 Hz 的第 16/31/61 帧，
  即起步帧；包含 pelvis、手部 IK 和膝目标。共 964 帧仍有姿势超阈值，
  最大位置差 `2.235704939316043 cm`、角度差 `2.467160878949694°`。
  其原因仍须继续定位，没有放宽阈值或把它归为可忽略浮点误差。

修复前文件分别保留 `full-graph-godot-147-exact`、`full-graph-ue-147-exact`、
`full-graph-godot-147-bone-local` 和相应 first/bone-local 差异报告。
修复后脚部属性也随基转换而改变，前后是各自同属性的 UE 配对，不能把两次
报告简单当作完全相同输入下的误差百分比改善。

普通 Editor 重启导出 `full-graph-ue-editor-147-fixed.json`，退出 0；与冷导出
递归比较 3,859,968 个值/容器，无差异。普通 Editor 启动仍有两条既有
AutomationTest `Condition failed`，不能称整个 Editor 日志无错误；完整图
导出成功。此次启动前完整 Editor/插件构建审计通过，BuildId 为
`4855b08e-0078-431b-b819-d1a7a48b513d`。

## 自动回归

优化 Debug 构建 0 警告、0 错误。Import 脚部/输入专项 62 项通过，包括
新增加的两个 FBX/常规组件基等价用例，覆盖组件旋转、世界偏移、脚锁、
pelvis、膝目标和完整 IK；Core 曲线 14 项通过。TRX 位于
`artifacts/test-results/full-graph-foot-basis-147.trx` 和 `full-graph-curves-147.trx`。
既有零分配专项包含在脚部测试中，禁用 tiered compilation 执行。

真实 Worker 单线程和并行各 960 帧通过，result=`CDDDE9E186232A35`、
fullPose=`D045017EB32832C1`、sampledPose=`4C82BCEFCE8EF047`、
root=`DB5B813964D3479C` 一致，224/258 来源，37 事件，lag/stale=0，
316 锁脚帧、910 偏移帧，legacy writes=0。
日志 `full-graph-production-single-147-final.log`、`full-graph-production-parallel-147.log`。

首次生产回归在旧“只增加根来源后结果应不变”的摘要断言处失败，日志
`full-graph-production-single-147.log` 保留。本次是有意修复脚部姿势与曲线
存在性，旧摘要保留的是错误基；已改为当前完整结果与完整姿势双摘要门槛，
并在两种线程模式验证，不再声称与旧错误输出兼容。

根隐藏/恢复组合 840 帧、18 次晚期失败和每帧重试通过；晚期姿势/来源事件
故障回滚通过，回调泄漏为零。日志 `full-graph-root-dispatch-147.log`、
`full-graph-late-transaction-147.log`、`full-graph-late-events-147.log`。

默认 BaseLayer 单线程兼容回归通过，实际为 180 帧、75/109 来源，
lag/stale=0，日志 `full-graph-default-demo-147.log`。该次命令没有开启
600 帧覆盖选项，不能记录成 600 帧回归。

## 实际渲染和后续

完整脚部入口 parallel 横移回放完成 720 帧、120 张截图。查看
`artifacts/full-graph-visual-147/movement-contact-sheet.png`，动作持续变化，
本回放未触发 30° 相邻帧脚部旋转诊断；最大值仍为 14.138°。
该指标只量化脚部瞬时转角，不能证明膝盖、上身、换髋延迟或支撑滑移完全正确。
日志 `artifacts/full-graph-visual-147.log`，目录保留全部 PNG、frames.json、
body-frames.json、violations.json。相同采样帧的截图及脚部状态与旧回放不同，
确认渲染实际运行了此次完整入口修改。

下一项继续定位起步后的上游姿势求值分歧，补各层/来源的对应诊断，再接
真实角色与平台接触窗口。默认仍为 BaseLayer，完整入口尚未默认启用。
P5A 剩余通用动作/事件、P5B Overlay/道具、P5C Mantle/Roll/Root Motion、
P6 物理恢复/Camera 和 P7 十分钟预算继续保留。既有 Core 23 项失败、
Import 分配不稳定与旧性能超预算未在本批整体关闭；音频暂缓。
