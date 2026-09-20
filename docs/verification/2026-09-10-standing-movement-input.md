# 第二十一批：Standing 移动输入与 ShouldMove 入口

## 范围与结论

本批属于完整性恢复计划 A 的起停入口补项，不是 P5C Root Motion 功能。
实际 Demo 已从单纯 `speed > 0.01 m/s` 切到源 AnimBP 的 ShouldMove 规则。
输入、结果和 Cycle 候选使用同一帧身份，随既有 Controller/Worker 事务提交与回滚。
没有修改人工确认过的相机/键鼠，没有增加时钟、事件队列或改变 Core 帧 ABI。

**尚未完成完整起停链。** 外层仍是临时的 `MoveToward(delta * 5)` 权重变化，不是
UE 的 Main Grounded States、Not Moving/Moving/Stop 状态机。Detail/Plant 组件尚未
进入 Demo，Pivot Notify/状态事件与重置尚未接通。本批不能作为起步滑步修复验收。

## 原生依据

`AlsStopGraph -IncludeInputs` 新增独立输入导出，包含 94 张图：现有 Cycle/Detail/Stop、
Main Grounded States，以及角色的 SetEssentialValues / BPI_Get_EssentialValues。
通过真实 ALS_AnimBP 的编译函数 ProcessEvent 执行 40 个 ShouldMove 边界案例，
不是由 Godot 生成期望值。输出包含角色 CDO 的 TriggerPivotSpeedLimit=200 cm/s，
仅保留数据供后续事件接线，本批没有把它做成按键冷却时间。

- Speed：角色实际水平速度长度，排除 UE Z / Godot Y。
- IsMoving：`Speed > 1 cm/s`，严格大于。
- MovementInputAmount：CharacterMovement 输入加速度长度 / 最大加速度。
- HasMovementInput：`MovementInputAmount > 0`。
- ShouldMove：`(IsMoving && HasMovementInput) || Speed > 150 cm/s`，严格大于。

导入编译器核对表达式、函数/变量来源、BPI 传递和赋值依赖顺序，读取源阈值并转换单位。
Godot 适配采用电机实际消费的已解析命令 InputAmount，不使用 ActualAcceleration：
后者还包含制动和碰撞反馈。这是现有 Godot 电机的语义适配，不是宣称其运动物理
已经逐帧等同 UE CharacterMovementComponent。

新规范文件：`assets/config/v4_locomotion_inputs.json`。
重复原生导出的 SHA-256 均为：
`C63DDF1BC78AC9B2B84F048CFAF16E4C41E514F88011FCB2CE55600ACAB2B956`。
旧 `-IncludeCycle` 再导出保持 69 图及原字节摘要
`60E5D6F540F186C1A2279004E25AD537A9166B814990B86992C4C03F83E0301F`。
原生探针 marker：`ALS_LOCOMOTION_INPUTS_OK character_graphs=2 should_move_cases=40 assets_saved=0`。

## 实现与验证

- Core：新增数值型 AlsStandingMovementInputModel，有限值与严格阈值检查。
- Import：新增 AlsLocomotionInputCompiler，错误源表达式/链接必须拒绝，阈值可数据驱动。
- Demo：Worker 传完整 AlsFrameInput；Controller 为 Standing 图要求显式同帧输入；
  Cycle 用 ShouldMove 决定现有外层权重目标，并保存到候选帧。
- 诊断：实际 Worker 同帧检查、晚期故障回滚检查和多帧 JSON 均覆盖 Movement。

验证结果：

| 检查 | 结果 |
| --- | --- |
| Godot Debug 构建 | 0 错误、0 警告 |
| Core 常规 Debug | 1611/1611，明确排除 AlsP5aGoldenTests、AlsP5aTraceSchemaTests |
| Import 完整 Debug | 658/658 |
| Core Release 输入/时间相关 | 36/36 |
| Import Release 新编译器 | 9/9，包含 40 个原生案例对照 |
| Standing 组件 30/60/120 Hz | movement_frames=5040，包含无输入制动、阈值、零最大加速度、非法/过期输入及重试 |
| 原有 Standing 覆盖 | source_timing=15216，timing_authority=1260，hip_transitions=9，wait_frames=371 |
| 优化 Debug、关闭分层 JIT | 同一 Standing smoke 通过，稳态及活动采样 alloc=0B |
| 新 Cycle 单/多线程 | 各 180 帧，120 帧同帧输入/源时间检查，结果与完整姿势摘要一致 |
| 新 Cycle 晚期故障注入 | 两模式 source_sync=1、movement_input=1，未发布失败帧 |
| 旧 verify-p4-pose.ps1 | 图、双模式脚部、晚期回滚和零分配全部通过 |

