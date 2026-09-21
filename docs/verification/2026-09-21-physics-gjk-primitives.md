# GJK 单纯形与原生凸包支持点

直接在 `D:/GodotALS`、main 推进，保留用户未提交的 P4 规划修改。本批是完整原生凸包碰撞的底层阶段，未接管 Godot 运行时查询。

## 原生实现与移植

本项目冷导观察到 `p.Chaos.Collision.UseGJK2=false`，GJK/EPA epsilon 均为 float `1e-6f` 再提升到 double。使用 `CollisionOneShotManifolds.cpp` 的 GJKPenetrationWarmStartable 路径，不用另一套 index-less/SIMD 算法代替。

- `AlsGjkSimplex.Closest` 移植 `Simplex.h` 的 indexed double 线段、三角形和四面体原点最近点约简，保持严格符号比较、退化时退回先前边、子特征平局次序、有效顶点和两端 witness 同步压紧、重心权重对应关系。使用 C++ `numeric_limits<double>::min()` 的最小正规数，不能用 C# `double.Epsilon` 代替。
- 三角形内部返回法平面投影点，不通过重心坐标重构最近点；保留原生运算次序与 component division，避免大形状上的额外误差。
- 对外仅有效区间有定义，未使用槽位不作为持久状态或测试预期。参数/非有限值在变更输出之前拒绝，局部缓冲 stackalloc；有重复约简零分配测试。
- `AlsConvexSupport.ZeroMargin` 实现 `FConvex::SupportCoreScaled` 的零 margin 支持点：先 double 方向乘 scale，再转 float 比较点积，严格大于更新，保留首个最大值对应的原生顶点编号；返回 cooked 顶点乘 double scale。支持映射覆盖非均匀、反射和退化 scale 输入，**不代表零 scale 物理形状或整个 GJK 已支持**。非零 cooked margin 明确拒绝。
- 当前只包含算法底层，不含完整 GJK 迭代、warm start/收敛/迭代上限、EPA、非零 margin 调整和原生 SelectContactPlane。未用它替换 Jolt 查询或规避现有整链失败。

## 原生对照

新增 `-PhysicsGjkPrimitivesOutput=<新绝对路径>`。参考直接调用 native `SimplexFindClosestToOrigin` 和真实 cooked AnimMan 两脚的 `SupportCoreScaled`，没有复制 Core 算法生成预期。

576 个 simplex 样本覆盖 1–4 点、重复点、共线/共面、原点边界/内部、随机分布，以及 1e-9/1/1e6 尺度。有效点、两端 witness 和 count 逐值一致；最近点最大差 `9.599853366654507e-10 cm`，权重最大差 `3.3306690738754696e-16`。

两只脚各 512 次支持查询，共 1024 次，覆盖零方向、六轴、float 舍入临界方向、随机方向与四种 scale。先逐值核对现有 cooked 顶点资产，再比较支持点和顶点 ID，均完全相等。零 margin 分支按原生行为不修改 supportDelta 哨兵。

资产 `assets/config/v4_physics_gjk_primitives_reference.json`：1084934 字节，SHA256 `422458A890BFC1BE3004DC7600570093907EB12304E3DD7A6E22BBC677C9AE54`，重复冷导字节一致。

## 构建与运行证据

日志 `artifacts/physics-gjk-primitives-20260921/`。UE 完整 Editor target 构建、所有项目插件审计通过，build-state fingerprint `72A31E40C452180550617B012CB4DE6DF208C30E95C46DC1436CE70AA8A88EDB`；主仓库与 UE 插件镜像三文件哈希一致。会话句柄丢失后，先检查进程和落盘记录：无在建进程、日志仍为上批，再重新发起本批构建，未并发重复构建。

DataValidation 退出 0，0 error/3 既有 warning。普通 Editor 成功加载 exporter class、执行 `ALS_GJK_PRIMITIVES_EDITOR_RESTART_OK` 并退出 0；两条旧 Condition failed 仍存在。上两批的退出访问冲突未定位，不能把本轮成功当成旧问题已修复。

Godot 优化构建零错误/警告。Core Release 固定 JIT、集合串行 2757 项通过（按约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests）；新增原生对照两项通过。

Import Release 固定 JIT、集合串行全量 2380 项通过、1 项既有跳过；Core/Import 均退出 0。

未改运行时查询路径，未重跑十二项整链；最新有效整链证据仍是上一批面裁剪最终产物的 9/12，三旧休眠失败未关闭，普通 demo 未接新后端。

## 后续

继续完整 GJK warm-startable 迭代/持久 simplex 和 EPA，再补原生 cached vertex-plane 邻接、选面与本批支持点身份的衔接，接入已验证面裁剪。碰撞 margin 必须核对约束实际值，不能仅从 cooked hull margin 推断：`PBDCollisionConstraint::InitMarginsAndTolerances` 还根据双方动态状态/几何类型/ConvexZeroMargin 计算实际 margin（该 CVar 源码默认 0，本批未导出实际 pair margin）。之后继续分离 cull、三项整链失败、普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能预算。

下一步 warm start 的源码约束：`bChaos_Manifold_EnableGjkWarmStart` 默认 true；`TGJKSimplexData::Restore` 用当前相对变换重建 As−Transform(Bs)，再次约简，当 Distance<=epsilon 时丢弃有效计数但保留调用方默认方向/距离。不能直接复用旧世界坐标 simplex，也不能擅自每帧冷启动当等价完成。
