# 原生 shape bounds 参考与 120 Hz 落地基线

本批在 `D:/GodotALS/main` 新增独立 UE 参考及验证，未改变 Godot/Core 生产公式，也未接通普通角色 Ragdoll。用户 P4 文档修改保留。

## 原生筛选对照

新增 `PhysicsShapeBoundsOutput` commandlet 分支。实际调用 `FSingleShapePairCollisionDetector::GenerateCollision`，启用 deferred narrow phase，使其激活结果直接反映私有 `DoBoundsOverlap` 的判定。没有复制 Core 的筛选公式作为期望值，也没有绕开 C++ 私有访问权限。

该模式专门用于观察 bounds：此前激活来自明确的重叠 seed，不代表这些 seed 已求出真实接触点。分别运行从未激活、紧邻上一 epoch 激活、间隔一个 epoch 三种状态；原生 `IsUsedSince` 记录与传给 Core 的状态逐项比较。此参考不证明完整世界中所有约束的历史状态均一致。

五种形状为细长盒、带偏移球、非轴对齐胶囊，以及从实际 AnimMan PhysicsAsset 读取的两只 cooked 脚部凸包。凸包在本参考中使用非均匀测试缩放 `(1.5,.8,1.2)`，不是声称其与普通角色实际 wrapper scale 相同。覆盖25个有序组合、4个角度、7个位置、3个 cull、3种历史，共 **6300组**。

- 原生接受4084、拒绝2216；130组因上一帧激活而改变判定。
- Core 所有布尔判定、原生 bounds flags 和球球 distanceSize 一致。
- 世界包围盒六个分量最大差 **0 cm**，在 .NET8 和 Godot 使用的 LatestMajor roll-forward 环境均通过；测试保留1e-9 cm上限。
- 另外核验现有两套真实资产 **32个胶囊**：`Endpoint0 + Axis * Height` 与既有 UE primitive snapshot 的 endpoint1 逐值相等。因此当前资产的端点重建末位疑点已排除；不能推广到未测的新资产。
- 新文件 `assets/config/v4_physics_shape_bounds_reference.json`，6,378,509 bytes，SHA256 `23E1CF1BA95EB23D871A959C789A5BEC99D46D62BEF02C8747F4F189A6E862D6`。两次独立冷导字节一致。

尚未在该参考覆盖 whole-particle 多形状 union/动态速度扩张、CCD/MACD、bounds checks关闭、mesh/heightfield/levelset或通用多岛睡眠。

## 普通 120 Hz 的新证据

从当前 Godot 入口捕获两角色初始身体、完整13个场景环境、惯量与材质，交给已有原生完整世界 exporter 独立推进1200步。原生 sleep settings保持启用，没有输入 Core 的中间轨迹或预期休眠状态。

| 模型 | UE 连续睡眠起始帧 | UE 保持帧数 | Core 当前结果 |
| --- | ---: | ---: | --- |
| AnimMan | 635 | 566 | 687帧睡眠 |
| Mannequin | 1004 | 197 | 1200帧仍未睡眠 |

两套原生角色均满足十秒内睡眠并保持至少一秒。本次证据确认普通120的 Core失败是实际差距，不能把它解释为原生也不睡，也没有因此放宽预算。此前普通30的原生不睡结论仅适用于其自己的参考，不能套到120。

完整原生120基线冷重导一致：47,421,909 bytes，SHA256 `A0AA64F5DB1DBC1B4C8A0233C3615DD49765EFA6BFC7928BF2820480E1105844`。该诊断原始文件与设置保存在本批 artifacts，尚未作为新的完整世界冻结测试 fixture 纳入 assets。

### 实际逐帧差异

第一批捕获两角色各0..59步，与原生完成步1..60对应。最大线速度差为 AnimMan0.00298852 cm/s、Mannequin0.00251862 cm/s。

继续抽样到400步，再密集捕获141..160完成步，定位到：

- Mannequin141帧最大差0.00777150，146帧0.02899818，147帧右手0.11575455，149帧0.33732610，150帧右手1.53549594 cm/s。
- AnimMan157帧0.00414650，158帧0.02000229，159帧颈部0.06778954，160帧颈部0.29288135 cm/s。

这是定位误差增长的观察结果，不是新增容差标准。另导出原生143..152帧接触窗口，与 Core同帧按body pair聚合比较：两模型20帧的有效接触对及点数均无差异。这排除了该窗口“多一对/少一对、聚合点数不同”的直接解释，但未证明每个shape的点序、几何、摩擦锚点、Gather输入或迭代结果一致。

下一步优先 Mannequin147–150帧的右手接触：比较原生局部点/恢复锚点/初始状态，以及同输入 Gather 和共同迭代，区分历史输入差异与小误差在非线性迭代中的放大。随后检查 AnimMan159–160和30 Hz既有分歧。

## 构建与回归

- 按 UE 插件构建诊断技能执行完整 Editor目标和全部项目插件审计，成功状态 fingerprint `EC12133B15B1BCD99B78FA6755865C8CB5B9763A71B00E823C444705377A7346`。首轮四元数条件表达式类型不兼容；改显式 `FRotation3` 后重建通过，失败日志保留。
- canonical exporter和UE项目镜像源码哈希一致。新 C++ SHA256 `03AA72DC76C6BB04FF30F20D7702FE9242BCA51C746FE345D3BB022BE7BA5FA3`。
- 两个新Import测试默认/.NET LatestMajor通过；Release固定JIT串行Import全量 **2443通过，1既有条件跳过**。首轮测试读取了错误的pose字段名，改用已有exporter的position/rotation后通过，日志保留。
- Godot优化构建0 warning / 0 error；生产代码无修改，本批不重复声明新的全量Core或十二矩阵结果。普通120为诊断采样重跑，仍失败；最新完整矩阵仍上批8/12。
- DataValidation成功，0 error / 3既有warning。普通Editor PID32488加载标记成功、退出0，进程已消失；两条旧 `Condition failed` 保留，既往间歇0xC0000005未定位，不能称已修复。

证据目录 `artifacts/physics-shape-bounds-reference-20260922/`。`setup120/`、`capture120/`、`later120/`、`window120/`保留设置及实际Core采样；`native-normal120*.json`是重复基线，`native-window120.json`含143..152接触；三个 differences120报告记录输入SHA。`compare_pairs120.ps1`运行无差异输出，比较的是body pair聚合而非逐shape几何。新CLI仅导出数据，未保存UE资产。

完整目标仍未完成：四项当前休眠失败、普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 与最终十分钟性能预算继续推进，普通demo仍未切换实验物理后端。
