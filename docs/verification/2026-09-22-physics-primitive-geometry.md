# 实际 primitive 几何与 leaf 坐标绑定

在 `D:\GodotALS` / `main` 继续推进。本批补齐实际 sphere/capsule/box 的几何数据与实验运行时绑定；没有完成全部窄相算法，普通角色仍未切换后端。

## 原生观察与差异

新增命令 `-run=AlsGodotExport -PhysicsPrimitiveGeometryOutput=<new absolute json>`，沿用实际身体创建和 shape user-data 对应关系，直接读取原生 leaf 的 sphere center/radius、capsule center/endpoint0/endpoint1/axis/height/radius，以及 box min/max。

输出 `assets/config/v4_physics_primitive_geometry.json`，589584 字节，两次冷导 SHA256 一致：

`1255B06C0E533BDEBB0CD72038030AED1705EED225A6FBDBF4A922D37F7EA8FF`

43 个 shape 中有 **32 个 capsule、2 个 sphere、7 个 box**，另外 2 个 convex 继续使用此前 cooked 数据。胶囊的局部平移和旋转已经烘入 float 端点/轴，leaf transform 为独立观察值，不能再把原始资源 transform 应用到这些已烘焙坐标上。盒体 min/max 对称，另有 leaf transform。

例如 Mannequin spine_01 的原生胶囊中心 Z 为 `3.814697265625e-6` cm，authored local 的 Z 为 `3.8074185795267113e-6` cm；原先从 Godot 半径/高度反算也会引入另一轮 float 米/厘米舍入。差异很小，但不是同一组原生输入。

## 实现

- 严格 primitive 编译器：剥离新增观察字段后必须与既有 runtime snapshot 完全一致，绑定模型/身体/形状身份；检查有限值、半径、原生 float 存储、轴、端点一致性及与 leaf bounds 的一致性。未支持偏心 box 时明确拒绝，不猜测中心。
- 所有实际资产 shape 的 registry 使用观察的 leaf-local 坐标。胶囊、球的 Godot 代理独立保存居中/转向变换；Jolt 返回的点和法向转换回原始 leaf 坐标后再交给 Core。
- Core 胶囊盒面算法新增原生 Endpoint0/Axis/Height/Radius 输入，直接使用它们；不从 Godot 尺寸或代理旋转重建。其相对盒空间段依照 UE `CollisionOneShotManifolds.cpp` 的 `FLineSegment3` 路径，由变换后的原生起点/轴和 double 提升后的高度构成。旧中心 Z 胶囊接口保留并调用同一实现。
- 盒体 Core 查询使用实际原生 half extents；球的实际半径与中心保留在绑定中，当前碰撞仍用 Godot 代理，未声称 sphere-box 原生窄相已实现。
- geometry trace 使用 `native_capsule` / `native_sphere` 标识原始 leaf 坐标与元数据，防止旧只接受 centered-Z capsule 的导出器静默重建错误形状。旧 capsule trace commandlet 尚未升级为处理新类型；后续原生实际 pair 对照须显式接入这些字段或绑定真实资产形状。

## 验证

产物：`artifacts/physics-primitive-geometry-20260922/`。

- Godot 优化构建通过。30/60/120 Hz smoke 各通过：新增 2 个原始 leaf/Core 和 Jolt fallback 点法向转换检查，保留 10 胶囊 cull、551 精度、9 几何、68 polygon、3 事务、5 流形、5 睡眠检查。
- Core Release 固定 JIT 串行 **2823** 通过，沿用两类 P5a 排除；Import 全量 **2413** 通过、1 既有跳过，退出 0。新增 primitive 定向与旧原生 capsule reference 共 6 测试通过；实际 primitive 数据导入并不等于所有实际形状 pair 的窄相输出已与 UE 对照。
- 十二项整链仍 **6/12**，通过项目没有新增/减少：高30、普通120、平移60/120、旋转60/120；普通30/60、高60/120、平移30、旋转30仍失败。未更改时间、睡眠和几何门槛。
- 高30两模型 M77/A88 帧睡眠，末秒速度/角速度 0；anchor 5.01766256 cm、末秒关节超限 .04978906 rad。较上批睡得更早，但这只是既有诊断预算通过。
- 普通60 M576帧、高60 M561帧才睡，未保持一秒。高120仍为 AnimMan 未睡。`final_speed` 是末秒最大值，不是睡后瞬时速度。
- 最后补全 native sphere trace 元数据绑定后，重新优化构建并运行高30带 geometry trace，报告 `final-high30.json` 与本批矩阵高30报告字节一致。

## UE 构建与兼容性

完整 Editor 目标及所有项目插件审计通过。fingerprint：`560EE185E4FD89A09118AB3C6FE3EB2690D2B90BB99B1F6B2A5EF1E12CC4A9F6`；日志前缀 `20260921T162454348Z-0ce5a3929a864e56871b04f359a3eb08`。

冷导及重复导出均退出 0。旧 `PhysicsRuntimeShapesOutput` 重导与既有 runtime JSON 字节一致，SHA256 仍为 `BC8E54EE27BFA7FA9258928B8005FD99FEBC14C165F7DD47A9E59232B08BED59`；没有重写旧资产。

DataValidation 退出 0、0 errors/3 既有 warnings。普通 Editor PID20896，exporter marker 成功，原生退出 0、DLL 已释放；两条旧 Condition failed 仍存在，不能据本次退出正常宣称间歇 AV 已修复。

下一步继续 sphere-box 完整窄相、胶囊盒边/深穿透/混合对及对应实际 pair 的原生参考，再六项整链失败与普通 Ragdoll owner、Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算。总体目标仍未完成。
