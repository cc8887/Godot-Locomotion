# 120 Hz 分歧窗口：历史、Gather 与共同求解

## 结论和边界

本批直接在主目录 `.` 的 `main` 推进，未改生产物理公式、睡眠门槛或普通 demo 后端。
把上一批已定位的分歧窗口固定为原生回归：在**相同捕获输入**下，历史匹配/锚点回存和 Gather 逐值一致，关键帧共同求解通过既有容差。
这些结果不等于完整世界轨迹一致，也未关闭普通 120 Hz Mannequin 不休眠的问题。最新完整矩阵仍为上一批 **8/12**；本批未重跑矩阵。

## 原始样本与阶段

- 原始捕获：`artifacts/physics-shape-bounds-reference-20260922/window120/`，两模型各 frame 140..159，即完成第 141..160 步，共 40 帧。
- 共同求解选取 Mannequin frame 146..149（完成147..150）和 AnimMan frame 156..159（完成157..160），共8帧；副本在本批 `selected/`，没有创建新项目或工作树。
- 新产物和日志：`artifacts/physics-divergence-window-20260922/`。
- 使用现有 `PhysicsRawGatherInputs/Output`、`PhysicsActualHistoryInputs/Output`、`PhysicsCoupledInputs/Output` 导出入口，无 UE 插件源码改动。
- 启动前现有完整 Editor 构建的只读审计通过：ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 的 DLL、manifest、BuildId 与 receipt 一致。沿用上批有效全目标构建，不声称本批重新构建或普通 Editor 重启。

三个导出及三个独立冷进程重导均退出0，各自文件字节一致：

| 新参考资产（`assets/config/`） | 字节数 | SHA256 |
| --- | ---: | --- |
| `v4_physics_window_raw_gather_reference.json` | 6526810 | E29F0F205FD935C4134472AA1B87F2C1EBCA7D31202D3CFDBCDCF2CF0B43AAF6 |
| `v4_physics_window_history_reference.json` | 4345691 | BBBBBBE4BF8080BA3F7686FE9ADF16609A542F8C2A2EE422EBBD0068751F59D0 |
| `v4_physics_window_coupled_reference.json` | 11323409 | 34619A399E37875DFECB033657AC6427C0307F3AB0FC6567B5D4BFA26FA4B8F4 |

## 对照结果

三组既有测试扩为参数化测试，保留旧参考、旧计数和旧容差，没有重写旧资产。

- Raw Gather：848对/2055点，其中2041点带已有锚点、14新点、160对共享 initialPhi。Core 重新 Gather 及原 Godot 捕获的 float 行均与 native 相等，最大差0。
- Actual history：848对/2055点，804次相邻帧发布、198次滑动摩擦、14新点、0空流形；锚点最大差0，savedIndex/初始深度/状态均通过。使用的是捕获提供的匹配资格和求解摩擦比例，不能推导原生世界的生命周期调度一致。
- Coupled：8帧×24阶段=192阶段，完整20/18关节且各有1个 ConnectivityOnly，全部包含环境接触，16个异图层动态接触对。接触/关节8次位置迭代、隐式速度、2次速度迭代和投影均比较。
- .NET 9 的共同求解最大 DP `7.616943520361019e-8 cm`、DQ `1.366914492706428e-8`、V `8.773836270847823e-6 cm/s`、W `1.6588923017479829e-6 rad/s`；生产 Godot 捕获与重新 Core 回放最大值相同。不是逐位相等。
- 新旧三类合计10项测试在 .NET 8.0.28 和 .NET 9.0.17 各通过。日志 `targeted8-final.log`、`targeted9.log`。仅测试和参考资产改变，未重复全量 Core/Import 或 Godot 构建。

注意 JSON 表示精度：PowerShell 直接把原 Godot 的短 float 十进制和 UE 的 double 展开表示相减，曾得到约1.52e-5的假差。双方先还原为 float 后差0；测试始终按实际字段 float 语义比较，未放宽断言。

## 完整世界中的几何观察

另读取上一批 `native-window120.json`，将 Mannequin `hand_r → environment_0` 的原生接触几何与当前历史 Prepare 前 detected 点按原顺序比较。Core pair key 的 body 是数组下标+1，shape 是全局 registry slot，不能误当 UE 的每身体 shape 序号。

| 完成步 | 手局部点最大差 cm | 地板局部点最大差 cm |
| ---: | ---: | ---: |
| 143 | 4.76837158203125e-7 | 0.0015034553855211932 |
| 147 | 4.76837158203125e-7 | 0.001758670597439442 |
| 148 | 4.76837158203125e-7 | 0.004048873027031758 |
| 149 | 4.76837158203125e-7 | 0.004591761097267663 |
| 150 | 4.76837158203125e-7 | 0.013154456825105167 |
| 151 | 4.76837158203125e-7 | 0.01916553107923928 |

143..152这十帧原生均标记 restored。双方完整世界的姿态已经不同，上表不是同姿态窄相误差，不能据此直接修改几何公式；更不能用 UE 求解后的 anchor 与 Core 求解前 anchor 比较而称摩擦历史错误。
原生 BoxBox 入口直接委托 ConstructConvexConvexOneShotManifold，不能仅凭 shapeType=13 就认定当前通用 polygon 路径有误。

## 下一步

1. 向前追踪右手流形首次生成/重建时的姿态和 GJK 缓存，增加同姿态、同缓存的原生几何/恢复对照；区分几何生成误差与进入该帧之前已累计的姿态误差。
2. 重点保留右手局部点末位差的来源证据；不凭微小差值假定它就是十秒后不休眠的原因。同步检查 AnimMan 159..160 的颈部窗口。
3. 有生产修复后再跑30/60/120完整矩阵。稳定后接普通 Ragdoll/Get-up/Pose Recovery，再继续 Mantle、完整相机和十分钟性能预算。

用户 P4 规划修改保持原样；旧普通 Editor 两条 Condition failed 和间歇退出访问冲突不在本批修复范围，仍未关闭。
