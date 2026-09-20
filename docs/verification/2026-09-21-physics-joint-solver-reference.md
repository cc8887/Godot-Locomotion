# Chaos 真实步进参考与软限制释放修复

本批基于 `c91f8f5`，直接在 `.` 的 `main` 实现。没有建立项目副本，
用户未提交的 P4 计划修改保留。实验关节尚未接入普通 Demo，Ragdoll 未验收完成。

## 原生参考与移植完整性

新增 `AlsPhysicsJointSolverReference.cpp`、导出命令和 Python 包装，使用本机
UE 5.9.0 的真实同步 Chaos 物理场景步进，不是 C# 公式生成的期望值。
覆盖 Mannequin/AnimMan × spine_02/neck_01 × 30/60/120 Hz × 三轴 × ±0.6 rad
× 资产驱动/ALS 满速驱动，共 144 组。每组固定父体、动态子体，关闭重力、接触和
动画 tick，仅保留一个 linear/cached joint；每组重新创建和销毁刚体/约束。

每组预热一次，再围绕实际连接点扰动并清零速度，记录初态及 12 个物理步：
1872 份状态、1728 份有效受力。初态受力标记为不可用，避免误读预热旧力。
每步断言 solver time 增量等于 float `1 / Hz`。记录骨骼世界变换、质心线速度、
角速度、醒睡状态、角度、约束力/力矩、原始质量/惯量、有效设置和惯量调节值。
每组时长分别为 0.4/0.2/0.1 秒，不能替代十秒整链参考。

提交数据：`assets/config/v4_physics_joint_solver_reference.json`，3,890,858 字节，
SHA256 `18243B9F6F948C65216D73DC17FF95DD5C8E6422E508C58FF87811819464D0FD`。
坐标仍为 UE 世界骨骼坐标，单位 cm、kg、rad；力为 kg·cm/s²，力矩为 kg·cm²/s²。
不保存或重写 UE 资产。

实测有效设置：MinParentMassRatio=0.2f、MaxInertiaRatio=5、PositionTolerance=0.025f cm、
AngleTolerance=0.001f rad，SIMD 与 position-based drives 开启；全局 position/velocity/
projection 迭代数为 8/2/1。这些来自运行中的 solver，不用结构体构造默认值代替。

原生有两层不同的调节：

1. 刚体阶段按几何包围盒和所有连接臂调节逆惯量。记录的孤立 spine_02 比例约为
   Mannequin `(0.8869, 0.8869, 1)`、AnimMan `(0.7716, 0.7716, 0.9497)`；
   neck_01 约为 `(0.4960, 0.5613, 0.4366)` / `(0.4994, 0.3835, 0.3835)`。
   完整骨架连接臂不同，不能把这些孤立样本比例硬编码到整链。
2. 关节阶段再对两侧质量和主惯量进行局部调节。新增 `AlsJointMassConditioning`，
   对齐 `FPBDJointUtilities::ConditionInverseMassAndInertia`。惯量中间分量也要重映射，
   不是仅截断最小分量。用 144 份实际固定父体记录及 8 份独立 utility 分支参考验证。
   152 份记录不是 152 个独立物理工况；工具尚未接入 Jolt，也不应覆盖共享刚体惯量。

参考源码位于 `../UnrealEngine\Engine\Source\Runtime\Experimental\Chaos`：
`Private/Chaos/MassConditioning.cpp`、`Private/Chaos/PBDJointConstraintUtilities.cpp`、
`Private/Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp`、
`Private/Chaos/Evolution/SolverBodyContainer.cpp`、`Private/PBDRigidsSolver.cpp`。

## 软限制释放修复

原 adapter 在“当前角度或预测角度超限”时都保留软弹簧。物体已经向限内回转、
预测角度已经入界时，这会额外制动，丢掉原生保留的动量。现按 Chaos 从预测连接
姿态初始化约束的方式，仅以预测角度决定活动限制和边界方向。

