# 刚体惯量调节移植与整链复验

基于 `d359e23`，在 `D:\GodotALS` / `main` 继续实现。没有新建项目副本。
用户未提交的 P4 计划修改保持原样。本批推进真实 Ragdoll 的物理前置条件，
没有把实验物理链路接入普通 Demo，也没有宣称 P5C/P6 完成。

## 实现与原生证据

新增原生 `PhysicsInertiaOutput` 导出器、纯值计算 `AlsBodyInertiaConditioning`、
拓扑编译器 `AlsBodyInertiaCompiler`，并接入 `AlsPhysicsJointSet`。
数据来自实际同步 Chaos 场景预热后的内部刚体，不是把资产原始惯量当作有效惯量。

参考文件 `assets/config/v4_physics_inertia_reference.json` 为 161,000 字节，SHA256：
`9658440D6BF7BB8A6FA67EAEAD8B45DBCAEB0E54D5943C957C6697F3C79AC9E1`。
两次独立冷启动导出完全一致，均退出 0，无导出错误/警告，不保存 UE 资产。
保留旧物理资产和轨迹 JSON 的原始字节，没有重写它们。

矩阵是两套资产各三个拓扑：完整骨架、仅 spine_01–spine_02、仅 spine_03–neck_01。
Mannequin 有 19 个刚体，AnimMan 有 21 个，合计 120 份记录；其中动态记录 42 份，
包含完整骨架 38 个动态体及四组孤立对子各一个动态体。全自由 root–pelvis 约束
不贡献惯量调节连接臂。

每份记录包含实际 cooked geometry 的 actor-local AABB、质心/主惯量坐标系、
COM-local 碰撞半尺寸、所有有效连接点、最终 extents、内部原始逆质量/逆惯量、
实际调节比例和调用原生 `CalculateParticleInertiaConditioning` 的结果。
这里 AABB 是资产几何输入，运行时不读取导出的 `actualScale` 当作校正表。
当质量、惯量或 PhysicsAsset 身份不匹配时，编译器拒绝绑定。

算法对齐 UE `Chaos/Private/Chaos/MassConditioning.cpp`：

1. 将 actor AABB 变换到质心主轴坐标，取半尺寸。质心平移在尺寸相减中抵消，
   不能误写成“质心到最远边界”的距离。
2. 对当前实际绑定、线性运动并非三轴全自由的关节，在 COM 坐标求连接臂绝对值，逐分量取最大。
3. 使用原生 float 精度、质量/逆惯量/尺寸容差和全局设置，计算旋转修正相对平移修正
   的比例，得到逆惯量缩放。当前原生设置 Distance=20 cm、RotationRatio=2.5、
   MaxInvInertiaComponentRatio=0。
4. 到 Godot 边界才换算单位：有效惯量为 `原始 kg·cm² × 0.0001 / inverseScale`。
   保持刚体质量和主轴坐标不变，Jolt 直接状态回读验证实际世界逆惯量张量。

完整拓扑和孤立对子重算结果均对原生通过：extents 容差 1e-4 cm，scale 向量距离
容差 1e-5。选中的 spine_02/neck_01 在这些资产中受碰撞尺寸主导，完整/孤立比例
恰好相同，不能编造它们存在拓扑数值差异。另以测试中移动连接点 100 cm 的方式，
验证编译器真的从连接臂重算，而非直接返回完整骨架的比例表；该测试不称为原生轨迹。

关节 owner 绑定时设置刚体有效惯量，释放关节后恢复绑定前惯量；重复释放安全。
十秒单关节/整链成功路径检查恢复行为。独立刚体 owner 的原始质量运输策略未修改。
本批实现的是刚体层调节，上批的关节局部质量/惯量调节仍未接入求解器。

## 运行结果与未通过项

Godot 4.7.2 Mono `ed1daf0bf`，启用 ALS 速度驱动，40 刚体/36 关节。
十秒整链门槛仍为全程锚点分离 <0.2 m，最后整整一秒最大超限 <0.1 rad，
接触落地额外要求最后一秒最大线速度 <0.2 m/s。无接触自由落体不要求静止。

| 工况 | Hz | 全程最大锚点分离 m | 末秒最大线速度 m/s | 末秒最大超限 rad | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 普通落地 | 30 | 0.137046 | 0.102415 | 0.253243 | 超限失败 |
| 普通落地 | 60 | 0.086029 | 0.394616 | 0.091557 | 速度失败 |
| 普通落地 | 120 | 0.021784 | 0.117350 | 0.023709 | 通过现有门槛 |
| 无接触自由落体 | 30 | 0.030100 | 不作门槛 | 0.017455 | 通过现有门槛 |
| 无接触自由落体 | 60 | 0.017238 | 不作门槛 | 0.000192 | 通过现有门槛 |
| 无接触自由落体 | 120 | 0.010551 | 不作门槛 | 0.000234 | 通过现有门槛 |
| 十米高速倾斜落地 | 30 | 0.292205 | 未到末秒 | 未到末秒 | 第 21 帧锚点失败 |
| 十米高速倾斜落地 | 60 | 0.099679 | 0.443016 | 0.106010 | 速度/超限失败 |
| 十米高速倾斜落地 | 120 | 0.084176 | 1.279229 | 0.104081 | 速度/超限失败 |

