# 分离接触检测的步上下文

本批是分离接触发现的前置接入，不关闭上一批五项整链失败。主目录仍为 `.`，普通 demo 尚未切换新物理后端。

## 源码依据与实现

本机 UE 源码 `Chaos/Private/Chaos/Collision/ParticlePairMidPhase.cpp:979` 用动态且有 bounds 的整个 particle 的 local bounds 最大完整边长计算 scale；静态地板的大小不参与。两端尺寸先以 double 乘以 float CVar，再将最大值存为 float。

同文件 `GenerateCollisions:1034` 的非 MACD 路径使用两端 `GetPreVf().GetAbsMax()` 的最大值，乘 float dt；不是相对速度或速度向量长度。速度扩展受 multiplier 和最大扩展量限制，二者均正才启用。PreV 不包含本帧重力。

新增 `AlsContactCullDistance` 实现上述尺寸缩放、距离和速度扩展的数值边界。参数显式传入，没有把源码默认值当实际项目配置。`PBDRigidsSolver.cpp:364` 的 multiplier/最大扩展默认 1/3，`UpdateConfig` 还会覆盖配置；实际运行时值仍需导出验证。MACD、CCD 不包含在此 helper 中。

此前 `AlsJointIsland` 给接触的唯一速度已经经过本帧积分。现在 Gather 的可选重载传递已提交身体状态，`AlsWorldContacts` 在任何几何/流形回调前调用 geometry source 的 `PrepareStep`。旧 provider 通过默认接口保持兼容；直接旧版 Gather 提供空 previous，未来需要 PreV 的 provider 必须拒绝缺失输入，不得用预测速度替代。既有失败注入包装器也转发新上下文。

上下文只在回调内借用。源端按每次尝试重建暂存数据；发生异常仍由世界 owner 解锁 registry、回滚接触，再由 island 保留已提交身体状态。未引入额外逐帧数组分配。

## 验证与限制

- 新测试覆盖相同两端速度仍扩展、最大分量而非向量长度、扩展上限及关闭条件；重力前后速度、失败不提交、重试复用正确上一帧状态。
- 首轮目标回归 15 项通过；包含新上下文、world/order 失败注入和 manifold 的串行目标回归 18 项通过。
- 首轮 Core 全量 2737 通过、1 失败：旧 `RepeatedRestorationDoesNotAllocate` 记录 1656 bytes；随后该测试所在的串行目标回归通过。没有修改该断言或相关实现，偶发分配原因未定位。
- 最终固定 JIT 的串行 Core 全量 2738 通过，退出 0（`-- xUnit.MaxParallelThreads=1 xUnit.ParallelizeTestCollections=false`）；保留既有 Golden/TraceSchema 排除规则。Import 本批未改导入器/资产，未重跑全量。
- Godot 优化构建通过，0 warning/0 error。
- Godot 实际世界 60 Hz：3 场景、13 几何、9 生命周期通过；报告 `artifacts/physics-contact-cull-context-20260921/scene60.json`。第一次命令漏 report 参数被入口拒绝，补齐新绝对报告路径后执行通过。

当前 Godot query 尚未消费新距离 helper，仍为 cull=0；本批没有改查询 margin 或休眠阈值，没有重跑整链十二项，因此最新有效结果仍是上一批 7/12，含 30 Hz 平移回归。没有新 UE 导出/构建或原生数值参考，不能声称新增距离函数已完成原生运行时对照。

下一步导出实际 detector 参数与 whole-particle bounds，明确 kinematic/native PreV 的映射，再接分离几何、按距离激活/失效及原生对照；以五项整链失败检验效果。随后继续普通 Ragdoll owner、pelvis/胶囊/相机跟随、Get-up/Pose Recovery，以及 Mantle、完整 Camera 和十分钟预算。
