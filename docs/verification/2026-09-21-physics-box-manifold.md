# 盒面原生初次流形与整链接入

主目录 `D:/GodotALS`、main。本批接入盒体完整位于另一盒面内部的原生接触点生成，最终十二项整链从 7/12 提升为 **9/12**：关闭 30 Hz 平移平台回归及 120 Hz 普通落地失败，未新增门槛失败。普通 demo 尚未接新物理后端。

## 实现与原生证据

`AlsBoxFaceManifold` 对应 UE `CollisionOneShotManifolds.cpp` 的 box-box vertex/plane 分支：使用原生 Box.cpp 的面顶点次序，将 incident face 投影到 reference face；四点保持原生交换 1/2 后的对角求解次序。支持正间隙与显式 cullDistance，不用膨胀碰撞形状来模拟检测距离。

当前只处理 shape1 作为 reference box、整个 incident box 的包围顶点均在面内部、中心在该面外侧的情况；穿入量和 margin 留出横向余量。边界、深穿透及 incident face 法向选择相等的情形回退，不写入部分结果。完整凸包、边缘裁剪、GJK/EPA 与原生任意输入顺序的 reference-face bias 仍未完成。

导出器新增 `PhysicsBoxGeometryOutput`，通过真实 `Collisions::UpdateConstraint` 生成初次盒-盒流形，无 Core 点输入。432 组包括六面、四种倾斜、负/正间隙、0/3/6 cm cull 与内部/边界。216 个面内部场景全部处理，其中 144 有点；逐点次序一致、接触点最大差 **0 cm**，法向差 `3.361069e-16`。216 个边界场景保留原生输出，但明确不由本 helper 处理。

新参考 `assets/config/v4_physics_box_geometry_reference.json`，651974 bytes，SHA256 `300699017B6B0DFB4A75BE63EFB385BA4A9F6843EFCAD0B098CD84B01BE0B4A6`，独立冷导重导字节一致。

Godot 对符合条件的盒对调用 Core 分支；其余保留现有有限形状查询。保留之前的规范化输入次序，反向输入做点与法向传输。144 项 Godot 正反传输检查对照**规范化的原生参考**，不声称 UE 相反原始输入的 reference-face bias 已复刻。最大点差 `1.066241e-6 cm`、法向差 `8.933738e-9`。

Godot 本批仍以 cull=0 调用，未接新距离参数：先单独验收初次几何，再继续分离发现与持续保留。没有修改睡眠阈值、迭代次数、资产或等待时长。

## 最终整链

十秒普通/高速落地、二十四秒平台生命周期；睡后均须保持至少一秒。产物在 `artifacts/physics-box-manifold-20260921/`，使用 `<mode>-<hz>.json/.log`。M/A 表示 Mannequin/AnimMan，按日志身份取帧，不能把报告字典值数组误当固定模型顺序。

| 平台 | Hz | 初次睡眠 M/A | 停后睡眠 M/A | 结果 |
| --- | ---: | --- | --- | --- |
| 平移 | 30 | 91/154 | 451/439 | 通过，回归关闭 |
| 旋转 | 30 | 151/154 | 459/未睡 | 失败 |
| 平移 | 60 | 92/69 | 877/1151 | 通过 |
| 旋转 | 60 | 91/69 | 875/875 | 通过 |
| 平移 | 120 | 170/920 | 1723/1723 | 通过 |
| 旋转 | 120 | 170/913 | 1742/1833 | 通过 |

| 落地 | Hz | 最大锚点 cm | 末秒线速度 cm/s | 睡眠 M/A | 结果 |
| --- | ---: | ---: | ---: | --- | --- |
| 普通 | 30 | 2.482846 | 2.557491 | 未睡/62 | 失败 |
| 高速 | 30 | 7.661554 | 0 | 52/69 | 通过 |
| 普通 | 60 | 1.185453 | 0 | 530/139 | 通过，但M比上批晚睡 |
| 高速 | 60 | 2.213658 | 0 | 322/287 | 通过 |
| 普通 | 120 | 0.336354 | 0 | 263/285 | 通过，旧失败关闭 |
| 高速 | 120 | 0.846848 | 1.536538 | 1173/1166 | 失败，入睡过晚 |

平台 5/6、落地 4/6，总 9/12。30 Hz 旋转末秒角速度 `0.126296 rad/s`，普通 30 Hz `0.223311 rad/s`，仍有运动。120 Hz 高速两链虽最终入睡，但均未保持满一秒，不判通过。不能把通过数增加理解为所有轨迹或入睡时间都改善。

## 构建与回归

- Core 新增分离/点序、边界与深穿透不写入、零分配四项测试通过。
- 最终固定 JIT、串行 Release 全量：Core 2743 通过（既有 Golden/TraceSchema 排除规则不变）；Import 2371 通过、1 项既有跳过，均退出0。日志 `core.log` / `import.log`。
- Godot 优化构建通过；30/60/120 Hz 各 545 精度、7 几何、5 流形、5 睡眠生命周期、3 动态场景通过。
- UE 完整 Editor 目标构建和所有适用项目插件审计通过。日志前缀 `20260921T101647629Z-32fc764fb95d4e6c8942f80aca4ed4b4`；state fingerprint `8D81071217C9A214127628B686E5ACEB3199E014C2D1965B4B157F6A0D00C0B6`。
- 三个 canonical/mirror 源文件一致；两次冷导退出0，DataValidation退出0、0 error/3既有warning。
- 普通 Editor 重启加载类，`ALS_BOX_MANIFOLD_EDITOR_RESTART_OK`，退出0。两条既有初始化 `LogAutomationTest: Error: Condition failed` 仍存在，未宣称解决。

下一步：已验证的非MACD检测距离接入分离几何/流形激活失效；凸包面拓扑与初次点序、边缘裁剪、输入次序 bias 继续补齐。以剩余三项整链失败检验效果，再继续普通 Ragdoll owner、pelvis/胶囊/相机、Get-up/Pose Recovery、Mantle、完整 Camera 与最终十分钟性能预算。仍不声明 Chaos 完整连续轨迹等价。

凸包导出缺口已定位：当前 `AlsPhysicsAssetExport.cpp` 仅保存 `FKConvexElem.VertexData/IndexData`；下一步需要核对 `GetChaosConvexMesh()` 的 cooked face 顶点循环、面法向与顺序，不能把三角索引直接当作原生合并后的接触面拓扑。
