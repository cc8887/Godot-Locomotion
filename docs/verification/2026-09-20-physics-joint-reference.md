# 原生关节有效配置与角度数学层

本批在 `D:\GodotALS` / `main`，基于 `5cd9d99` 继续 Ragdoll 关节前置工作。
没有新建项目副本。用户的 P4 计划修改未纳入本批。

## 修正实际求解器分支

上批仅对照非线性 Chaos solver 的椭圆锥分支，错误推断了当前资产的实现要求。
本批继续跟踪 `ChaosEngineInterface::SetUseLinearJointSolver_AssumesLocked`、
`PBDRigidsEvolutionGBF::CreateJointConstraint`，再读取真实物理场景中的
`FJointConstraint::GetJointSettings()`：两套资产的 38 个关节全部
`bUseLinearSolver=true`。

当前 UE 5.9 的 `PBDJointCachedSolverGaussSeidel.cpp` 中
`InitRotationConstraints` 对两个 Limited swing 选择 `InitPyramidSwingConstraint`。
椭圆锥属于另一条非线性路径。这修正了上份报告的判断；Godot Jolt 的 Pyramid
类型仍不足以证明软限制、驱动、质量调节和投影与 Chaos 等价。

## 新增实现

- `ExportPhysicsJointReference` 在独立、未步进模拟的 GamePreview world 中，
  为 Mannequin / AnimMan 创建实际 SkeletalMesh 物理状态，读取 18 / 20 个
  live joint settings；不保存任何 UE 资产。
- `assets/config/v4_physics_joint_reference.json` 保存有效参数和原生数学参考。
  schema 1，原生厘米/千克/弧度，轴顺序 X=Twist、Y=Swing2、Z=Swing1。
  文件 1,845,545 字节，SHA256：
  `3846F07CBA9A7A8F71FC04473787C54546156EEF4F7F2059DB99928A2954CB08`。
- `AlsPhysicsJointCompiler` 按 mesh、PhysicsAsset、JointName、child/parent
  绑定到已有刚体定义，不依赖 JSON 行顺序。类型化保留 motion、角度、软限制、
  驱动、投影、质量调节和碰撞开关；其余已导出字段保留在 NativeSettings。
  非线性求解器、未知枚举、无效参数、缺失或重复绑定显式失败，不自动降级。
- `AlsJointAngularKinematics` 提供双精度 swing/twist 分解、角度、Pyramid
  轴、locked rotation Jacobian 轴及原生 TwistAndSwing drive error。
  保留 UE 的小角度误差近似、最短四元数弧、半周退化分支和 float UE_PI 常量语义。
  有效输入路径零托管分配，不接触 Godot 对象。

参数对照揭示资产面板值不能直接用作物理 API 输入，例如 spine_02：

| 参数 | 资产配置 | live Chaos settings |
| --- | ---: | ---: |
| Swing soft stiffness | 50 | 5,000,000 |
| Swing soft damping | 5 | 5,000 |
| Angular drive stiffness | 50 | 75 |
| Angular drive damping | 1 | 1.5 |
| Swing1 / Swing2 限幅 | 25° / 15° | Z≈0.436332 / Y≈0.261799 rad |

软角限制与角驱动均导出为 Acceleration 模式。上述是进入 Chaos 的数值，
不是已经换算好的 Godot 力矩。Locked / Free 轴可能保留 `float.MaxValue`
或其他不用的限幅值，必须结合 motion 判定。root–pelvis 六轴均 Free，
Mannequin 手部全锁、AnimMan 手部有限角度，不能混用两套资产。

## 验证与范围

完整 UE Editor Target 构建及插件审计通过，状态 fingerprint：
`253E5DAE94C7FEDE8FD434E9C6DE4367A8DEC29C130A0E7B828F3F6EF072E2F6`。
第一次构建遇到 TObjectPtr 的 auto 指针推导失败，改为显式模板类型后修复。
失败构建期间引擎 BuildId 改变，随后构建合同拒绝旧项目 receipt；确认目标为
项目本地生成物后，使用构建包装器隔离旧 receipt/manifests/DLL/PDB 并重新构建，
备份位于 `D:\AdvancedLocomotionSystemV\Saved\BuildReceiptBackup\20260920T151710969Z`。
没有修改 BuildId 或拷贝单个 DLL 绕过审计。

NullRHI 冷启动 commandlet 和正常 D3D12 Editor 的 Python 导出均退出 0，
输出逐字节一致，38 个关节、每个 24 个姿态，共 912 条记录。
24 个姿态在各关节重复，不宣称 912 个独立随机姿态。参考调用原生
`FPBDJointUtilities` 分解/角度/轴函数；Pyramid 轴和 drive error 是原生类型
按 cached solver 表达式计算。没有执行完整 Chaos 约束步进，不能把它称为受力 oracle。
所有已实现数学输出逐分量误差不超过 2e-10，另检查目标旋转、四元数符号、
180° 退化、非法输入和热路径分配。

普通 Editor 启动日志有两条 `LogAutomationTest: Error: Condition failed`，
发生在 Engine 初始化之前、导出脚本执行之前；未据此宣称整份 Editor 日志无错误。
导出与退出正常，数据一致。DataValidation commandlet 单独退出 0，
汇总 0 errors / 3 warnings（现有 PawnActionsComponent 缺失、旧 RecastNavMesh）。
这些原工程告警未通过本次关节移植修改资产去消除。

验证日志与 TRX 位于 `artifacts/physics-joints-20260920/`。

- Core Release：2,596 通过；按项目既有规则排除依赖旧夹具的
  `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests`，未宣称这些被排除项通过。
- Import Release：2,324 通过、1 跳过；跳过项是需显式提供
  `ALS_LAYERING_REPEAT_FILE` 的原有普通 Editor layering repeat 测试。
- 新增关节检查：Core 3 项，Import 21 项，全部通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning / 0 error。

本批未改变 Godot 普通场景接线、碰撞策略或输入，未重跑上一批全部落地/
键鼠 smoke；也没有宣称新的整角色观感或十分钟性能验收。

## 后续工作

本批还没有绑定 Godot 物理关节，没有向刚体应用约束冲量，也未启用普通场景
Ragdoll。下一步需要基于有效设置完成各轴的硬限制/软限制和驱动适配，
单关节受力对照、整套关节链 30/60/120 Hz 稳定性与初始姿势交接；随后接入
Main 的角色物理所有权、动画输出回传、Ragdoll/Get-up/Pose Recovery。

原规划中的 Mantle、完整 ALS Camera 和最终十分钟性能预算仍未完成。
接触稳定性与普通动画运行的上一批验收不能替代本次尚未实现的关节受力验收。
