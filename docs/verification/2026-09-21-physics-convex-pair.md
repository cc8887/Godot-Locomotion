# 原始凸包初次流形：GJK/EPA、选面与裁剪组合

本批在 `D:/GodotALS` 的 main 实现未包装、零 margin cooked FConvex 对之间的初次流形，直接消费上一批原生顶点邻接缓存。它是完整后端中的一个具体形状路径，不等于所有凸包、盒体或运行时接管完成。

## 实现

`AlsConvexPlaneSelection.Unscaled` 按原生缓存计数选择遍历前三槽位或全部平面，使用 double 平面距离与法向比较，保留严格比较的首个最优面；找不到候选时遍历全部平面找最反向面。它只对应 unwrapped FConvex，不用来代替 scaled FConvex 专门的 float 分支。

`AlsRawConvexManifold.Build` 串联：

1. GJK/EPA 产生双方局部接触点、分离法向与支持顶点身份。
2. 按 cullDistance + supportDelta 拒绝过远结果；保留已计算 GJK 缓存。
3. 使用双方真实邻接选面，保留 `(double)0.002f` 的第二侧参考面偏置与可配置面法向 epsilon。
4. 零 margin 边接触使用 GJK 点；可配置边接触零 cull 行为。
5. 面接触使用已验证的裁剪、最多 32 个中间点、四点次序/缩减及投影，最后才发布输出和缓存。

参考面保留全部边，incident 面按原生上限截取。数据相关面长使用 ArrayPool，避免无界 stackalloc；已预热盒状凸包 1000 次查询零分配通过，不代表首次分配或所有形状零分配。

接口显式要求双方 cooked margin 为零和原生邻接；非零 margin、scaled wrapper、盒体专门路径及持久流形注入/恢复尚未集成，不能自动回退到近似路径并声称等价。

## 原生完整流形参考

新导出参数 `-PhysicsConvexPairOutput=<新绝对路径>`。参考直接创建原生 collision constraint 并执行 `Collisions::UpdateConstraint`，读取实际流形；没有输入预选平面或提供接触点作为预期。

真实两脚的四种有序组合，三轴正负、六距离、三角度及 cull 0/3/6，共 1296 组冷启动场景。native 实际 pair margin 均为 0，记录实际 GJK/EPA epsilon、最小选面距离、面法向 epsilon 和边接触零 cull 配置。

初次导出错误地使用了 `EContactShapesType::ConvexConvex`，此 UE 版本分发进入 default ensure，导出退出 1、结果全部为空，Core 对照也失败。已修正为源码实际使用的 `GenericConvexConvex` 并完整重建；首轮日志/失败对照及 `invalid-dispatch-reference.json` 保留，不作为有效参考。

最终 1296 组全部通过，所有场景均参与比较，无排除：

- 853 组无接触；87 组边接触；94 组参考面为第一侧；262 组参考面为第二侧。
- 点数、点序、接触类型均相同；float 接触点与法向最大差 **0**。
- 从已舍入的点重算 Phi，与原生保存的 Phi 最大差 `1.1970675672934306e-6 cm`，门槛 `1e-5 cm`。Core 输出保持现有 contact API，由后续几何层计算 Phi，不宣称保存了原生未舍入 Phi。

参考资产 `assets/config/v4_physics_convex_pair_reference.json`：1026366 字节，SHA256 `4C5FA7D3066A9D424E40730B4178F70A283D116F4503BCCFD9159D21A19D5DAE`。最终冷导及重复冷导退出 0，字节一致。

这验证的是选面所产生的完整流形结果；并未通过独立导出逐值验证内部选中平面编号，也未覆盖暖启动连续流形替换和缩放分支。

## 构建与边界

按 `ue-diagnosing-plugin-build-load` 技能完成完整 Editor target 构建/插件审计后导出，最终 build-state fingerprint `EDA7CB2EBCC8CD7CBCA82CE1B8FE6481B7827C39C56630BE1D3C242447F1E9C8`，成功日志前缀 `20260921T132527723Z-dd3c518d32994388a903ed57235ae346`。三个导出器文件与 UE 项目镜像哈希一致。

Godot 优化构建 0 错误/0 警告。日志在 `artifacts/physics-convex-pair-20260921/`，有效流形对照为 `reference-final.log`，有效首次导出为 `export-final.log`；不以较早失败记录代替最终结果。

Core Release 固定 JIT、集合串行全量 2784 项通过（按约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests）。DataValidation 退出 0，0 error/3 既有 warning。

Import Release 固定 JIT、集合串行全量 2389 项通过、1 项既有跳过，退出 0。

普通 Editor 加载 exporter class 并执行 `ALS_CONVEX_PAIR_EDITOR_RESTART_OK`，本次退出 0；两条旧 Condition failed 仍存在。之前间歇退出 0xC0000005 的原因未定位，不能据本次正常退出称为修复。

本批没有接管运行时查询、没有重跑十二项整链；最新仍为 9/12，三项旧休眠失败未关闭。下一步扩展 scaled 凸包与 box 适配、非零 margin 的边缘投影、真实查询 owner 的缓存生命周期和分离 cull；之后继续普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 与十分钟性能预算。EPA 特殊退化原生对照同样保留在未完成清单中。
