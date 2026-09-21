# 胶囊与实际脚部凸包：原生对照与实验查询接入

本批直接在 `D:\GodotALS` / `main` 实施，保留用户的P4规划修改。普通角色入口未切换到实验物理后端。

## 原生算法与参考

UE `CollisionResolution.cpp` 的 `CapsuleConvex` 分支通过不解包的 CastHelper 分别进入 raw、instanced、scaled 凸包，再调用 `ConstructCapsuleConvexOneShotManifold`。该算法与已移植的 capsule-box 共用同一模板：凸包在前、轴线段在后的同空间 GJK/EPA，补回胶囊半径，原生选面及轴线裁剪生成最多三点。TGJKShape 使用完整凸包支持点，不使用 polygon-polygon 的外层 margin 缩进；选面搜索距离从0与原生最小距离取大值。

新增 `PhysicsCapsuleConvexOutput=`，直接调用真实 `UpdateConstraint(CapsuleConvex)`，使用 AnimMan 两只脚的原生 cooked 凸包。4,320例覆盖 raw、instanced、实际微小Z缩放、非均匀缩放、镜像缩放，实际外层margin、偏心胶囊、两半径/两长度、三倾角、九位置、0/3cm cull，以及旋转与百万cm平移。

参考点数分布：0点2318例、1点741例、2点1079例、3点182例。Core现有通用实现无需公式修改，所有样本点数、点序、两侧局部位置、法向、存储Phi均精确一致；没有筛除失败场景或放宽误差。

资产 `assets/config/v4_physics_capsule_convex_reference.json` 为4,683,365 bytes。两次冷导退出0且字节一致，SHA256：

`478E25A1C920EA94269CC4EACE86E943079853377AA3EA9A2E94ACA4A21D285F`

## 实验运行时

显式 native capsule 与 cooked foot 绑定现在使用 Core 流形，保留实际leaf坐标/缩放及查询端点反序，沿用实际detector cull。该路径每步重新查询，不恢复polygon流形，也不走Jolt fallback；独立计数 `NativeCapsuleConvexQueries`。

Godot优化构建0错误/0警告。新增smoke在30/60/120Hz各通过48项：两只真实脚、旋转/大平移、双向查询、近接触/远离剔除、世界空间点/法向/Phi一致，以及两步连续Gather/commit不恢复polygon、不回退Jolt。各频率原有全部检查仍通过，包括120步胶囊退化连续求解。

Import Release固定JIT串行全量 **2419通过、1既有跳过、0失败**，退出0，测试耗时4分51秒；包括新增4320原生对照以及旧capsule-box/pair等参考回归。

首轮Godot编译发现测试读取了不存在的 `Margin` 成员，按真实 `AlsRuntimeShape.MarginCm` 修正后构建通过；原日志保留。本批未改Core公式，Core全量沿用上一批2834通过证据，不伪称本批重跑。

## 整链结果与边界

十二项整链仍为 **8/12**，无新增失败、无既有失败关闭。通过项为普通30/120、高速30/60、平移60/120、旋转60/120。普通30接触点累计11219→11220，其余报告字段不变（两角色82/138帧入睡，末速度/角速度0，最大锚点1.746481715cm、末限位0.060622395rad）；另外七个通过报告与上一批字节一致。

普通60、高速120、平移30、旋转30仍未满足整链休眠/稳定性门槛。独立原生窄相参考通过不能证明完整轨迹等价，也不能作为普通Ragdoll完成证据。没有放宽验收阈值或强制定时休眠。

## UE门禁与后续

完整Editor target构建及项目插件审计通过，fingerprint `D8E86A648A0F70E9582E30DDE64690C982A32C1CAFEEB33848BADAE70DDA501E`；导出插件主仓与UE源码镜像hash一致。DataValidation退出0，0errors/3既有warnings。

普通Editor PID33892加载标记成功，原生进程退出0，exporter DLL无占用。两条旧 `Condition failed` 仍存在，不能宣称既往间歇 `0xC0000005` 已修复。项目未发现额外打包构建要求。

日志与报告位于 `artifacts/physics-capsule-convex-20260922/`。后续继续 sphere-capsule/sphere-convex/sphere-sphere 原生混合路径、native primitive实际pair trace重放及四项整链失败，再完成普通 Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera与十分钟性能预算。
