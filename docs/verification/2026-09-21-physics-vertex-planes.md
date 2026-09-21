# 原生 cooked 顶点邻接缓存

在 `.` 的 main 直接推进。本批补齐原生选面必需的数据入口，未实现完整 SelectContactPlane，未切换普通 demo 的碰撞查询。

## 为什么不能从面环推断

UE `FConvex::GetVertexPlanes3` 返回半边结构预先构建的邻接计数及前三个面编号。对于未配对边，半边遍历会提前结束；超过三个邻接面时，SelectContactPlane 遍历全部平面，否则只按缓存顺序检查候选面。

两只真实 cooked 脚各有 128 个顶点、215 个面，已有未配对边。此次直接导出发现 **256 个顶点中有 7 个的原生计数不同于扫描所有面环得到的计数**。不能按面编号排序后重建缓存，也不能假定面环关联计数就是原生候选数。

例如 foot_l 顶点 63 的原生计数为 3，缓存顺序 `[27,207,173]`，但扫描面环会得到 7；foot_r 顶点 0 原生只返回 1 个面 `[0,-1,-1]`，面环关联却有 7 个。前者会改变“缓存候选还是全平面遍历”的分支，后者会改变候选集合。

## 数据与实现

- 拓扑 schema 从 1 升到 2。每个 cooked 顶点增加原生 `count` 和三个 `planes` 槽位，直接调用 `GetVertexPlanes3`，不重建半边结构。
- `AlsConvexTopology` 保存不可变缓存并提供 `HasNativeVertexPlanes` / `VertexPlanesAt`。有效槽位验证范围、重复及对应顶点的面关联；未使用槽位保留原生哨兵，不误当有效面。
- Core 的纯几何构造仍允许没有缓存，以保持支持点等独立功能可用；访问不存在的原生缓存会明确抛错，不静默推断替代数据。
- Import 要求 schema 2、每个顶点都有缓存，继续校验原有 mesh/body/bone/shape、源顶点/索引及局部变换绑定。旧 schema、缺缓存和坏缓存均拒绝。

冷导后先在内存移除新增字段并还原 schema 编号，与旧文件逐字段比较：所有既有几何、margin、面环和绑定数据不变。原始 schema 1 文件保留在 `artifacts/physics-vertex-planes-20260921/topology-v1.json`，新导出替换主目录正式资产，不手工修改 JSON。

新 `assets/config/v4_physics_convex_topology.json` 为 263018 字节，SHA256 `EDC7D87AC49863D6120BE6B27030433C864EEC28A6806CAE07A0F351F953CA80`；第二次冷导字节一致。

## 验证

256 个顶点的计数和所有三个槽位逐值核对一致，并锁定上述 7 个与面环扫描不同的顶点。Core 新测试覆盖缓存顺序/不可变性、缺缓存访问、未使用哨兵、错误尺寸/范围/重复/非关联面。

旧原生对照全部通过：576 simplex 最近点最大差 0，1024 支持点/身份一致，528 GJK 缓存对照通过，318 EPA 帧深度/点/法向最大差 0；统一 GJK/EPA 的全部 528 帧结果及身份仍一致。

使用 `ue-diagnosing-plugin-build-load` 技能完成全 Editor target 构建及插件审计后才导出。build-state fingerprint `079FD9511C784581BC1E1F2DB4042B5FDD0832FAD4C8B811F489504A9958D8DA`，日志前缀 `20260921T130600963Z-6ec32e3b7ddc4258b4e83ba3ff1cfa06`。导出器与 UE 项目镜像 SHA256 均为 `DE75105FB292204835496A3CCB6B9E486542ADE9E74CD4703DB519323D757DF2`。

所有本批运行日志在 `artifacts/physics-vertex-planes-20260921/`。

Core Release 固定 JIT、集合串行全量 2780 项通过，按约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests。Godot 优化构建 0 错误/0 警告。DataValidation 退出 0，0 error/3 既有 warning。

Import Release 固定 JIT、集合串行全量 2388 项通过、1 项既有跳过，退出 0。

普通 Editor 成功加载 exporter class 并执行 `ALS_VERTEX_PLANES_EDITOR_RESTART_OK`，但退出码 -1073741819（0xC0000005），两条旧 Condition failed 仍在。旧间歇退出异常再次复现，未修复，普通重启门禁失败；不能称所有验证通过。

## 后续边界

尚未实现 unscaled/scaled 原生选面，也未将本批缓存连接到完整流形生成。下一步用这份真实缓存实现候选面规则、fallback 及 scaled float 分支，再与 GJK/EPA、margin 和已有裁剪衔接，通过原生完整流形对照后接入查询。

本批未改变几何查询行为、未重跑十二项整链，最新仍为 9/12。三项休眠失败、EPA 特殊退化对照、分离 cull、普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能预算都未因此完成。
