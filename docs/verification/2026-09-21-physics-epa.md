# EPA 穿透求解与统一 GJK 接口

在 `.` 的 main 直接实现，保留用户 P4 规划修改。本批延续 GJK 搜索，补齐 EPA 主算法及统一返回接口；还没有切换运行时碰撞查询或普通 demo。

## 实现

- `AlsEpa` 按本机 UE 5.9 `Chaos/EPA.h` 实现 1–4 顶点初始化、较远支持点选择、四面体绕序和邻接更新、可见边界栈遍历、面扩展、上下界收敛、128 次迭代上限以及最近点/重心 witness 回算。
- `AlsEpaWorkspace` 由调用方持有，缓冲按需扩容后复用；不是每帧临时分配。一个 workspace 不能并发使用。1000 次暖启动重叠盒查询零分配通过，仅说明该已预热场景；新复杂度可能扩容，不代表所有输入首次查询零分配。
- `AlsGjkPenetration` 把 GJK 与 EPA 接起来：Ok/MaxIterations 返回 EPA 点，BadInitialSimplex 使用 EPA touch normal 和原 GJK witness，Degenerate 保留 GJK 结果。两端点/法向分别处于各自形状局部坐标。原生 MaxSupportDelta 保留 GJK 最后一轮值，不擅自累加 EPA 查询。
- 统一入口先暂存 GJK 缓存，EPA 成功或按原生规则回退后才发布。注入 EPA 支持点异常时旧缓存不变，workspace 可重试；后续 owner 仍需把流形生成纳入更外层事务。
- 原生可见边界 10000 次安全限制若耗尽，Core 抛错而不是继续使用部分边界；这是明确的防止坏几何发布边界，不宣称与原生警告后继续完全等价。

## 实际原生对照

复用上一批由 UE 原生 `GJKPenetrationWarmStartable` 生成且重复冷导一致的 `v4_physics_gjk_search_reference.json`，未修改参考输出或调整精度门槛。

48 组真实 cooked 两脚与盒体连续运动，共 528 帧。统一入口逐帧核对穿透值、双方点/法向、顶点编号、support delta，并确认 EPA 没有改变供下一帧使用的 GJK 缓存。

318 个重叠帧单独调用 EPA：最终深度、点与法向最大差均为 **0**，支持顶点编号一致；无退化回退，队列最大 12。其余 210 帧沿用 GJK 分离结果。该参考覆盖盒 margin 0/0.2，凸包 margin 0；不代表实际碰撞约束 pair margin 已解析。

退化点触碰、盒体深度/witness 几何一致性、EPA 中途异常不发布与重试有独立 Core 测试。真实帧未触发 Degenerate/MaxIterations，仍需补专门原生参考核验这些分支，不能用普通帧通过宣称极端退化对齐。

## 面排序的额外证据

UE 当前目标使用 MSVC 14.44.35207 工具目录，编译器 `_MSC_FULL_VER=194435228`。EPA 使用 `std::sort` 按距离降序排列后从末尾取面；相同距离的面身份不可任意重排。Core 按该实现保留插入、median/ninther 分区和堆回退次序，不使用 .NET 自带排序替代。

`tools/diagnostics/export_epa_sort_reference.cpp` 使用同版本实际 STL 生成 256 组参考：长度 0–1024，含 31/32/33、40/41 边界，八类相等/递增/递减/交错/随机距离；额外强制 ideal=0 验证堆回退。各组最终索引次序全部一致。这是排序对照，不能代替具有大队列的 EPA 几何对照。

`tests/Als.Core.Tests/Fixtures/Physics/epa_sort_msvc.json` 为 525324 字节，SHA256 `FB8D2AD57C9BB43518225959CA4AD81BAAA13229E928E92A16C4F2F5E7124B3B`，重复生成一致。生成器对已有目标拒绝覆盖。首次经嵌套 cmd 引号调用编译失败，保留错误；改用 artifacts 中明确参数的 build_sort.cmd 后编译/生成成功。

## 验证边界与下一步

日志位于 `artifacts/physics-epa-20260921/`。Godot 优化构建 0 错误/0 警告。本批没有修改 UE 插件、引擎或配置，没有重跑 UE 全目标构建/普通重启；上一批普通 Editor 日志关闭后的 0xC0000005 和两条旧 Condition failed 未修复。

Core Release 固定 JIT、集合串行全量 2767 项通过（按约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests），退出 0。

Import Release 固定 JIT、集合串行全量 2382 项通过、1 项既有跳过，退出 0。

尚未改运行时接触查询，未重跑十二项整链。最新有效结果仍为 9/12，三项旧休眠失败保留。

下一步补特殊退化原生参考、实际 pair margin 与 cooked 顶点原生邻接/选面；将 GJK/EPA 与面裁剪组合成完整凸包流形，再接分离 cull 并处理剩余整链失败。普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能预算仍在总目标中，均未因本批算法对照而完成。