相对上批，30 Hz 无接触末秒超限从 0.186646 降到 0.017455 rad；120 Hz 普通落地
末秒速度从 0.597169 降到 0.117350 m/s。另一方面，60 Hz 普通落地从通过变成失败，
120 Hz 高速落地速度也变差。不能把本批说成全面稳定性修复。
`--raw-inertia` 的 60 Hz 对照再次得到上批 0.181105 m/s、0.043088 rad 的通过结果，
证明新旧差异来自惯量路径；不通过删掉原生惯量调节来维持单项绿色结果。
普通入口行为不受此实验链路变化影响。

新增 `--computed-body-conditioning` 回放 144 组原生轨迹，每组断言从几何/拓扑重算的
惯量与此前实际原生记录匹配，并回读 Jolt 惯量张量。全部退出 0；最大旋转偏差
仍为 0.403482 rad，最大位置偏差 11.692 mm，与上批直接注入原生比例的诊断一致。
这证明已用计算替换比例注入，但没有证明后端运动等价；输出保持 `parity_asserted=false`。

18 次十秒单关节测试（30/60/120 Hz × 三轴 × ±0.6 rad，每次两套资产）全部退出 0，
末秒线速度和角度超限均为 0，同时覆盖后端惯量回读与释放恢复。
单关节静止不替代整链落地、动态驱动误差、Get-up 或人工动画观感验收。

## 构建和回归记录

- 优化 Godot 构建通过，0 warning / 0 error。
- Core Release 2609 通过、0 失败，按项目规则排除两组 P5A 外部 trace 测试。
- 新增 Import 惯量测试 4 通过，包含错误单位、资产及质量拒绝、完整/对子原生对照、
  修改连接臂后重算。新增 Core 测试 4 通过，覆盖尺度、主轴、容差/禁用、零分配。
- Import Release 全库复跑：2332 通过、0 失败、1 个既有条件跳过。首次全库为
  2331 通过、1 失败、1 跳过，失败是原有
  `RepeatedCreateViewsAllocateZeroBytesAndKeepStableHeadersAndSpans` 测到 7680 字节；
  该项独立重跑通过，随后不改代码的全库复跑通过。本批未改其运行时路径或断言，
  波动原因尚未定位，保留首次失败和两次复验记录，不将首次记录改成全绿。
- 按 UE 构建诊断技能完成整个 Editor host 构建与插件审计。编译初次因 TObjectPtr
  范围循环不能推导 `auto*` 失败；改为具体指针类型。随后链接错误确定为缺少
  `ChaosCore` 直接模块依赖，补齐依赖后完整构建成功，没有复制 DLL 或修改 BuildId。
- 最终构建指纹 `C5B3833302B10E0A444DF4DDF396DEEEDD3675796B451E90DEF81426A960A45F`，
  BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`；审计日志位于原项目
  `Saved/Logs/PluginBuild/20260920T171011223Z-451daa77e379477f8566d23f02ba2e87-*`。
- 普通 D3D12 Editor 重启加载新版插件，并通过已有 Python 入口导出 144 组轨迹，
  文件哈希与已提交轨迹逐字节一致，日志记录正常关闭、进程终止。该次 GUI 启动
  没有可靠保留进程退出码，不把启动 shell 的退出 0 冒充 Editor 退出码。
  仍存在两条已有 AutomationTest `Condition failed` 及旧地图/材质等警告，
  无本批插件加载错误、Python 异常或设备丢失。
- DataValidation 退出 0，0 error / 3 warning：旧 PawnActionsComponent、旧 Navmesh
  与对应加载警告。本批没有保存 UE 资产、打包游戏或运行最终十分钟性能预算。

本地日志/TRX 位于 `artifacts/physics-inertia-20260921/`：`native-export.log`、
`native-repeat.*`、`editor-restart.log`、`data-validation.log`、`core-release.trx`、
`inertia-import-final.trx`、`import-release.trx`、`allocation-isolated.trx`、
`import-release-repeat.trx`、`final-computed-replay.*`、`chain-*.log`、`flight-*.log`、
`high-*.log`、`raw-chain-60.log`、`pair-*.log`。失败日志保留。

## 复现与下一步

```powershell
Set-Location D:/GodotALS
$godotAlsEngine = 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives --no-contact
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=120 --als-drives
# 仍失败的接触工况及旧原始惯量对照
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives --raw-inertia
# 输出必须为新文件
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_reference_replay.tscn -- --computed-body-conditioning --report=D:/GodotALS/artifacts/computed-inertia-new.json
& 'D:/UnrealEngine/Engine/Binaries/Win64/UnrealEditor-Cmd.exe' 'D:/AdvancedLocomotionSystemV/AdvancedLocomotionSystemV.uproject' -run=AlsGodotExport -PhysicsInertiaOutput=D:/GodotALS/artifacts/native-inertia-new.json -unattended -NullRHI -nosplash -nop4
```

下一步处理关节局部质量/惯量、独立单边软限制/驱动行、预测积分及投影与接触耦合。
当前 Jolt 加权目标合并仍不是 Chaos 独立约束行，剩余门槛失败不能靠任意提高阻尼或
放宽阈值解决。完整整链稳定后继续角色姿态交接、Ragdoll/Get-up/Pose Recovery；
Mantle、完整 ALS Camera、最终十分钟性能预算均保留在总清单中。
