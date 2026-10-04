# Lyra 最终 FootPlant Rig：真实 Godot 碰撞

2026-10-02；直接在主目录实施，无 worktree、提交或推送。本批不启动 UE，不修改其源码、配置或资产。继续使用 ALS 人物及 81 个逻辑通道。整个迁移目标仍进行中。

## 实现与原合同

`LyraGodotRigCollision` 提供实际 Godot 4.7.2 / Jolt `ShapeCast3D` 球扫掠，供 `LyraMainPoseHost` 的可选最终 ControlRig73 路径使用。查询在主线程物理阶段执行；Capture 冻结物理 tick、world space、组件变换、actor 下全部 CollisionObject RID 与 capture serial。另一次 Capture、组件移动、物理 tick 改变、owner/RID 集合变化或 Dispose 后，旧 Frame 均拒绝查询。此 Frame 不创建独立动画时钟；同一物理帧允许取消后重新捕获。

安装版 UE 源码 `RigUnit_WorldCollision.cpp:86` 起的实际 SphereTraceByTraceChannel 合同为：Rig 厘米坐标经组件转换到世界，sphere Radius 已是世界厘米且不随组件缩放，FQuat::Identity，忽略 actor，返回 ImpactPoint；ImpactNormal 经组件 `InverseTransformVector`，包含逆缩放且不在此处正规化。Godot 采用厘米→米 `(Y,Z,-X)*.01`、世界 identity query basis、Margin=0；先原位查询初始重叠，再执行移动扫掠。未命中保留原 Position=0、Normal=Up。正可逆非均匀组件缩放支持；反射及 shear 显式拒绝。

原 trace byte=2 由 UE 配置解析为 Traversable，不能直接当 Godot layer bit。构造器要求显式 UE channel→Godot mask；测试映射为 mask=4，同时 mask=1 的诱饵表面位于上方。actor 当前全部自身物理 RID 排除，包含仍在物理世界的排队删除子物体。

Jolt 的 rest contact normal 与 UE sphere sweep 的 ImpactNormal 合同存在差异。安装版 `CollisionConversions.cpp:115` / `:445` 对 Box 使用 `FindGeomOpposingNormal`，最终执行 Chaos `AABB.h:249` 的几何表面选轴。provider 从实际 PhysicsServer body/shape transform 取得 Box 轴，对投影超过原 1e-4 阈值的面选择最反向 trace 的法线；严格 `<` 保留 native XYZ tie 顺序，对应 Godot Z/X/Y。初始重叠保持原独立 MTD 路径，不执行此转换。其他几何目前使用 Godot 返回的 contact normal，尚未证明与 Chaos 通用逐值等价。

本地 `../godot` 为 4.8-dev（97dab7a638），仅用于定位实现。实际版本证据来自 4.7.2 API XML、实际运行日志及测试结果；不把本地 4.8 源码当成 4.7 的运行证明。

## 验证

新 `LyraRigSceneCollisionSmoke` 在真实 `_PhysicsProcess` 中执行三种 provider profile 的原观察输入，每组四秒，覆盖真实 30/60/120Hz，并断言 actual delta。实际地面平移、升降、斜坡旋转，actor 移动/旋转，组件倾斜和非均匀缩放，且部分时间关闭地面 collision layer。解析几何仅检查返回接触，不给 solver 注入虚构命中。

| 项目 | Debug | Optimize |
| --- | --- | --- |
| 最终构建 | 0 警告/0 错误 | 相同 |
| 物理帧 / 完整姿态 | 2520 / 2484 | 相同 |
| 每帧取消重试 | 2520 | 相同 |
| 实际命中 / 未命中 | 30744 / 2976 | 相同 |
| 几何检查 / 初始重叠 | 30798 / 9 | 相同 |
| 晚期组件失效 / 非法拒绝 | 21 / 2716 | 相同 |
| Locomotion 被真实 FullBody Slot 覆盖 | 2358 | 相同 |
| 实际部分 alpha / 关闭 | 468 / 387 | 相同 |
| Box opposing normal 转换 | 30744 | 相同 |
| 最大接触点平面差 | 3.762543201446533e-5 cm | 相同 |
| 最大 Rig 法线分量差 | 6.66742780985885e-8 | 相同 |

平面/法线检查门槛保持 .02 cm / 1e-4。逐帧取消对照完整 81 骨姿态、曲线、属性/root 通道及 Main/Rig 历史。21 次求值后移动组件使旧 Frame 失效，拒绝重新求值与提交，取消恢复后完整输出精确一致。边界还覆盖非物理阶段、worker、旧 tick/serial、异 epoch/channel、零半径、Disposed、组件/自体 reparent、新子 body、球边缘接触（同路径射线会漏）、Box 角点选轴、真实 Concave 两三角面及初始重叠。

Optimize 同时重跑原 Rig 完整输出：2520 帧/2154 姿态/174474 骨、10279440 比较/43080 通道检查，maxVector=2.842170943040401e-14 cm、maxRotation=2.220446049250313e-16。六个 Debug DLL/PDB 在 finally 中恢复，额外 verifier 再核对 backup 和实际文件 SHA256。旧 669 个 UE 包、808 份 JSON、原探针及外部 source/package 哈希保持。

## 失败历史

- 首次场景编译为 nullable candidate.Rig 警告升级错误；改为已由构造启用合同保证的非空访问，最终构建 0/0。
- 观察库存实际为 18 轨迹，初断言 9 错误；改为每个 profile/Hz 取首条的明确九组，保留首次失败。
- Concave 三角 winding 导致真实漏碰撞；修正 Godot 正面顺序。测试 detached StaticBody 未 Free 导致一项 RID/Object 泄漏；补显式 Free。
- scene transform 延迟发布，初平面误差 .1581979 cm / normal .01245794；夹具更新地面后调用 ForceUpdateTransform，保证物理 server 已收到实际变换。
- 接触点通过后法线差 .00024406794 超原门槛；检查原 UE 几何法线转换后补 production Box opposing normal，保持原误差门槛。随后补 native 角点 tie 与 actual physics delta 门禁，两最终配置均通过。

首次失败日志及中间成功报告均保留在 `artifacts/lyra-analysis/rig-collision-*`，不作为最终通过结果。最终证据为 `rig-collision-debug-build-final.log`、`rig-collision-optimize-build-final.log`、`rig-collision-scene-debug-final.log/json`、`rig-collision-godot-optimize.log`、`rig-collision-scene-optimize-final.json`、`rig-collision-verification.log` 和 `lyra-rig-collision-verification.json`；复核脚本 `tools/verify_lyra_rig_collision.py`，Optimize 脚本 `scripts/verify-lyra-rig-collision-optimize.ps1`。

## 范围与后续

此批关闭最终 FootPlant Rig 的真实 Godot 查询与 Main 候选事务边界。Main 输入仍取原受控观察，角色节点用于真实物理组件空间；没有把完整链路接普通 Demo，没有渲染/人工观感或性能验收，也没有新完整 UE Main 连续对照。测试 provider 的另一 FootPlacement 分支仍传 NoGround，EnableControlRig=false；不能称该分支已完成真实地面验证。原最终 Rig 不由该 EnableControlRig 开关关闭。

生产 `enableFinalFootPlant` 仍默认 false，Rig 保留 authored Manny 参考腿长。下一步建立显式 ALS 比例/控制 offset profile，接生产角色物理入口与模型输出，并验证普通 Demo；动态 provider 换类/多角色、Notify/root physics 消费、新整链 native、视觉和性能仍保持开放。