单/多线程共同摘要：result=`C658034A39C5917B`，full_pose=`076E345A0151A012`，
root=`309E8D0E0BEEB2CB`，与第二十批相同。

## 多帧回放

每组实际运行 720 帧、保存 120 张 1280x720 截图；已检查横移联系表和起步原图。
路径分别为 `artifacts/should-move-strafe-20260910` 与
`artifacts/should-move-run-strafe-20260910`。

| 固定镜头回放 | 最大单帧脚旋转 | 起步低位脚位移代理 | 与上一批首次骨骼差异 |
| --- | --- | --- | --- |
| 走路横移 | 9.986°，上一批 11.801° | 6.12380498154744 cm，未改善 | 第 602 帧，松键制动 |
| 跑步横移 | 11.857°，上一批 11.807° | 9.56777951663101 cm，未改善 | 第 609 帧，松键制动 |

1,440 帧输入身份、水平速度、ShouldMove 与 Result/Runtime/Cycle 时间检查通过。
与旧速度门控不同的帧：走路 602–606，跑步 609–614；不是起步阶段。
例如走路第 602 帧无输入、速度约 1.25 m/s，旧 MovingWeight 仍为 1，当前开始下降。
显示到两位小数的 1.50 不代表数值恰好等于阈值，判断保留实际浮点值和严格边界。
交错步等待帧数 42/63、换髋开始帧 291/487 与 319/496 保持不变。
低位脚位移只是代理，不是经 UE 接触状态确认的支撑脚滑移，更不代表原版等价。

## UE 工具链检查

使用 ue-diagnosing-plugin-build-load 技能的全项目 Editor-target 构建及审计；
AlsGodotExporter、AutoTestTools、BlueprintLisp 审计通过。
BuildId：`a62acd02-ebd3-4df3-b93a-71842617113c`。
全目标构建记录：`20260910T103301550Z-97fed81ed6c444769fade3cabe3ae0f2`。
未复制打包 DLL 回项目、未修改 BuildId、未保存 UE 源资产。

- 输入导出/重复导出退出 0，各有一条旧 PawnActionsComponent 缺失警告。
- 旧 Cycle 导出退出 0，0 错误、0 警告，数据摘要不变。
- 全项目 DataValidation 退出 0，0 错误、3 警告：旧 AI 组件和导航网格版本问题。
- 非 NullRHI Editor 冷启动加载项目模块，达到 FEngineLoop::Init 完成并按 TestExit 退出 0。
  日志另有两条 `LogAutomationTest: Error: Condition failed` 和引擎材质/渲染警告，
  未定位到具体测试；只确认启动完成，不能称为完全无错误的交互 Editor 验收。
- 独立 Win64 BuildPlugin 打包成功，输出到
  `artifacts/unreal/AlsInputsPluginValidation-20260910`，未部署打包产物。

日志位于 UE 工程 `Saved/Logs/AlsLocomotionInputs*-20260910.log`、
`AlsLocomotionCycleRepeat-20260910.log`、`AlsInputsDataValidation-20260910.log`、
`AlsInputsEditorRestart-20260910.log`。打包日志由 AutomationTool 保留。

没有重跑两个长耗时 P5 Golden/TraceSchema 套件、P5 全矩阵、十分钟最终预算或人工
Demo 验收。构建、组件与自动截图通过不替代这些阶段的完成条件。

## 下一步

先导出外层 Main Grounded States / Standing 起停的编译状态表、退出顺序、过渡配置
和状态事件，补真实状态权重、重入/相关性与缓存归属；不能用当前 MovingWeight
替代 Detail 要求的两层状态机权重。随后把已有 Detail、惯性化和 Stop/Plant 组件接入
同一生产候选事务，Pivot 来源保留原 Notify/状态事件。再完成 P5A 全局源身份、曲线/
Notify 生命周期与同步分发，并用同输入 UE/Godot 全状态/骨骼轨迹定位首处分歧。
动态上身分层、Overlay 与 P5C/P6/P7 按主计划继续，不用后续 Root Motion 补偿掩盖起步问题。
