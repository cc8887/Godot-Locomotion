# 完整胶囊—盒体接触接入

本批在主目录 `${env:GODOT_ALS_ROOT}` / `main` 补齐原来只覆盖 face interior 的胶囊—盒体窄相。实验整链由 **6/12 提升至 7/12**，没有调整验收门槛。普通角色仍未接入实验后端，总目标尚未完成。

## 实现与原生依据

`AlsCapsuleConvexManifold` 对应 UE `CollisionOneShotManifolds.cpp::ConstructCapsuleConvexOneShotManifold`：

1. 原始 float capsule endpoint/axis/height 提升到 double，按原生相对姿态变换到盒体局部空间。
2. 盒体先、轴线后，以零 support margin 执行每次冷启动的 same-space GJK/EPA，再补回胶囊半径。复用现有已验证 GJK/EPA，identity 相对姿态保持同空间；复用 workspace，但不继承上次 simplex。
3. 选择原生盒面；轴线与接触法向点积阈值 `.01` 区分 cylinder/end cap。按面顶点绕序裁剪轴线，保留 `.01` plane tolerance 与原始端点顺序。
4. Cylinder 路径沿圆柱法向投影；end cap + plane 且倾斜未超过 `.707` 时加裁剪的圆柱点，再按原生 `.1f * radius` 距离排重，最后加入 GJK 点。完整覆盖边缘、内部穿透、裁剪为空后的 GJK 回退；记录原生 Phi。

新增 `AlsGjkSegmentShape` 保留 dot>=0 选第二端点的规则。首轮零分配检查发现默认接口方法导致每次查询装箱；添加显式值类型方法后热路径零分配通过。

Godot 仅对同时显式绑定 `NativeCapsule` / `NativeHalf` 的 pair 使用完整路径，并处理反序局部点与法向。共用真实 bounds / PreV 检测距离，每步新查，不误用 polygon manifold restore。未绑定的旧诊断 helper 保留；capsule–convex、capsule–capsule 等混合对仍未接管，不能把泛型 Core 接口视为这些形状已经完成原生验证。

## 原生参考与测试

- 旧 `v4_physics_capsule_geometry_reference.json` 的 **744 例全部验证**，包括旧 guarded helper 排除的 300 例，不再跳过。点数、点序、两侧点、法向和 Phi 全部精确相同。
- 新 `PhysicsCapsuleBoxOutput=` 经真实 `UpdateConstraint(CapsuleBox)` 导出 **8640 例**：3 种姿态/大平移 × 2 局部中心 × 2 半径 × 2 高度 × 2 cull × 6 轴线倾角 × 30 位置。覆盖面/边/角/内部、短长轴、原始局部轴和分离。0/1/2/3 个点分别 484/966/5196/1994 例；点数与有序几何、Phi 精确一致。
- 两套共 **9384 例**，比较容差最终为零，未按失败例放宽。新参考冷重导字节一致，SHA256 `B2B64C4CC2CA4D81E4816418F7258740047ACC390E90DAECA124EE115B29A96A`。
- Core Release 固定 JIT 串行 **2829 通过**，沿用两类旧 P5A 排除。新增深穿透、边缘、冷启动历史独立、无效输入不发布、零分配测试。
- Import Release 固定 JIT 串行全量 **2416 通过、1 既有条件跳过**，退出 0。
- Godot 优化构建 0 warnings / 0 errors；30/60/120 Hz smoke 各 **24 项新增完整 capsule-box 检查通过**（反序/原始 leaf/旋转大平移/边角内部/分离/逐帧查询且无 Jolt）。旧 551 精度、9 几何、68 polygon、10 capsule cull、2 primitive、3 transaction、5 manifold、5 sleep、30 sphere-box 保留通过。

## 整链结果

| 场景 | 30 Hz | 60 Hz | 120 Hz |
| --- | --- | --- | --- |
| 普通落地 | 失败 | 失败 | 通过 |
| 高速落地 | 通过 | **恢复通过** | 失败 |
| 平移平台 | 失败 | 通过 | 通过 |
| 旋转平台 | 失败 | 通过 | 通过 |

高速 60 Hz 两模型分别第 161/289 帧休眠，末秒线/角速度均为 0；最大 anchor 1.798296 cm，末秒限位残差 .015029575 rad。普通 60 Hz Mannequin 第 574 帧才睡，未满足保持一秒；普通 30 Hz Mannequin 未睡，高速 120 Hz AnimMan 未睡；两个 30 Hz 平台均在启动前未睡。未把末秒最大速度误称为最终睡眠状态速度，未放宽时间或阈值。本批消除一项旧失败，仍有五项，不宣称全链 UE 轨迹等价。

## UE 门禁与工作区

完整 Editor target 构建及全插件审计通过，fingerprint `AED66EC0BAE68CD2850960BA87F34E295C263CBD80CFF53B30706D7C7FE92A94`；源码镜像逐文件 hash 一致。导出/重导退出 0；DataValidation 退出 0，0 errors / 3 旧 warnings。

普通 Editor PID 34476 打印 exporter 加载标记，但原生退出 **0xC0000005**，两旧 `Condition failed` 仍在；普通重启门禁失败，不能称所有 UE 门禁通过。退出后 exporter DLL 无占用。日志及失败证据保存于 `artifacts/physics-full-capsule-box-20260922/`。

用户未提交 P4 规划修改保留，hash 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。下一步是其余混合形状、真实 native_capsule trace 对应原生形状身份的重放及五项稳定性问题；旧 exporter 尚未支持此 trace 类型，不能用中心 Z 代理替代。之后继续普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 与十分钟预算，不以单项几何对照替代角色验收。
