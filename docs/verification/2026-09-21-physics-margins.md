# 原生碰撞对 margin

直接在 `.` 的 main 推进，用户 P4 规划改动保持原样。本批补齐 GJK/EPA 与原生选面之间的 margin 解析规则，尚未接管运行时查询。

## 规则与范围

`AlsCollisionMargins.Resolve` 对照 UE `FPBDCollisionConstraint::InitMarginsAndTolerances` 中的 margin 分支，输入为双方已解析的原生 float 形状 margin、是否为球/胶囊、运动状态及 `ConvexZeroMargin`，输出双方 float 碰撞 margin（厘米）。

- 两个多面体：只有动态/休眠侧保留形状 margin；双方非零时两侧都取较小值，一侧为零时另一侧至少取配置最小值。
- 双方多面体动态 margin 均为零时，原生分支只给第二侧配置最小值；保留该次序，不能自行对称化。
- 球/胶囊 margin 是半径，不因静态/运动学状态清零；与多面体配对时多面体侧 margin 为零，两个球/胶囊分别保留自己的半径。
- 显式区分 Sleeping 与 Kinematic：原生 `IsDynamic()` 包含 Sleeping。非法输入拒绝。

本批只实现 margin 规则，没有把同一原生函数里的 CollisionTolerance 称为已实现或已对照，也没有从 cooked hull margin 推断所有实际运行时碰撞对的参数。下一步 owner 必须传入正确的形状、双方状态和设置。

## 原生观察

新增导出参数 `-PhysicsMarginOutput=<新绝对路径>`。参考使用实际 collision allocator 创建约束，读取 Setup 后的 `GetCollisionMargin0/1`、quadratic 标志和 particle IsDynamic；未复制 Core 公式作为预期。

七个形状为 margin 0/0.2/0.6 的盒体、半径 2 的球、半径 3 的胶囊、实际 AnimMan 两脚 cooked 凸包。遍历有序形状对、四种运动状态与配置最小 margin 0/0.05，共 1568 组。首次冷导观察到项目 `ConvexZeroMargin=0`，实验设置只在独立导出进程中变动并恢复，不保存资产/配置。

逐值对照 1568 组全部精确一致，包含 448 组真实脚部为第一侧的组合；休眠动态身份和球/胶囊分类单独核对。两脚 cooked margin 均为 0。

资产 `assets/config/v4_physics_margin_reference.json` 为 491824 字节，SHA256 `F212C01FF82FC5364AF1130561A4F42623A2808522ADA5A7933BC4CDFB760917`。两次独立冷导均退出 0，字节一致。

## 构建与检查

使用 `ue-diagnosing-plugin-build-load` 技能要求的完整 Editor target 构建/插件审计，而不是只编译导出器。首次构建因 FConvexPtr 无法隐式转为 FImplicitObjectPtr 失败；改为显式持有同一 cooked 对象的引用后重建成功，未复制/重烘焙或修改凸包。

成功构建日志前缀 `20260921T125416364Z-41e5f08c21134947939906aaef74f05f`，build-state fingerprint `E83B87CF0B6B3205C606982F22A79E7541167D8B1B55BB9294BF3CA0E7C1C23D`；三个导出器文件与 UE 项目镜像哈希一致。首个失败构建日志保留，前缀 `20260921T125238668Z-d33b6ed353c041a6a7c2fc2da2d2359c`。

Core Release 固定 JIT、集合串行全量 2773 项通过（按既定约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests），Godot 优化构建 0 错误/0 警告。日志在 `artifacts/physics-margins-20260921/`。

Import Release 固定 JIT、集合串行全量 2383 项通过、1 项既有跳过，退出 0。

DataValidation 退出 0（0 error/3 既有 warning）。普通 Editor 加载 exporter class，执行 `ALS_MARGIN_EDITOR_RESTART_OK`，本次退出 0；两条旧 Condition failed 仍存在。此前间歇退出 0xC0000005 的原因未定位，不能把本次成功当作修复。

## 后续

尚未改运行时查询路径，未重跑整链；最新仍为 9/12，三项旧休眠失败未关闭，普通 demo 尚未切换。

继续导出 cooked 顶点 `GetVertexPlanes3` 原生缓存与选面对照，不能从面数组自行排序推断邻接。在 native SelectContactPlane 中，超过三个邻接面时会遍历全部平面，否则只按缓存顺序遍历；scaled凸包还使用专门的 float 选面路径。之后组合完整 GJK/EPA、margin 与裁剪，接分离 cull；EPA 特殊退化对照、三项整链失败、普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算均仍未完成。
