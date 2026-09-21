# GJK 连续搜索与暖启动缓存

本批直接在 `D:/GodotALS` 的 main 实施，未创建副本，保留用户 P4 规划改动。目标是补齐完整凸包碰撞的 GJK 搜索阶段；尚未接管运行时查询。

## 实现范围

`AlsGjkSearch` 对齐 Chaos `GJK.h` 的 indexed `GJKPenetrationWarmStartable` 搜索循环，包含当前相对姿态下的 simplex 恢复、内部恢复点丢弃、收敛、32 次迭代上限及支持点身份。真实 cooked 凸包目前仅接受零 margin，盒体支持显式 margin；实际碰撞对 margin 的解析仍待实现。

缓存保留双方局部 witness 和权重。搜索失败不发布局部变更，提供 CopyFrom 给后续 owner 暂存整个 GJK/EPA/流形事务；几何身份变化必须由 owner Reset。重复暖启动测试检查零分配。

结果明确标记 NeedsEpa：此时点、距离和法向只是中间状态，不能作为最终穿透接触发布。EPA、原生选面及运行时事务 owner 尚未完成。

## 数值差异与修正

初次对照中最终分离距离几乎相同，但少数右脚场景支持点编号和缓存点序不一致，暖启动随后传播差异。失败保留在 `artifacts/physics-gjk-search-20260921/reference-test.log`。

将 GJK 方向归一化与 simplex 三角形投影的分量除法改为共享倒数乘法后，对照全部通过。原生模块使用 `/fp:fast`；这是对当前原生构建的实测匹配，未通过反汇编证明全部编译器版本均采用此运算次序，也未修改通用向量旋转或放宽断言。

旧 primitives 文档中的 component division 描述由本批修正替代。576 组原生 simplex 最近点最大差从 9.60e-10 cm 降为 0，权重最大差 5.55e-17；1024 次凸包支持点及编号仍精确一致。

## 原生参考与验证

新命令行 `-PhysicsGjkSearchOutput=<新绝对路径>` 直接调用原生完整 GJK/EPA，并输出 EPA 前的持久缓存。恢复计数在缓存副本上观察。两脚、三轴、正负方向、盒 margin 0/0.2、冷启动/暖启动共 48 序列，每组连续 11 帧，共 528 帧。

- 全部帧缓存有效点、点序和恢复计数一致；187 帧恢复了旧缓存。最大缓存权重差 1.11e-16，点差 0。
- 210 帧无需 EPA：最终距离、双方局部点及法向最大差均为 0，支持点编号及 support delta 一致。
- 318 帧需要 EPA：只验收 GJK 缓存及非明显分离检查，未宣称最终穿透结果对齐。
- 资产 `assets/config/v4_physics_gjk_search_reference.json` 为 970669 字节，SHA256 `CA7B780374C633225825FCB8696515657088AF3E5598718B2F4A4B986E522623`。两次冷导退出 0，字节一致。

Core Release 固定 JIT、集合串行 2762 项通过，按既定约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests。Godot 优化构建 0 错误/0 警告。日志均在 `artifacts/physics-gjk-search-20260921/`。

Import Release 固定 JIT、集合串行全量 2381 项通过、1 项既有跳过，退出 0。

UE 完整 Editor target 构建及项目插件审计通过，build-state fingerprint `CB61D20A12C3EE48AC75546C69A1E710D15B4AB0E054F92D10BC8226DA0DB8CF`。三个 exporter 文件与 UE 项目镜像哈希一致。

DataValidation 退出 0，0 error/3 既有 warning。普通 Editor 成功加载 exporter class 并执行 `ALS_GJK_SEARCH_EDITOR_RESTART_OK`，但日志关闭后进程退出 -1073741819（0xC0000005）；两条旧 Condition failed 同样存在。该旧间歇退出异常再次复现，未修复，普通重启门禁未通过；不能宣称所有验证通过。

## 未完成边界

本批未改运行时查询路径，未重跑十二项整链；最新有效结果仍为 9/12，三项旧休眠失败未关闭。普通 demo 尚未接新物理后端。

下一步继续 EPA、实际 pair margin、原生 vertex-plane 邻接和选面，与已验证面裁剪衔接；再接入分离 cull、解决整链失败，并推进普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 与十分钟性能预算。
