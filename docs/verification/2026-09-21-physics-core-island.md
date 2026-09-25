# Core 共享刚体求解与 Godot 无接触接入

本批在 `${env:GODOT_ALS_ROOT}` 的 `main` 将上一批独立关节步接成共享刚体状态的求解组，
并通过 Godot 真实物理回调、导入的碰撞形状和骨架姿态传输进行验证。
普通入口仍使用原有实现；这里没有完成接触世界，也不代表 Ragdoll 已可交付。

## 实现与所有权

`AlsJointIsland` 预分配每个刚体唯一的预测姿态、DP/DQ 和速度，以及关节缓存。
每一步先预测所有刚体并缓存所有关节，再以“迭代 → 全部关节”的顺序执行位置阶段；
统一补隐式速度后执行速度阶段。所有位置修正提交后，先缓存全部投影，再逐关节投影，
最后统一发布 actor 状态。后一个关节可以看到前一个关节对共享刚体的修正。
固定刚体使用 actor 坐标，动态刚体使用质心坐标；保留原生 float particle 存储边界。
初始输入和拓扑复制归求解组所有，重置先验证全部状态，失败不发布部分结果。

这不是物理岛发现算法：当前由调用方提供完整身体列表与约束顺序。每个求解组只有一个
写入者，不同组可以在独立 worker 上计算；同组不能并发调用。`StepForceFree` 明确限定
无重力、无接触、保持唤醒；移动 kinematic 不在此接口中，也不拿零质量静态体冒充。

`AlsCachedJointSettingsCompiler` 解码原生设置，拒绝未实现的 SLERP、非零驱动速度目标、
有限扭矩上限、线性驱动、非锁定线性轴、shock propagation、非单位 parent mass scale、
restitution、solver override 和不同阶段顺序。投影当前只接受已验证的 SIMD 线性路径。
288 组逐阶段原生对照同时验证这个解码器，144 组连续轨迹测试改用实际求解组执行。

Godot 的 `AlsForceFreeJointHost` 使用冻结的 `AlsPhysicsBodySet` 作为姿态代理。
Core 拥有积分与约束状态；代理必须为 Static freeze、collision layer/mask 均为 0，
不能同时启动旧 Jolt body owner。每帧只向 Godot 写出质心变换，不把 float 场景变换再
读回 Core 累积误差。测试检查下一次物理回调里的 server transform、节点变换和骨架回读。
故意打开代理碰撞 mask 时，入口必须在改变 Core 状态前拒绝。

这是已批准 C# Core 架构内的接入，未修改 Godot 引擎，也未增加 UE 运行时依赖。
后续接触行必须与关节共享这些修正量；不能让 Jolt 完成求解后再覆盖动态身体姿态来冒充共同求解。

## 验证结果

环境：Godot 4.7.2 Mono `ed1daf0bf`，Jolt Physics；Godot 项目优化构建 0 warning / 0 error。
全量 Release Core **2638 通过**，沿用排除既有 P5A Golden/TraceSchema 的过滤器；
Import **2356 通过 / 1 既有条件跳过**。测试子进程关闭 tiered compilation，不修改用户环境。
最后增加的 scalar projection 契约拒绝在七项 Core focused 回归中再次通过。

七项 Core 测试覆盖分支共享修正、跨关节迭代顺序、未调节质量时的线动量守恒、非法输入
不发布状态、重置精确重放及输入数组隔离、十个独立组并行/串行结果一致、2048 步零托管分配。
Import 增加十九项设置拒绝与坐标边界测试。原 288 组关节阶段、516 组角行、262 组投影
和 144 组连续轨迹均包含在本批回归中。

`physics_core_joint_replay.tscn` 的默认模式在真实 Godot 物理回调中执行 144 组 × 12 帧，
只从 sample 0 初始化；后续样本只用于断言。源自两模型、两个关节、三个频率、三个旋转轴、
正负扰动、普通及高速驱动。使用导出的 conditioned inertia 作为孤立对子输入；不是完整链的惯量计算证明。

| Godot 中的原生对子对照 | 最大误差 | 门槛 |
| --- | ---: | ---: |
| actor 位置，cm | 1.020660e-6 | 2e-5 |
| actor 旋转，rad | 3.576279e-7 | 1e-6 |
| 线速度，cm/s | 4.722374e-5 | 1e-4 |
| 角速度，rad/s | 8.397188e-6 | 2e-5 |
| 骨架位置传输，m | 3.586463e-7 | 5e-5 |

