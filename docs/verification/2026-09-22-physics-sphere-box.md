# Sphere–Box 原生完整接触与 Godot 接入

本批直接在 `${env:GODOT_ALS_ROOT}` / `main` 实现。范围是显式原生 sphere/box 的完整离散几何接触，不是完整物理轨迹等价，也未将实验后端接入普通角色。

## 实现及原生依据

- Core `AlsSphereBoxManifold` 移植 `ContactPointsMiscShapes.cpp::SphereBoxContactPoint`、`CollisionOneShotManifoldsMiscShapes.cpp::ConstructSphereBoxOneShotManifold` 与 `AABB.h::PhiWithNormal`。覆盖面、边、角、内部穿透，保留原生内部最大轴平局规则、近表面 `SafeNormalize` 的 squared-length 阈值及 +X 回退。box polygon margin 不参与这条路径。
- 球心采用原始 float 几何，变换和接触点计算采用 double，最后才存 float。先比较 **double Phi < cull**，不能先转 float。初次对照的 6382/6718 两例证明：double 尚小于 3，保存后的 float 已等于 3，提前转换会错误丢弃接触。
- 初次接触点实现误将 `radius * worldNormal` 降为 float，造成最大 1.65e-6 cm 点差；修为 double 后所有样本精确一致。未扩大测试容差，最终测试断言点、法向、Phi 差全部为 0。
- Godot 仅在显式 `NativeSphereRadius` / `NativeHalf` 同时绑定时调用该算法，球心取 observed proxy center、姿态仍为原始 leaf。支持反序返回、原始局部点和法向；检测距离使用既有 particle bounds / PreV。二次曲面每步重新查询，不启用 polygon manifold restore。旧未绑定诊断路径保留。
- UE `PhysicsSphereBoxOutput=` 使用真正 `UpdateConstraint(SphereBox)` 创建初次流形，源码同步主仓库与 UE 插件。

## 验证

原生参考 `assets/config/v4_physics_sphere_box_reference.json`：8,064 例，6,516 有接触；3 种共同姿态/大平移、2 种局部中心、2 种半径、2 种 box margin、2 种 cull、168 种位置。点数、点、法向、Phi 全部精确一致。重复冷导出字节一致：

`SHA256 536DB587DA24B3A46EF63AA82DA46852E26F45E5D25B4882AE280AC3B3689444`

- Core Release 固定 JIT、串行：2,826 通过；按既定命令排除两类旧 P5A 测试。新增退化/严格 cull、非法输入、热路径零分配检查。
- Import Release 固定 JIT、串行全量：2,414 通过、1 项既有条件跳过，退出 0。
- Godot 优化构建：0 错误、0 警告。
- 30/60/120 Hz 接触 smoke：每种 30 项新增 sphere-box 检查通过，包括面/边/角/内部/分离、原始中心、共同旋转大平移、反序、严格距离等号、逐帧查询且无 Jolt 回退。旧 551 精度、9 几何、68 polygon、10 capsule cull、2 primitive、3 transaction、5 manifold、5 sleep 检查仍通过。
- 整链仍 **6/12**；六个成功 JSON 与上一批字节一致。通过：高速 30、普通 120、平移 60/120、旋转 60/120。失败：普通 30/60、高速 60/120、平移 30、旋转 30。没有关闭这六项，也没有新增失败。普通 60/高速 60 休眠太迟，最后一秒速度指标不是最终已睡时的瞬时速度。
- UE 全 Editor target 构建及插件审计通过，fingerprint `2A0A02B09DFFDFD9D0A72A25DEE49F5F2AB194B981FE2D0287573E522E45A9C3`。首轮误用全局 FSphere 的编译失败日志保留，显式 `Chaos::FSphere` 后完整重建通过。冷导出、重导出均退出 0；DataValidation 退出 0，0 errors / 3 既有 warnings。
- 普通 Editor PID 33988 成功打印 exporter class 加载标记，但原生退出码 **3221225477 / 0xC0000005**；两条旧 `Condition failed` 仍在。本批普通重启门禁失败，不能称 UE 全门禁通过。退出后 exporter DLL 无占用。

产物、首轮失败、修正对照、smoke、矩阵、UE 日志：`artifacts/physics-sphere-box-20260922/`。用户原有 P4 规划修改保留，SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 剩余工作

继续 capsule 边缘/深穿透及混合形状的完整窄相和实际 pair 对照；旧 capsule trace exporter 尚不支持新 `native_capsule` / `native_sphere`，必须显式适配原始 leaf 几何再重放。随后排查六项整链失败及 history/tolerance/退役、CCD/MACD 缺口，再接普通 Ragdoll / Get-up / Pose Recovery。Mantle、完整 Camera 与最终十分钟性能预算仍属于总目标，未以本批独立几何通过代替这些验收。
