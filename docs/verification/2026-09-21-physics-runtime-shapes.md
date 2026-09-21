# UE 运行时形状观察与脚凸包重复变换修复

在主目录 main 推进。上一批原生查询接入暴露的问题不能仅靠调整休眠阈值处理；本批直接观察真实资产创建后的 Chaos shape，发现源元素变换与最终几何并不相同。

## 观察证据

新增命令行模式 `-run=AlsGodotExport -PhysicsRuntimeShapesOutput=<new absolute file>`。复用原有隔离世界、reference pose 和 identity component 的资产创建过程，读取 game-thread particle 的实际 shape，不推进模拟。使用 shape user-data 与源 FKShapeElem 身份匹配 authoredIndex，拒绝漏项、重复及数量不符，不假定两套形状顺序一致。

输出保留旧资产快照，另外记录每个 body 的 runtimeShapes：native/authored index、类型标志、wrapper、margin、inner margin、wrapper scale、leafLocal 和 leaf bounds。球/胶囊 GetMarginf 是其 quadratic radius，不能把它们当作 polygon 外壳 margin 使用。

新资产 `assets/config/v4_physics_runtime_shapes.json` 共 567374 bytes，两模型 40 个身体、43 个形状，包含 7 个盒体及 2 个脚凸包。两次冷导 SHA256 均为：

`BC8E54EE27BFA7FA9258928B8005FD99FEBC14C165F7DD47A9E59232B08BED59`

| 脚 | 实际 wrapper | scale | 外层 margin cm | inner margin cm | leafLocal |
| --- | --- | --- | --- | --- | --- |
| foot_l | scaled | (1, 1, 0.9999998807907104) | 0.6178215742111206 | 0 | identity |
| foot_r | instanced | (1, 1, 1) | 0.6178215146064758 | 0 | identity |

源 foot_l 的 FKConvexElem scale Y 为 1.2249667644500732，并有非零平移；它不是上表运行时 wrapper 的缩放和变换。cooked 凸包顶点已经包含元素变换。新测试对所有 cooked 脚凸包顶点仅应用观察到的 wrapper scale，所得 min/max 与 UE leaf bounds 在 1e-6 cm 内一致。再次使用源元素变换会重复缩放和平移。

## 修复与边界

- Import 新增严格 runtime shape 编译器：先去除新增观察字段，与原始物理资产快照结构精确比对；再验证 body/shape 身份完整性、类型标志、wrapper、有限数值、非奇异缩放、刚性 leaf pose 和 bounds。
- 实验后端绑定 cooked 凸包时，现在使用实际 wrapper scale 和 leafLocal，不再重复应用 authored element transform。Godot 凸包代理与 Core polygon 查询使用相同实际缩放。
- 诊断场景验证两脚所有代理顶点的实际参数传输；保留旧捕获的合成接触场景作为显式源参数诊断，不将其当当前运行时资产参数。
- **尚未把观察到的非零 polygon margin 接入查询**：FConvex 非零 margin SupportCore 和双方 pair margin 解析仍待补齐；当前查询代理仍 margin=0、cull=0。
- 盒体/球/胶囊运行时尺寸和内嵌几何姿态虽然有观察 bounds，本批没有替换其原来的源参数构造。完整几何传输、碰撞容差、分离检测和缓存退役仍未齐。
- 普通 demo 尚未接实验后端。不能宣称完整 Chaos/ALS 等价，Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能验收仍未完成。

## 验证

产物目录 `artifacts/physics-runtime-shapes-20260921/`。

- 新 Import 定向 11 项通过，包括源快照失配、重复/遗漏身份、非法 margin、wrapper、scale、bounds、类型标志拒绝，以及两脚真实 wrapper、margin、局部变换和 cooked bounds 对照。
- Import Release 固定 JIT、集合串行全量 2402 通过、1 既有跳过，退出 0，耗时 4 分 47 秒。
- Godot 优化构建 0 错误、0 警告；30/60/120 Hz 接触 smoke 均通过，每频率 551 精度、9 几何、5 流形、5 睡眠、3 事务、4 原生 polygon 检查。
- Core 算法未修改，本批不重复报告旧 Core 2806 为新跑结果。
- UE 完整 Editor 目标构建和插件审计通过，fingerprint `0AB180035D2722A2224CAE19ACE0C5044F15903BA77DF6F19D28D4336C6B47A5`。主仓库与 UE 项目三份改动源文件哈希一致。
- 冷导出及重导退出 0、字节一致；DataValidation 退出 0，0 errors / 3 既有 warnings。
- 旧模式 PhysicsAssetOutput 冷重导退出 0，与原始 v4_physics_asset_inputs.json 字节一致，SHA256 为 `C5B51449FD46390524CA6C10ED95FB4DE9E6E8E0B1C61A3A8AADD4099F8AF847`，未破坏旧资产哈希依赖。
- 普通 Editor 重启加载 exporter 标记成功，原生进程等待返回 0、退出码 0，退出后无 exporter DLL 占用。两条既有 Condition failed 仍在；既往间歇访问冲突不因本次正常退出而视为修复。

十二项整链最终仍 **8/12**，门槛不变，但失败组成变化：

| 频率 | 普通落地 | 高速落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 Hz | 失败，Mannequin 不休眠 | 新失败，AnimMan 第 9 帧身体 19 穿过地板检查触发，z=-73.3190865986863 cm | 恢复通过 | 失败，Mannequin 启动前不休眠 |
| 60 Hz | 通过 | 通过 | 通过 | 通过 |
| 120 Hz | 通过 | 失败，Mannequin 第 1173 帧才休眠，未保持一秒 | 通过 | 通过 |

30 Hz 平移平台两模型最终均静止休眠，最大锚点误差 0.642362 cm；普通 30 Hz 最终最大速度仍 4.067823 cm/s。高速 30 Hz 的穿地失败不应写成普通休眠失败，也不能仅用总数 8/12 掩盖回归。

下一步优先实现非零 FConvex margin 支持与实际 pair margin、分离检测距离，然后重跑四项失败和完整矩阵；同时核对其余形状的最终原生几何构造。尚未证明这些缺口能解释全部失败，不放宽原验收条件。