Godot host 使用导入的资产 mass-local，纯 Import 测试使用对子导出的 mass-local；
二者浮点边界略有差别，仍使用原门槛，未放宽误差，也未用下一帧 golden 回灌求解。

`--chains` 模式同时运行 Mannequin 与 AnimMan：**40 刚体、36 个非自由关节**，
全链惯量从实际几何与 connector 拓扑计算，自由 root 不绑定。
初始线速度 200 cm/s、角速度 `(0.3,0.7,-0.2)` rad/s，spine_02 另加 0.6 rad 扰动；
30/60/120 Hz 各连续运行十秒。检查有限姿态、骨架位置与旋转回读、代理未被 Jolt 移动，
并要求第一步以后锚点距离小于 10 cm。

| 频率 | 每模型步数 | 最大锚点距离，cm | 最大骨架位置传输误差，m |
| --- | ---: | ---: | ---: |
| 30 Hz | 300 | 0.279536733 | 1.528373e-5 |
| 60 Hz | 600 | 0.270646058 | 1.157379e-5 |
| 120 Hz | 1200 | 0.250295155 | 1.149036e-5 |

这些是无接触整链稳定性和传输检查，**没有完整链 UE golden**，不声明整链原生等价，
不把未比较的误差记成零；JSON 相应字段为 null，`full_chain_native_parity_asserted=false`。
十秒、两个模型不等于目标十分钟、十角色性能验收，也没有进行画面观感验收。

## 发现和修复

首次完整链启动失败：`GetRefFrame` 的 float 轴重建带有约 1e-8 的 scale 舍入，
被 Core 的严格 rigid pose 校验拒绝。对照本地 UE 5.9 源码：

- `Chaos/Public/Chaos/ParticleHandle.h` 的 `GetComRelativeTransform` 重建位置/旋转；
- `Chaos/Private/Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp` 的 `InitDerivedState`
  只读取 connector translation/rotation。

新增 `RigidConnector` 在导入到求解器的边界去掉经过 1e-5 校验的舍入 scale，保持原位置和
四元数，拒绝实际缩放；原始资产 JSON 未修改。失败日志保留为 `chains-60.log`。

144 组第二次回放退出时曾报告 190 个 Jolt shape RID 残留，保留在 `native-pairs-final.log`。
原因是身体节点释放后，C# 新建 Shape/PhysicsMaterial 的 wrapper 仍依赖 GC 释放持有的引用。
`AlsPhysicsBodySet` 现在记录自己创建的资源，在释放身体节点后显式 Dispose，并覆盖部分构建
失败与重复 Dispose。未释放外部加载或共享的资产资源。
修复后重新执行对子回放、60 Hz 完整链和原身体生命周期场景，均退出 0，退出日志无
ERROR / FAILED / leaked 标记；数值结果不变。最终对子日志为 `native-pairs-ownership.log`。

原身体生命周期探针覆盖 40 身体 / 43 形状的 seed、flight、suspend、resume、stop、dispose；
几何误差 2.793250e-5 m，姿态误差 7.582022e-7 m，惯量相对误差 1.159215e-6，退出 0。
它保护现有 Jolt 身体生命周期，不构成接触或新 Core 关节的额外等价证明。

全部日志、TRX、各频率 JSON 在 `artifacts/physics-core-island-20260921/`。
主场景入口和资产未换版；本批没有修改或启动 UE exporter，沿用已验证的导出数据。

## 后续顺序

1. 接触输入与行求解：明确 body/shape 身份、碰撞过滤、接触流形和持续接触缓存；
   用 UE 原生接触阶段验证法向、摩擦、恢复系数，然后与关节在同一物理步共同迭代。
2. 加入重力/外力、运动 kinematic、动态物体的双向响应、连续碰撞与物理岛发现。
   查询只获得几何信息，不能把其他动态物体默认当无限质量静态物体。
3. 按 UE 的真实物理材料与岛状态实现睡眠/唤醒；恢复原带睡眠的 144 组轨迹验收。
4. 重新执行完整落地/高速/多频率回归后，才接普通角色 Ragdoll、Get-up 与 Pose Recovery。
   Mantle、完整 camera、十分钟性能验收及其他总清单未完成项继续保留。

旧 Jolt 九项回归的失败与完整链观感问题未被本批关闭。普通入口没有切换到无接触测试后端。
