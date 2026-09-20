# V4 动态移动数据与原生地面速度积分

第一百五十四批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 完整性发现

第 153 批关闭了受控输入下完整动画图的姿态差异。真实角色仍有另一条
未闭合链路：`AlsCharacterMotor.Step` 用常量 MaxAcceleration/制动力，
沿目标速度方向分段 MoveToward；`CalculateDesiredSpeed` 使用旧方向速度表。
原 V4 `UpdateDynamicMovementSettings` 按旋转模式/姿态选择 MovementModel，
按 AllowedGait 写入 MaxWalkSpeed/MaxWalkSpeedCrouched，并将 GetMappedSpeed
输入 Movement Curve，X/Y/Z 分别写入加速度、制动力与 GroundFriction。

UE `UCharacterMovementComponent::CalcVelocity` 在有输入且未超速时也使用摩擦
改变速度方向；无输入或超速时进入 ApplyVelocityBraking，再加输入加速度并
限制速度。制动包含摩擦系数倍率、子步和停止阈值。这个差异足以改变提供给
动画的起步/反向速度轨迹，不能仅靠给换髋增加固定延迟修复。

## 本批实现

- `export_character_movement_inputs.py` 只读导出八个原始函数、MovementModel
  全表、CharacterMovement 默认值及 Normal/Responsive/Sluggish 三套
  CurveVector 的原生关键帧和每套 401 个 GetVectorValue 采样点。
- 原生 `SetMovementProbeInput` 只接受 Editor 世界中临时角色的真实移动组件，
  设置受保护的 Acceleration/AnalogInputModifier；Python 直接调用引擎的
  `CalcVelocity`。没有复制算法作为 UE 对照，也不保存关卡或资产。
- `AlsGroundVelocity` 用 native cm、double 向量及原 float 标量边界实现
  地面输入积分、转向摩擦、超速制动、模拟输入限速和制动子步。
- `AlsCharacterMovementCompiler` 校验动态参数图的控制顺序、组件所有者、
  曲线/XYZ 通道、两项速度赋值和 Gait 映射。复用现有严格旋转图校验器，
  对新导出的 MappedSpeed/模式姿态选择/更新顺序函数本身校验；忽略非语义
  的原生未连接引脚 GUID 差异，不比较整段 T3D 文本是否相同。
- 原生富曲线关键帧进入 `AlsCharacterMovementModel`，按 3 模式 × 2 姿态
  编译。Constant/Linear/Cubic 与端点外常量采样保持；NormalMovement 在
  映射速度 2 到 2.1 的 Constant 段保持。样本用于校验，不代替关键帧。
- 模型已经在 `AlsMovementGraphDefinition` 生产共享定义加载。

| 原始设置 | 实际值 |
| --- | --- |
| 正常站姿 walk/run/sprint | 175 / 375 / 650 cm/s；Aiming walk 165 |
| 蹲姿 walk/run/sprint | 150 / 200 / 300 cm/s |
| 站姿映射速度 0：加速度/制动力/摩擦 | 2000 / 1500 / 5 |
| 站姿映射速度 2：加速度/制动力/摩擦 | 2000 / 1250 / 4 |
| 站姿映射速度 3：加速度/制动力/摩擦 | 750 / 500 / 0.5 |
| CDO BrakingFrictionFactor / MinAnalogWalkSpeed | 0 / 25 cm/s |

## 验证范围

正式输入：`assets/config/v4_character_movement_inputs.json`。
实际 CMC 3600 个案例覆盖 30/60/120 Hz、80/200 ms，零/低/运行/超速速度，
前向、反向、垂直转向、半量模拟输入、无输入，摩擦 0/1/4/8、制动倍率
0/1/2 和制动力 0/800。Godot Core 输出向量差平方不超过 1e-16 cm²。
这不含 Actor Tick、碰撞、PhysWalking 子步、坡面、平台、空中、Root Motion
或 AI requested movement。独立 CalcVelocity 大步不等价于完整 CMC 物理大步。

三套曲线共 1203 个向量、3609 个标量与原生采样校验通过，绝对门槛 .001
native 单位。Import 新增 9 项含上述矩阵、模式姿态步态和七项破坏性输入
校验，加原旋转 8 项共 17 项通过；Core 97 项相关回归通过，新增三项覆盖
最小 Tick、非法输入及热身后 10000 次调用零托管分配。

测试记录：`character-movement-compiler-154-validation.trx`、
`character-movement-core-154.trx`，位于 `artifacts/test-results`。
优化 Debug 构建 0 warning/0 error。生产双模式各 960 帧通过，共享定义可加载，
结果 `F6021FE2E30E494D`、完整姿态 `81FD4BE13734881C` 与上一批一致；
这不是新物理模型已接入的证据。日志为
`character-movement-production-single-154.log` 和
`character-movement-production-parallel-154.log`，位于 `artifacts`。

## UE 构建与导出

使用 `ue-diagnosing-plugin-build-load` 技能执行完整 Editor 目标构建/插件审计。
该技能引用的两个 superpowers 技能在本地不可用，使用首错日志和实际退出码/
产物校验作为替代。只同步原生源文件，未复制 DLL。

完整构建/三插件审计通过，日志前缀
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T054912943Z-0315255261ae4ae7b75ca72eec716711`。
BuildId `f880e577-2516-4097-a852-56c91d729e95`，fingerprint
`DADE0C312865F26DB1A529AF963FCE7C085AB35DFEF613B7E59C60C2787AB355`。
原生探针源码两份 SHA256 均为
`803C1F22C4E472838145F609EBEA0E8C481E2247CDC8300211E34DB10F5F956E`。

冷导出退出 0，0 error/1 既有 PawnActionsComponent 缺失 warning；普通 Editor
退出 0，三曲线、移动表、组件默认值和全部 3600 速度案例与冷导出完全一致。
普通 Editor 仍有两条既有 AutomationTest Condition failed，不能称全日志无错。
DataValidation 退出 0，0 error/3 warning。日志均在 `artifacts/unreal`，
前缀 `character-movement-`、后缀 `-154`。

隔离 BuildPlugin 退出 0，产物在
`artifacts/unreal/AlsCharacterMovementPluginValidation-20260913-154`，日志
`character-movement-package-154.log`。该包没有部署，打包后项目三插件审计
再次通过。

首轮 Python 方法名、实例默认属性可写性、受保护输入和未绑定 UpdatedComponent
失败日志完整保留。最终使用真实 Capsule 的 SetUpdatedComponent 建立有效
组件，原生保护条件保留。早期缺夹具/文本 GUID 比较失败的测试记录同样保留。

## 下一项与明确边界

数据齐全和组件通过已经完成；真实 Motor 接线、UE 整角色配对、人工通过均
尚未完成。本批没有改变 Demo 移动速度或切换默认动画入口，没有重拍相同
运动路径来声称新的视觉修复。

下一项先核对 Actor/CMC 的 Tick 先后、当前/前帧参数和初始设置，再接入
Motor 的动态加速度/制动力/摩擦与速度限制、AllowedGait、落地临时制动倍率
及生命周期恢复。随后用真实角色采集起步/反向输入、位置、速度、动画相位、
Feet_Crossing/HipOrientation_Bias、接触窗口和平台相对脚轨迹，并做原生配对。
这些属于 P3/P4 移植补完；P5A 余项、Overlay/道具玩法、Mantle/Roll/RootMotion、
Ragdoll/Get-up/Pose Recovery、完整 Camera 与最终十分钟预算继续按原规划。
