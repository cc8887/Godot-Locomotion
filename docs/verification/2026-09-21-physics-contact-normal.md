# 接触法向、相对坐标与整链睡眠验证

主目录 `.` / `main`，接续睡眠提交 `bac6d25`。
修复了两个 Godot 几何适配错误，并校正 Core 验收场景对锁定轴的测量。
两模型普通/高速落地在 30/60/120 Hz 的六项睡眠检查通过；关闭睡眠的六项粗略落地检查也通过。
这仍是独立诊断场景，不是普通角色 Ragdoll、完整 Chaos 世界等价或视觉验收。

## 1. 不能用每个接触点对的差决定法向

旧适配器取 `normalize(point1 - point0)`。近乎水平的盒子与地面接触时，
Jolt 同一个流形中会保留少量尚有间隙的点，因此这个向量的方向可能反转。
真实 Godot 复现：倾斜 0.0005 rad、中心高度 49.99 cm 的 1 m 盒子，
四点中第二点被旧适配器算成 native `(0,0,-1)`，第一点却是 `(0,0,1)`。
`red.log` 保存了修改前的失败，未把它当成正常浮点误差忽略。

核对本机 Godot 4.7.2 Mono `ed1daf0bf` 的
[Jolt 查询实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/jolt_physics/spaces/jolt_physics_direct_space_state_3d.cpp)：
`collide_shape` 返回流形点对；`rest_info` 返回独立的负 penetration axis。
官方 [Jolt 流形构造](https://github.com/jrouwe/JoltPhysics/blob/master/Jolt/Physics/Collision/ManifoldBetweenTwoFaces.cpp)
也明确保留在 manifold tolerance 内的分离点。

现在每个非空凸形状对用 `GetRestInfo` 读取统一几何法向，并核验 target RID/shape index。
适用前提严格保留：查询空间只有一个目标形状，双方只能是球、盒、胶囊或凸包；
不能直接把这个做法推广到复合或凹网格的多个命中。
接触点未重新投影、未丢弃分离点、未改 Core 摩擦/约束公式。
回归还要求同一流形确实同时含正、负有符号间隙，避免用全穿透用例掩盖错误。

每个有接触的形状对增加一次查询和 dictionary wrapper；NarrowPhaseQueries 现同时计数两次查询。
这是正确性修复，不声明主线程分配或窄相成本已达到最终性能预算。

## 2. 转 float 之前先减去共同原点

旧实现直接把两形状世界坐标转为 Godot float。
仅将同一接触整体平移 `(1e7,-2e7,3e7)` cm，接触点数量就改变；
修改前失败记录在 `translation-red.log`。

现在先在 Core double 坐标中减去 shape0 的世界位置，再转换到 Godot。
双方只移除相同平移量，保留原来的世界轴与旋转。
输出接触点直接从这个相对坐标转换到各形状局部坐标，不绕经大世界 float 坐标。
这是几何查询的局部原点处理，不会改变角色身体的 Core 世界位置。

Godot 新增 17 项几何精度检查：上述混合间隙回归，以及四类形状 × 两个坡度 × 两种端点顺序。
每种都比较原点附近与大平移后的完整流形；点数相同、局部点一对一匹配误差 <0.002 cm、
法向误差 <0.0001，朝向正确。30/60/120 Hz 全部通过。
旧六项几何/主线程检查、三场景动态响应和五项睡眠生命周期也全过，总动量误差 0。

## 3. 锁定轴与有限角度轴使用不同的误差定义

仅完成法向/相对坐标修复时，普通 60 Hz 两角色已经入睡，但旧角度门槛失败：
AnimMan `neck_01`、Swing2 锁定轴被记录为 0.1055354318 rad。
旧验收对所有轴一律检查分解后的 twist/pyramid 角。

本机 UE 5.9 `Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp` 的
`InitRotationConstraints` / `InitLockedRotationConstraints`（约 1097/1459 行）
实际对锁定轴约束相对四元数 `R01` 的 X/Y/Z 分量；有限 swing 轴才使用 pyramid 角。
当前 Core 求解器早已按此求解，本批没有改求解行或调参。
混合 twist/swing 下，即使 `R01.Y==0`，分解后的 pyramid Y 仍可非零。

新增 `RotationLockResidualAngles` 报告距离各自 `R01[i]==0` 约束集合的最短旋转角：
`2*asin(abs(R01[i]))`。这是独立残差，不是 Euler 角或三轴合并距离。
单轴偏转时与原有角度一致；复合旋转、整体坐标旋转和四元数取反由新增四项 Core 用例覆盖。
有限角度轴的计算不变，最后一秒 `<0.1 rad` 的门槛也不变。

报告保留 `legacy_final_pyramid_limit_rad`、新的 metric 说明以及最大误差对应的骨骼/轴/帧。
普通 60 Hz 新定义下最大残差为 0.0190218792 rad，来自颈部另一条 Limited 轴。
旧的 0.1055354318 rad 仍可在最终 JSON 中查看。
本修正仅适用 Core 原生缓存行的验证，不据此重判旧 Jolt 后端的历史失败。

## 4. 最终整链结果

普通模式保持原先 100 cm 世界偏移；高速模式为 300 cm 偏移和 -1000 cm/s 初速。
仍是两套真实资产共 40 个身体、36 个关节，加两地面，45 个几何形状。
每次模拟十秒，要求两角色按原生材质阈值自然入睡，各保持至少一秒，
睡眠期间 pose/velocity/contact epoch 必须逐帧不变。
睡眠阈值、平滑参数与求解迭代次数没有改变，也没有新增强制睡眠超时。

| 模式 | Hz | AnimMan 入睡帧 | Mannequin 入睡帧 | 最大锚点误差 cm | 末秒最大约束残差 rad |
| --- | --- | --- | --- | --- | --- |
| 普通 | 30 | 62 | 244 | 1.48463549 | 0.04858358 |
| 普通 | 60 | 243 | 346 | 0.95723551 | 0.01902188 |
| 普通 | 120 | 554 | 899 | 0.37676500 | 0.00623875 |
| 高速 | 30 | 101 | 173 | 4.84066519 | 0.06901265 |
| 高速 | 60 | 323 | 171 | 1.73960296 | 0.01253574 |
| 高速 | 120 | 276 | 453 | 0.91490611 | 0.00637407 |

六项均退出 0，末秒线/角速度均为 0。成功报告为 `verified-{normal,high}-{30,60,120}.json/log`。
关闭睡眠的对应六项也通过（`awake-*`），末秒最大线速度分别为
普通 6.2672 / 8.3391 / 3.0988 cm/s，高速 7.1151 / 1.9779 / 9.2495 cm/s。
无睡眠时仍有残余运动；这些结果只满足既有 `<20 cm/s` 的粗略检查，不表示完全静止。

Core Release 固定 JIT 全量 **2693 通过**，保留既有 P5A Golden/TraceSchema 过滤；
Import **2363 通过 / 1 既有条件跳过**。Godot 优化构建 0 warning / 0 error。
原 144 组 ×12 步无接触关节回放通过，最大位置差 1.020660e-6 cm、角度差 3.576279e-7 rad，和前批一致。
本批未更改 UE exporter 或重新导出资产。

所有产物：`artifacts/physics-contact-normal-20260921/`。
修改前两个红测、仅法向修复的 `normal-*`、重定位后的 `rebased-*`、
旧测量失败的 `final-sleep-60.log` 均保留；最终以 `verified-*`、`awake-*`、
`final-contact-*`、`final-pairs.*`、`core-full.log/core.trx`、`import-full.log/import.trx` 为准。
通过 agent-reach 的 GitHub 路由核对源码；gh 未登录后用只读官方仓库页面完成核对。

## 后续边界

本批关闭了 Core 诊断整链的三频率睡眠失败，不关闭旧 Jolt 后端失败。
普通 demo 未切换，也没有多帧画面或人工观感验收。
仍需补完整原生整链接触/重力对照、运动 kinematic 与真实场景几何接入、
角色触发/物理姿态回写/胶囊跟随，再完成 Ragdoll、Get-up、Pose Recovery。
CCD、岛发现/合并/分裂、完整材质/碰撞通道及主线程查询成本仍有缺项。
Mantle、完整 ALS Camera 与最终十分钟性能预算继续保留在总目标中。
