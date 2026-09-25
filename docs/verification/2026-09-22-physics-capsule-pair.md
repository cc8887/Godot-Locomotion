# 胶囊对原生流形与运行时接入

在 `${env:GODOT_ALS_ROOT}` / `main` 实现 capsule–capsule one-shot manifold；实验整链由 **7/12 提升到 8/12**。普通角色仍未接入实验后端，整体 ALS 目标未完成。

## 原生规则与实现

依据 `CollisionOneShotManifoldsMiscShapes.cpp::ConstructCapsuleCapsuleOneShotManifold`、`UnrealMath.cpp::SegmentDistToSegmentSafe`：

- 先取 double 相对姿态，再转 float pair space；使用原始 float endpoint/axis/height 算球柱中心，不用居中 Godot 代理重建。
- 两轴同向化，安全求线段最近点；最近点必加（Phi<=cull）。轴完全相交时，按较小 **动态** 半径选择 ±Z 推出方向，kinematic/static 用最大 float 参与比较。原生 IsDynamic 包括 Sleeping，Godot 使用固定逆质量归属，不把睡眠变成静态。
- 同向端对端直接返回一个点；否则按照轴线对齐 `.8f` 或深穿透 `.05f` 规则补圆柱支撑点，保留 `.2f` 时间距离、`.35f` 正交阈值、`.25f` 径向限制。先最近点，再小 T、大 T；补点使用严格 Phi<cull。
- 原生编译结果在两个投影限制间共用倒数。首轮直接除法产生最多 4.76837e-7 cm 的差，改成共用 float reciprocal 后点、法向、Phi 精确一致，最终断言容差为零。
- Godot 显式 native_capsule pair 使用该路径，必须 PrepareStep 提供动态归属；共用既有 bounds/PreV 的 cull，保留调用端点顺序。每帧查询，不复用 polygon manifold，不回退 Jolt。旧 proxy fallback smoke 改用一侧未显式绑定的胶囊，继续覆盖原 leaf 坐标转换。

## 原生验证

`PhysicsCapsulePairOutput=` 调用真实 `UpdateConstraint(CapsuleCapsule)`。7,056 例涵盖两种半径组合、短/长轴、动态/运动学归属、局部偏心、共同旋转/大平移、平行/反平行/交叉、深穿透及分离；0/1/2/3 点分别 **3348/2424/630/654**。有序点数、两侧点、法向、Phi 全部精确一致，默认三个 CVar 也由原生观察值断言。

参考 `assets/config/v4_physics_capsule_pair_reference.json` 为 7,744,723 bytes，冷重导字节一致：

`SHA256 C6A94433372442906936F3AE268B4088311DBD52B34B7E6A4C374EAD6C7450F5`

- Core Release 固定 JIT 串行 **2833 通过**，沿用两类旧 P5A 排除。后续补充退化不发布断言，胶囊对定向 4 测试再次通过。
- Import Release 固定 JIT 串行全量 **2417 通过、1 既有条件跳过**，退出0。
- Godot 优化构建通过。30/60/120 Hz smoke 各新增 **20 capsule pair 检查通过**；旧 551 精度、9 几何、68 polygon、10 capsule cull、2 primitive、3 transaction、5 manifold、5 sleep、30 sphere-box、24 full capsule-box 均通过。
- 新测试包括动态归属/端点反序、平行补点、原 leaf 点/Phi、静止 cull/速度扩展、无上下文拒绝、逐帧查询和无 Jolt 回退；Core 覆盖零分配及输出原子性。

## 整链与边界

| 场景 | 30 Hz | 60 Hz | 120 Hz |
| --- | --- | --- | --- |
| 普通落地 | **恢复通过** | 失败 | 通过 |
| 高速落地 | 通过 | 通过 | 失败 |
| 平移平台 | 失败 | 通过 | 通过 |
| 旋转平台 | 失败 | 通过 | 通过 |

普通 30 Hz 两模型第 82/138 帧休眠，最大 anchor 1.746482 cm，末秒限位 .060622395 rad。普通 60 Hz Mannequin 第 596 帧才睡（上批574），保持不足一秒；高速120 AnimMan未睡，Mannequin第850帧睡。30 Hz 平台两项仍未过。没有扩大时长、放宽阈值或按 native awake 驱动结果；8/12 不等于全链原生轨迹等价。

明确保留的退化边界：补点恰落在另一轴上时，原生补点公式含 `delta / distance` 的零除；当前 Core 在发布前抛异常，防止 NaN 进入 solver，不伪造法向。Core 已验证此拒绝不改变 destination，但 **该退化没有专门 UE 输出对照、未修复**，不能声称所有 capsule pair 输入都能稳定执行。现有7,056原生样本和12整链未触发它。后续应独立核对原生行为并解决稳定性，再验收普通角色。

## UE 与工作区

完整 Editor target 构建和全插件审计通过，fingerprint `17238962DAF449EEEA0D7EC5266922EB3C76DEF7EE8D99BA7AE5E0D017235DAD`。首轮误用 external particle 的 `SetObjectState` 编译失败保留；更正为内部 handle 的 `SetObjectStateLowLevel` 后完整重建通过，源码镜像 hash 一致。

冷导/重导/DataValidation均退出0；DataValidation仍0errors/3旧warnings。普通Editor PID2032加载标记成功，原生退出0且DLL无占用；两旧Condition failed仍在，不能据一次正常退出声称间歇AV已修复。全部产物在 `artifacts/physics-capsule-pair-20260922/`。

用户原有P4规划修改未动，hash仍 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。后续继续退化补点、capsule–convex / sphere混合对及实际pair trace重放，排查四项整链失败；再接普通Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera和十分钟预算。