新增 Godot 回放场景，从原生记录的初始姿态和零速度出发逐步比较。
修正了回放本身的帧序：下一组在其首个物理回调创建，避免 Jolt 硬约束多运行一个
未记录的物理步。初始姿态/速度不一致会失败，不能用错帧数据归因于物理后端。

最明显的 Mannequin spine_02、120 Hz、Z 负向、资产驱动工况：

| 指标 | 修复前 | 修复后 |
| --- | ---: | ---: |
| 最大旋转误差 | 0.870795 rad | 0.125325 rad |
| 第 12 步旋转误差 | 0.789943 rad | 0.032741 rad |
| 第 3 步 Z 角 | -0.436829 rad | +0.064904 rad |

原生第 3 步为 +0.167991 rad。`--assert-limit-release` 断言这一已确认的过零行为，
本批通过；不把误差降为零或完整后端等价作为虚假结论。

完整 144 组原始惯量回放的最大旋转误差从 0.870795 降至 0.403482 rad；
按每组最大旋转误差、1e-6 rad 比较容差计，98 组改善、38 组相同、8 组增大。
增大的包括 AnimMan spine_02 30 Hz 资产驱动约 0.03752 → 0.32606 rad，
60 Hz 约 0.05999 → 0.29509 rad；因此不能宣称所有工况都更接近原生。
全局最大位置误差仍为 12.156 mm。

诊断开关 `--body-conditioning` 仅给孤立对子注入原生记录的刚体惯量：最大位置
误差变为 11.692 mm，最大旋转误差仍为 0.403482 rad。仅补这一层惯量不能消除
剩余差异。回放成功输出始终标明 `parity_asserted=false`，目前也没有比较 Jolt 关节力。

## 整链稳定性：门槛不放宽

40 刚体 / 36 关节，启用 ALS 按骨盆速度更新的角驱动。
十秒测试的最后整整一秒要求最大线速度 <0.2 m/s、最大角度超限 <0.1 rad；
全程锚点分离 <0.2 m。下表没有用第十秒瞬时值代替最后一秒峰值。

| 工况 | Hz | 最后一秒最大线速度 m/s | 最后一秒最大超限 rad | 结果 |
| --- | ---: | ---: | ---: | --- |
| 三米普通落地 | 30 | 0.148757 | 0.166225 | 超限失败 |
| 三米普通落地 | 60 | 0.181105 | 0.043088 | 通过现有门槛 |
| 三米普通落地 | 120 | 0.597169 | 0.043382 | 速度失败 |
| 十米高速倾斜落地 | 30 | 未运行到末秒 | 未运行到末秒 | 第 21 帧锚点分离 0.276133 m，失败 |
| 十米高速倾斜落地 | 60 | 0.785696 | 0.111874 | 速度/超限失败 |
| 十米高速倾斜落地 | 120 | 0.349394 | 0.038384 | 速度失败 |

60 Hz 普通落地上一版为 0.363421 m/s，本批降为 0.181105 m/s；最大锚点分离
99.453 mm。最后一秒最大角速度仍为 2.404956 rad/s，此项目前只记录，未设门槛，
通过不能解释为已完全睡眠或 Ragdoll 完成。

30 Hz 无接触整链最后一秒超限从 0.251030 降为 0.186646 rad，仍失败；
不能把剩余问题都归因于碰撞。60/120 Hz 无接触本批未重跑，不冒用上批通过结论。
高速落地参数是起始高度 10 m、速度 `(2,-12,0)` m/s、倾斜 `(0.5,1.1,0.2)` rad。

早期 `release-*-high.log` 的 PowerShell 可选参数未正确传入，实际配置显示
`high_drop=False`，不计作高速测试。有效高速数据只采用 `high-explicit-*.log`，
配置均核实为 `high_drop=True`。所有失败日志保留。

## 构建、导出与回归

- Godot 4.7.2 Mono `ed1daf0bf` 优化构建通过，0 warning / 0 error。
- Core Release 2605 通过；按项目规则排除 `AlsP5aGoldenTests` 和
  `AlsP5aTraceSchemaTests`。新增质量调节测试包含零托管分配、单位转换和非法输入。
