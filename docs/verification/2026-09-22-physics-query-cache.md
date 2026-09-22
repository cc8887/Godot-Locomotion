# 真实查询缓存与流形恢复观测

## 本批结果

在主目录 `D:/GodotALS` 的 `main` 继续上批反向变换修复后的排查。新增可选、只读的 GJK 查询输入/输出快照及流形恢复标志，没有修改几何或求解公式。

新捕获明确区分：本步确实调用 polygon Query 的接触对，记录实际 query input/cache output；经流形恢复、未调用 Query 的接触对，`polygonQuery=null`。查询输入在身份/margin重置之后复制，不能直接把上一帧提交缓存误当本步实际输入。

`AlsPolygonQueryCache` 默认关闭额外快照，开启时按entry延迟分配；读取复制到独立cache对象，外部不能改内部缓存。省略查询、Release、Abort、下一步均不暴露过期快照；失败/已提交阶段拒绝读取。原有无分配热路径测试保留并通过。

## 实际重放

捕获两模型完成77..79和141..160的46帧，目录 `artifacts/physics-query-cache-20260922/captures/`。从中筛出8次右手/地板实际重新查询：

| 模型 | 完成步 | 查询前缓存点数 |
| --- | --- | ---: |
| AnimMan | 78、79、141 | 0、3、3 |
| Mannequin | 77、78、79、142、153 | 0、3、3、3、3 |

新增 `Export-BoxCaptureInputs.ps1 -RequireCapturedCache` 保留查询前/后缓存。缺少新快照字段的旧捕获明确拒绝，字段存在但为null的恢复帧跳过。新原生导出器将输入seed写入实际FGJKSimplexData，再调用BoxBox UpdateConstraint；第二次查询继续使用native本次产生的缓存。输出native接触与缓存，既比较Core独立重算，也比较原Godot实际捕获。

8次真实查询中6次带非空旧缓存，两次查询共64点：位置、法向和Phi逐值一致，缓存点差0，权重最大差 `1.1102230246251565e-16`。保留既有GJK权重1e-10/点1e-8容差；接触仍精确相等。cull固定6 cm，真实输入中的生命周期资格/姿态仍由Core提供，不是独立原生世界轨迹或流形恢复等价证明。

新资产 `assets/config/v4_physics_box_cache_capture_reference.json` 81619字节，SHA256 `9B18D747C15D0C495763D465B95BF23891220CBE33F9E4EC1E3A001F8F36004A`。独立冷进程重导字节一致；旧45姿态参考重导仍为 `CEF5150770906353E2587907AFC0E69BDC5C910D7F9243AAE5D27ECD019AF4C4`，未重写旧资产。

## 恢复窗口与下一步

当前Core Mannequin右手在142步重建、143..152全部恢复，153重建。此前native-window120里143..152也全部标记restored。说明147..150的分歧期间该对没有新GJK查询；这八个真实重建输入中的GJK计算已通过对照。

这不排除其他接触对改变共同求解的输入，也不证明恢复点更新正确。下一步在原生完整世界诊断中记录初始流形点、当前shape世界姿态和恢复状态，针对真实恢复帧重算几何，并检查右手周围接触对；避免继续只重复测试已验证的右手重建公式。当前流形容差仍由Godot代理bounds尺寸生成，原生按实际leaf bounds计算；该边界需独立核验，不能在尚未证明影响时把它当唯一原因。

## 验证与限制

- 快照开启前后18个重叠捕获、每帧全部24个共同求解阶段逐值一致（共432阶段）。普通120仍AnimMan658睡、Mannequin1200步未睡；末秒V2.242934226989746 cm/s、W.27109295129776 rad/s和接触总数96299与上批相同。
- Core缓存/恢复定向20项、Import新旧BoxCapture及GJK搜索3项，在.NET8.0.28/.NET9.0.17均通过。新旧样本包含528个既有连续GJK帧。无新全量Core/Import声明；最新全量仍上批2860/2448+1旧skip。
- Godot Optimize构建通过，60 Hz接触smoke通过。本批未重跑十二项矩阵，最新仍8/12四项旧休眠失败；诊断采集退出1是既有最终休眠断言，不是通过验收。
- UE第一次构建因新局部变量P遮蔽已有粒子数组被C4456拒绝；改名SeedRow后重新完成整个Editor目标构建/审计，fingerprint `D2A41294410DFC3B1D865DC9E284B1F6B3DD264A496AFF1CD852AEED3BAC5E3C`。失败日志保留。
- 新exporter源码主仓库与UE镜像SHA256均 `BB79A5447EEC3DE7F76D986400E6B9B2419AEBB985A03CA6F42F4573C0CA3525`。
- 原生新旧重导/DataValidation均退出0，数据验证0 error / 3旧warning。普通Editor PID22956加载标记成功、原生退出0；两旧Condition failed和既往间歇访问冲突未解决。

过程日志、新旧重导、输入和重复输入位于 `artifacts/physics-query-cache-20260922/`，输入重复SHA256 `CAEDE63744E09E51C96578B34A493C83EC5E501BBF9A1514F82B98A2FD18D65B`。用户P4规划修改保持原样。普通demo仍未接实验Core刚体后端；Ragdoll/Get-up/Pose Recovery、Mantle、完整相机与十分钟预算等完整目标继续保留。
