# 缩放凸包初次流形对照

本批直接在 . 的 main 分支实施，补足双方均为零 margin scaled FConvex 的初次接触流形。未接入普通角色运行时，未重跑十二项整链；最新整链仍为 9/12，三项旧休眠失败未关闭。

## 实现与原生边界

新增 AlsScaledConvexGeometry 和 AlsScaledConvexManifold，复用原始凸包的 GJK/EPA、参考面选择、裁剪和事务式缓存发布。

- 缩放与逆缩放按 UE wrapper 保存为 float，再提升至 double；绝对值小于 1e-6f 的分量修正为正 1e-6f，包括微小负值。
- 候选面筛选使用 float 点、法向、平面距离和原生 GetVertexPlanes3 缓存顺序；fallback 接收已舍入的法向，按 double 缩放平面法向。
- 实际裁剪平面使用 double 计算与 float 逆缩放值；顶点保留原生缩放顺序。负缩放的符号乘积传给裁剪器，保留镜像后的参考面绕序。
- 无效缩放在发布缓存和输出前拒绝。新增单位测试覆盖 float 舍入、微小负值修正、正负缩放四点流形、cull 边界和失败不发布。

依据本地 UE 5.9 的 CollisionOneShotManifolds.cpp、ImplicitObjectScaled.h、CorePlane.h 和 Convex.cpp 实现。未用未缩放选面近似代替 scaled 专门分支。

当前只支持双方均包装的零 margin 凸包；不支持单边包装混合、box 专门适配、非零 margin 边投影或外部 wrapper margin。极端缩放触发的退化与选面 fallback 尚无独立原生覆盖；微小分量修复仅单位测试。原生参考验证最终流形，不独立验证选中平面编号，不代表暖启动持久流形生命周期已验证。

## 完整原生参考

导出参数：-PhysicsScaledConvexPairOutput=<新绝对路径>。导出器实际创建约束并执行 Collisions::UpdateConstraint，使用 GenericConvexConvex 分发，没有预先提供 GJK 点、平面或期望流形。

真实两脚的四个有序组合 × 三轴 × 两方向 × 六距离 × 三角度 × 三个 cull 距离 × 四组缩放，共 5184 组。缩放组合：

| 组 | 第一侧 | 第二侧 |
| --- | --- | --- |
| 0 | (1,1,1) | (1,1,1) |
| 1 | (2,2,2) | (1.5,0.8,1.2) |
| 2 | (0.5,1.5,0.75) | (2,0.75,1.3) |
| 3 | (-1,1,1) | (1,-0.8,1.2) |

记录 wrapper 实际 float 舍入后的缩放及原生 GJK/EPA/选面配置。所有行均参与比较，未排除失败或退化行：

- 3214 组空、201 组边接触、631 组第一侧参考面、1138 组第二侧参考面。
- 点数、点序与类型一致；法向最大差 0。
- 点位置最大差 3.844384073399806e-6 cm，原有门槛 1e-5 cm；不是逐位一致。
- 从 float 输出点重算 Phi 最大差 3.858489685648614e-6 cm，原有门槛 1e-5 cm。
- 旧原始凸包 1296 组仍全部通过，点/法向差 0。

新资产 assets/config/v4_physics_scaled_convex_pair_reference.json：5215171 字节，SHA256 92B349A9F5F3E3B4D6472A93505571F0A34B995023152573235F4784725C4B13。两次冷导退出 0、字节一致。旧原始凸包也重新冷导，SHA256 仍为 4C5FA7D3066A9D424E40730B4178F70A283D116F4503BCCFD9159D21A19D5DAE。

## 构建与回归

依 ue-diagnosing-plugin-build-load 技能先完成整个 Editor target 构建和插件审计，再启动导出。审计 fingerprint C5372DEB6C852E61B8AD5F1A34C39EDD586E2B7118AF77FD378A1D464BC3AAE5，日志前缀 20260921T134326625Z-3228d4340b544656b66135fb805aa3c1。三个原生导出器文件与 UE 项目镜像哈希一致。

Core Release 固定 JIT 串行全量 2787 通过，按既定约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests。Godot 优化构建 0 warning/0 error。DataValidation 退出 0，0 error/3 既有 warning。日志保存在 artifacts/physics-scaled-convex-20260921/。

Import Release 固定 JIT 串行全量 2390 通过、1 既有跳过，退出 0。

普通 Editor 执行 ALS_SCALED_CONVEX_EDITOR_RESTART_OK，日志写出 Exiting 和 log closed，两条旧 Condition failed 仍存在。启动器的 WaitForExit 数分钟未返回；对本次 PID 3632 用 Windows OpenProcess/GetExitCodeProcess 原生查询成功、退出码为 0。随后只结束本次卡住的 PowerShell 诊断启动器 PID 26576，没有强杀 Editor。由于启动器未正常收尾，本批不宣称普通重启全门禁通过；既往间歇退出 AV 与本次等待异常均未定位。

下一步：box 与非零 margin 边投影适配、实际查询 owner 缓存生命周期和分离 cull；再关闭三项整链失败并接普通 Ragdoll/Get-up/Pose Recovery。Mantle、完整 Camera、十分钟性能预算及 EPA 特殊退化原生对照仍在未完成清单。