- 新增 Import 原生参考测试 4 通过：覆盖矩阵/真实移动和受力、实际 overrides 与惯量、
  原资产身份/参数绑定、关节调节原生参考。没有重跑 Import 全库。
- Godot 原始/条件惯量各 144 组回放退出 0；释放过零回归退出 0。
- 修复后重新运行 30/60/120 Hz × 三轴 × 正负方向的 18 次十秒单关节测试，
  每次含两套资产，全部退出 0，最后一秒线速度和超限均为 0。三个频率最大锚点
  分离分别为 3.403 / 3.506 / 3.440 mm。此场景从导入参考姿态开始，和原生轨迹
  回放的预热后初态不同；例如这里 Z 负向扰动没有超限，不能混作同一个用例。
- UE 导出器按 `ue-diagnosing-plugin-build-load` 技能完成完整 Editor host 构建和
  插件审计，未复制零散 DLL 或伪造 BuildId。使用引擎自带 .NET 10；首次误用系统
  .NET 9 的调用在编译前失败，后续已修正环境。
- 最终构建指纹 `8E1E54A42BE70A795AE8F26A75DEF8878D4B096F319B3D96ECAFDE3A660FDC59`，
  BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`。
- 最终 DataValidation 退出 0，0 error / 3 warning。警告是旧 PawnActionsComponent
  缺失、旧 Navmesh 版本及对应资产加载警告，没有在本批重写地图。

导出器最初每组创建一个预览世界：NullRHI 成功，但普通 D3D12 编辑器在保存数据后
出现 `CreateReservedResource` 2 GiB 预留与 `DXGI_ERROR_DRIVER_INTERNAL_ERROR`。
将 144 组改为复用一个预览世界，每组仍重建对象并断言仅一个原生约束后，
最终普通 D3D12/default map 运行退出 0，无该设备丢失错误；与最终 NullRHI 及提交
数据的 SHA256 完全一致。普通 Editor 启动仍记录两条已有的 AutomationTest
`Condition failed`，因此不声称整个启动日志零错误。此前失败日志也保留。

本地证据位于 `artifacts/physics-joint-solver-20260921/`，不纳入 Git：
`core-release.trx`、`native-reference-tests-final.trx`、`final-release-assert.*`、
`final-replay-raw.*`、`final-replay-conditioned.*`、`final-pair-*.log`、`release-chain60.log`、
`release-30-normal.log`、`release-120-normal.log`、`release-flight30.log`、
`high-explicit-*.log`、`single-world-export.log`、`editor-single-world.log`、
`final-data-validation.log`。UE 构建审计位于原项目 `Saved/Logs/PluginBuild/`
的 `20260920T164724435Z-6a03c7f252aa4950a85f1f73687921bb-*`。

## 复现和后续

```powershell
Set-Location .
$godotAlsEngine = 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
# report 必须为尚不存在的绝对文件路径
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_reference_replay.tscn -- --assert-limit-release --report=./artifacts/release-check-new.json
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_reference_replay.tscn -- --report=./artifacts/trajectory-check-new.json
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives
# 当前预期仍失败的工况
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives --no-contact
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=120 --als-drives --high-drop
# 构建并部署同版原生插件后，导出到新的绝对路径
& '../UnrealEngine/Engine/Binaries/Win64/UnrealEditor-Cmd.exe' '../AdvancedLocomotionSystemV/AdvancedLocomotionSystemV.uproject' -run=AlsGodotExport -PhysicsJointSolverOutput=./artifacts/native-trajectory-new.json -unattended -NullRHI -nosplash -nop4
```

下一步先补刚体几何/连接臂惯量调节的原生输入和实现，并解决关节局部质量调节
在共享刚体上的表达；继续对齐预测积分、单边软限制与 drive 独立行、投影和接触。
当前 Jolt 合并加权目标仍不是 Chaos 独立约束行，不能以本批修复宣称 1:1 移植。
通过各帧率/高速整链门槛后，再接普通角色的物理所有权、姿态交接、
Ragdoll/Get-up/Pose Recovery。Mantle、完整 ALS Camera、最终十分钟性能预算仍在清单中。
