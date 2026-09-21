# 非零凸包 margin、运行时 pair 解析与高速落地对照

在主目录 main 继续推进。承接实际 wrapper 导出与重复变换修复，本批把真实 polygon margin 接入实验物理链路，十二项整链从 8/12 提升至 **9/12**。普通角色尚未切换，不代表完整 Ragdoll/ALS 已验收。

## 实现

- Core 新增 FConvex margin core 支持点：用原生缓存的前三个邻接面向内移动后求交，保留退化三面及两面/单面/无面 fallback。raw 与 scaled 路径遵循各自 float/double 边界，不用沿搜索方向减去 margin 的近似。
- 支持点的 delta 使用 ref 语义：零 margin 与 fallback 不改写原值。GJK 新增保留 delta 的支持接口，每侧值在同次搜索内延续；原有 out 接口保留给独立采样。所有生产值类型适配器显式实现，预热零分配测试通过。
- AlsConvexPolygonShape 接受已经解析好的非零 pair margin，支持点与选面搜索距离同时使用它。内层 cooked margin 仍须为零。
- Godot 查询绑定实际资产 wrapper margin。PrepareStep 从固定岛 inverse mass 识别动态身体（包括休眠）；静态/外部运动身体的 polygon margin 按原生规则归零，再调用已有 AlsCollisionMargins.Resolve。当前 ConvexZeroMargin 使用原生参考观察值 0；不是新增可任意配置的项目参数导入器。
- 两个动态 polygon 都有 margin 时取较小值；仅一侧有效时保留该侧。查询缓存会随解析后 margin 变化冷启动。带非零 margin 的直接诊断查询缺少 PrepareStep 运动上下文时明确拒绝。
- 球/胶囊及其混合对仍走此前路径；未把 quadratic radius 当成 polygon margin。Godot/Jolt 几何代理继续 margin=0，原生 polygon 分支自行处理真实 margin，避免叠加两份外壳。

## 原生算法参考

新增原生导出命令：

`-run=AlsGodotExport -PhysicsConvexMarginSupportOutput=<new absolute file>`

`-run=AlsGodotExport -PhysicsConvexMarginPairOutput=<new absolute file>`

支持点参考包含两只真实 cooked 脚、raw 与多种 scaled（含实际左脚的近单位缩放、非均匀与反射）、零/.05/约.61782/1 cm margin、方向搜索及全部顶点的直接调整：

- **8960 组**，顶点编号一致，最大支持点误差 **0**、delta 误差 **0**；1740 组保留初始 delta=17。
- 两面/单面/无面及人为退化三面另外有解析 Core 测试；没有把这些人为拓扑冒充真实资产原生样本。

非零 margin 的 instanced/scaled 脚、零/.2 cm 盒体，六种形状全部有序组合：

- **11664 组**实际 UpdateConstraint 初始流形全部通过。
- 7168 空、903 边接触、966 第一侧参考面、2627 第二侧参考面。
- 存储接触点与法向最大差 **0**，点数、次序与特征一致；由点重算 phi 最大误差 1.519306e-6 cm。
- 原有 raw1296/scaled5184/box6480 流形及 528 帧 GJK/EPA 对照仍通过。

新资产均冷重导字节一致：

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| v4_physics_convex_margin_support.json | 3852795 | 9C2CF930EE7C6ABAA5BA0E79369F8C62383C99EBE75C7FFF02AF71B721090A4B |
| v4_physics_convex_margin_pair_reference.json | 11999852 | FF72968749558A1250270CD8325294EEAE8A630641A09D744585C706511999FE |
| v4_physics_high_drop_coupled_reference.json | 7080659 | 6B635161767DFBC8B8C1EDB94E3EBDDE6A15881E89F7C796FA8348DC6389CE56 |

## Godot 与整链结果

产物目录 `artifacts/physics-convex-margin-20260921/`。

- Core Release 固定 JIT、串行全量（沿用 P5A golden/schema 排除范围）**2816 通过**。
- Import 同配置全量 **2404 通过、1 既有跳过**；之后新增高速落地阶段参考，定向运行新旧三组 coupled 测试全部通过。未将后加的一项算作此前全量执行。
- Godot 优化构建通过，0 错误、0 警告。
- 30/60/120 Hz smoke 每频率通过：551 精度、9 几何、5 流形、5 睡眠、3 事务、**59 polygon** 检查。新增54组运行时动/静 pair 参数传输和一次缺少上下文拒绝；确认确实区别于零 margin，且中止查询不发布缓存。

| 频率 | 普通落地 | 高速落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 Hz | 失败：Mannequin 未休眠 | 失败：AnimMan 身体19，第9帧穿地检查触发 | 通过 | 失败：Mannequin 启动前未休眠 |
| 60 Hz | 通过 | 通过 | 通过 | 通过 |
| 120 Hz | 通过 | **恢复通过** | 通过 | 通过 |

高速120两模型分别在第496/521帧休眠，最终速度/角速度0，最大锚点误差0.861583 cm，最终限位误差0.00507428 rad。没有调宽休眠门槛、提高迭代数或修改运动输入。

## 高速30失败的进一步证据

保留 `high-30-trace.log` 及 `high30-capture/`。第6帧 foot_l 对 StartFloor 无接触；第7、8帧各3点，地板接触点 z=-43.398404 cm，法向向上。穿透深度从约8.35 cm 增至约21.4 cm，随后第9帧身体 z=-73.3190866 cm 触发失败。

因此不能简单归因于未查询地板或误选底面。把两模型第6/7/8帧的真实 Gather 后输入送入原生接触/关节共同求解，6组、144阶段在既有容差下通过：

- 新 Core 回放最大 DP差3.147780e-6 cm、DQ差1.862645e-7、V差1.239630e-4 cm/s、W差8.530756e-6 rad/s。
- 实际 Godot 捕获相对原生最大 DP差2.691506e-6 cm、DQ差2.417711e-7、V差1.549068e-4 cm/s、W差7.248278e-6 rad/s。
- 包含3个不同图层级的动态 shock 对，未关闭 shock 来换取通过。

该对照固定了 Core 提供的 Gather 输入、接触对和阶段顺序，不证明这些输入与原生真实世界相同，也不覆盖前几帧的碰撞发现、历史创建、积分、CCD 或睡眠。下一步继续核对原生分离 cull、历史/Gather 初始量、其余形状实际几何，必要时扩展原生连续轨迹观察，不能仅凭阶段对照宣布穿地修复。

## UE 构建与限制

遵循 ue-diagnosing-plugin-build-load 的完整 Editor 目标构建/审计流程。首轮导出源文件的 auto* 与 TObjectPtr 容器不匹配导致编译失败，已修复，原失败日志保留。最终构建和插件审计通过，fingerprint：

`4B5F7D5F65D198FAFA646F75D1A79012247A0FDB6DABEA072AE85D1C640CC71F`

主仓库和 UE 项目的四份改动源文件哈希一致。所有冷导出与重导退出0；DataValidation退出0，0 errors/3既有warnings。普通 Editor 加载标记成功，但原生退出码 **0xC0000005**，退出后无 exporter DLL 占用；两条既有 Condition failed 仍存在。故不能称 UE 全门禁通过，间歇退出异常未修复。

仍缺：运行时分离检测距离（当前 cull=0）、完整 native midphase 退役与容差、其他 primitive 最终尺寸/姿态核对、三项整链失败，以及普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算。
