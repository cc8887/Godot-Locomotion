# 实际世界的碰撞两端与约束图顺序

本批直接在 . 的 main 修正碰撞两端顺序。独立原生世界的 20 帧、416 个接触对与 Core 的两端顺序、约束图内次序和接触集合一致。完整矩阵仍为 **8/12**；四项休眠失败未修复，部分轨迹误差反而扩大，普通角色尚未接入 Core 刚体后端。

## 原因与修正

UE 当前源码 `Chaos/Collision/CollisionKeys.h` 的 AreParticlesInPreferredOrder 实际执行 `P1->ParticleID() < P0->ParticleID()`。同类型双动态粒子按创建 ID 降序，单动态优先；随后 CollisionResolution.cpp 的 CalculateShapePairType 将 sphere 放在 capsule/polygon 前、capsule 放在 polygon 前。混合形状分派可以覆盖动态优先。不能按注释中的 lower ID first 或注册槽升序推断实际顺序。

新导出的实际粒子 GlobalID 均为 -1、LocalID 等于身体定义索引。Mannequin 第 147 步中，原生为 spine_02→pelvis、thigh_r→thigh_l，旧 Core 两对均相反。416 个接触中有 111 对动态/动态、101 对反注册顺序。原生 allocator 数组次序与约束图次序不同，不能用前者代替求解顺序。

- 注册表独立维护粒子创建序号；形状注册/替换不改变它，RebindBody 分配新序号并推进 generation。
- WorldContacts 在 bounds/恢复/查询前取得两端顺序，几何、摩擦历史、Gather、求解使用同一有向 key；三角存储仍为无向槽。
- PolygonQueryCache 允许反向 key 共用槽位，但反向身份不能继承旧 witness；失败回滚和旧身份 Release 不影响新身份。
- Godot 显式原生 sphere/capsule/polygon 绑定启用该策略，trace 转发；非原生 provider 保持默认行为。
- 捕获增加 prepared key，UE 只读导出粒子 ID、接触 graphOrder/level/color/insertionKey/sleeping。未修改 UE 引擎或物理算法。

这是当前有界 primitive 路径和固定身体拓扑的创建顺序映射，不代表所有网络 GlobalID/LocalID、网格、高度场分派均已实现。

## 原生世界对照

普通 120 Hz、两模型各第 143..152 步，冻结窗口另保留第 142 步基线。Compare-WorldContactOrder.ps1 按实际 body/local shape ID 对齐，比较 Core 捕获序列与原生 island graphOrder，拒绝重复/无效身份。

20 帧的 orientationDifferences、orderDifferences、nativeOnly、coreOnly 均为空。416 对全部 graphColor=-1。两次比较报告字节一致；没有把 allocator 顺序、单独同输入公式对照或图内顺序当作完整后续 solver partition 调度证明。

本批 native.json 和 repeat.json 字节一致，SHA256 为 296436DD5431062A71C74801336215AF64567E7D8C2592ABE47C462ECEBCED90。相对于前一原生 world 窗口，全部 48040 身体样本的姿态、线/角速度、awake 与每帧 awake 计数及 setup 未变，新增只读字段未改变模拟。

冻结文件 `assets/config/v4_physics_world_order_window.json`：1464358 字节，SHA256 2EF8829A0237F853851AB9A0316F0AE20976DB389D22C4F743AE5ED84BC2B44A。未改写既有 golden。

## 回归结果

产物：`artifacts/physics-world-contact-order-20260922/`。

- Core Release 固定 JIT、串行全量 **2871 通过**，沿用排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests 的既有过滤。定向 28 项在 .NET 9.0.17 通过。
- Import 定向 4 项在 .NET 8.0.28 / 9.0.17 通过，含新独立原生顺序、旧实际流形恢复、旧约束图参考。没有新 Import 全量；最近仍是 2448 通过 + 1 既有跳过。
- Godot Optimize 构建 0 warning / 0 error。30/60/120 Hz smoke 全过，新增 native_pair_order_checks=3，覆盖实际双胶囊有向 Gather/history、Rebind 新身份、动态/静态顺序；旧检查继续通过。
- 首次 Core 测试错误地把只读已提交 history count 当作当前匹配资格，已改为断言 SavedIndex=-1、HasAnchor=false、InitialManifold=true。首次 Import 将 Dynamic 枚举误写 3，查原生枚举后改 4。失败日志保留。

| Hz | 普通 | 高速 | 平移平台 | 旋转平台 |
| ---: | --- | --- | --- | --- |
| 30 | 休眠失败 | 通过 | 休眠失败 | 休眠失败 |
| 60 | 通过 | 通过 | 通过 | 通过 |
| 120 | 休眠失败 | 通过 | 通过 | 通过 |

八个成功报告相对前一批均变化，不宣称字节不变。普通 120 Hz AnimMan 第 675 步睡眠（原 658），Mannequin 到 1200 步仍醒；末秒最大线速度 2.253145217895508 cm/s、角速度 0.2741921842098236 rad/s、累计接触点 97488、最大锚点误差 0.5033184118913622 cm。没有放宽休眠阈值。

相对独立原生世界的采样最大速度差（cm/s）：

| 身体集合 / 完成步 | 修正前 | 修正后 |
| --- | ---: | ---: |
| Mannequin / 147 | 0.0482613508 | 0.172008286 |
| Mannequin / 150 | 1.492923998 | 1.566225456 |
| Mannequin / 160 | 0.099515731 | 1.253298183 |
| AnimMan / 160 | 0.248268006 | 0.350294552 |

移植缺口已由独立世界证据确认并修正，但上述采样退化，不能称为整体轨迹改善。姿态不同的两世界接触点差也不能直接当作同输入几何公式错误。

## UE 构建与加载边界

按 ue-diagnosing-plugin-build-load 技能完成完整项目 Editor target 构建及插件审计。首次 const Particle 传 GetParticleLevel 编译失败，局部指针类型修正后完整重建通过；仍只读。成功 fingerprint：E42970B4C959FA9F91522191E11F069FB4EF1D2399CC5D79E5A6245739564164，引擎 BuildId：186ff094-6861-4ab2-95dd-e0889004ba00。主仓库与 UE 插件镜像 cpp 哈希均为 37A31F16C4C378E68608A9979F2962461DAA0B9C6B62E769C85425BEA154670C。

两次冷导出成功且一致；DataValidation 退出 0、0 error / 3 warning。普通 Editor PID 15788 加载 exporter 类，记录 ALS_WORLD_ORDER_EDITOR_RESTART_OK、assets_saved=0，退出时仍发生 **0xC0000005**（原生退出 3221225477），启动器退出 1。两条旧 Condition failed 仍在。本次普通重启门禁失败，没有用加载标记掩盖失败；普通启动没有执行新增 world 诊断，退出异常原因未解决。

## 后续

接下来在当前原生两端顺序下向最早数值分歧追溯，区分同输入几何/求解和上游状态差异；不再重复把已验证的手部 GJK、恢复或 143..152 步顺序当作未知。继续普通 120 Hz 与低频稳定性，再接普通 Ragdoll/Get-up/Pose Recovery；Mantle、完整相机与最终十分钟性能预算等目标仍保留。

用户 P4 规划文件未纳入提交，SHA256 仍为 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。
