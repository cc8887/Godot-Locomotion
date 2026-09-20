# Godot 物理体与姿势所有权基础

本批基于 `66ff396`，直接在 `D:\GodotALS` / `main` 实现。
新增 `AlsPhysicsBodySet` 与实际引擎 smoke 场景，尚未连接关节或普通角色的
Ragdoll 状态。当前是物理体/姿势边界实现，不能视为完整物理骨架或 P6 验收。

## 实现范围

- 按实际导入 FBX 骨骼名称绑定两套原始 PhysicsAsset：共 40 个刚体、43 个
  碰撞形状，支持一个 body 的多个 shape、sphere / box / capsule / convex。
- UE cm 转 m，惯量 kg·cm² 转 kg·m²；Godot body 使用原生质量主轴坐标系，
  shape 相对该坐标系重定位。保留旋转后的完整惯量张量，COM 设为 body 原点。
  Capsule 轴向与圆柱段长度、AnimMan 足部凸包非均匀缩放均显式转换。
- 原生质量、重力开关、线性/角阻尼、材质摩擦/反弹系数及显式碰撞排除对被消费。
  Godot 的材质组合、睡眠、solver 配置不等同于 Chaos；这些语义尚未全部适配。
  非零 UE RestOffset 明确拒绝，不能当成 Godot margin。当前原生资产均为零。
- Main 接受角色/槽位代际/帧身份及完整局部姿势值；拒绝错误代际、无效变换、
  非单位四元数和活动中重新 Seed。避免读取 Worker 正在修改的 Skeleton。
  提供 Seed / Start / Suspend / Resume / Stop / Dispose；继承线速度、角速度
  及角速度产生的各 COM 切向速度。TopLevel 根节点隔离父节点移动。
- 物理姿势反算完整目标骨架的局部姿势；无物理体骨骼保留 Seed 局部变换，
  跟随当前父骨骼。停止清除姿势身份，Dispose 可重复调用。
- root 保持原生 Kinematic；其他 body 动态。尚未构建 root-pelvis 或其他关节，
  未将自由 root 错误固定到 pelvis。

## 已修复的暂停问题

启用接触报告后，动态 body 切换到 Kinematic 暂停可回放旧目标变换。
核对当前引擎提交 `ed1daf0bf` 的
[GodotBody3D 实现](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/godot_physics_3d/godot_body_3d.cpp)：
切换模式时可能保持活动，而后续运动学积分消费 `new_transform`。
暂停现在使用 Static freeze；真正 Kinematic root 在启动时显式初始化目标变换。
最终回归在接触报告容量为 8 的情况下，暂停期间位置不再变化或跟随父节点。

使用 agent-reach 的 GitHub 读取流程核对实现；本机 gh 未登录，改用公开原始
源码只读访问，没有要求用户配置认证。另核对
[Shape3D 文档](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/doc/classes/Shape3D.xml)
确认 margin 不是 UE RestOffset，且 GodotPhysics 不使用该属性。

## 验证结果及未解决问题

Godot 4.7.2 Mono `ed1daf0bf`；优化构建 0 警告、0 错误。
最终日志在 `artifacts/physics-bodies-20260920/final-*.log`：

| 实际引擎用例 | 30 Hz | 60 Hz | 120 Hz |
| --- | --- | --- | --- |
| 无碰撞飞行、质量惯量、姿势回传、生命周期 | 通过 | 通过 | 通过 |
| 薄地板接触，保持原始形状与物理参数 | 失败 | 失败 | 通过 |

几何最大位置误差 `2.79324922E-05 m`（约 0.028 mm）；参考姿势回传最大
位置误差 `7.58202191E-07 m`；实际后端世界逆惯量张量最大相对误差
`2.32537801E-07`。质量从 PhysicsDirectBodyState 的逆质量独立检查。
还检查碰撞排除集合、无效输入不改变所有者、重复启动拒绝、角速度继承、
暂停/恢复的速度保持、父节点移动隔离、停止后重 Seed 与重复释放。

地面夹具是 100×0.2×100 m 的静态 box，顶部 Y=0，body 从实际参考姿势
向上平移 3 m 后带 2 m/s 水平速度落下。仅与地板碰撞，隔离体间碰撞与关节；
观察总计 4 秒，含 0.5 秒暂停。检查所有动态碰撞几何最低点在
`(-0.02, 0.03) m`、速度小于 `0.2 m/s`，没有放宽断言来通过测试。

- 初始仅采用原生 CCD=false 时，30 Hz 有体穿过地板。增加显式 Godot CCD
  策略（默认开启、可配置）后，该用例不再穿过整块地板。这是后端适配，
  不是原生 UE CCD 配置；原始导出值未改。
- 最终 30 Hz 的 Mannequin 左手仍有 `0.29535574 m/s` 残余速度；60 Hz
  的 AnimMan 左手最低点为 `-0.03283312 m`。低 Hz 接触稳定性未解决。
- 延长至 10 秒、迭代增至 64、取消反弹、改变地板尺寸/改平面、将 box 改为
  等几何凸包，均未消除失败。取消阻尼或摩擦的诊断也未通过，但不据此把
  阻尼/摩擦排除为所有问题的因素。诊断日志保留；正式 body 参数没有采用
  这些试验修改。`--seconds`、`--iterations`、`--floor`、`--diagnostic`
  只属于 smoke 的对照入口。
- 临时 `override.cfg` 对照 Jolt：30/60 Hz 锁骨接触深度略超 2 cm，120 Hz
  仍有小腿未稳定的失败。测试后删除本批临时文件，项目保持原物理后端；
  未将“换后端”宣称为修复。

复现（在主目录、完成 Godot 构建后）：

```powershell
& 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path D:/GodotALS res://scenes/tests/physics_body_set_smoke.tscn -- --hz=60
# 添加 --contact 可复现当前 60 Hz 接触失败；--hz=120 --contact 为通过对照。
```

本批没有修改 Core/Import 算法或原始 JSON，未重新运行它们的全库测试；
不把此前证书写成本批结果。未进行整角色视觉验收、体间碰撞/关节稳定性
验收或十分钟性能测量。用户 P4 计划的未提交修改保持原 SHA256 不变。

## 后续顺序

先收敛低 Hz 接触稳定性与后端策略，再消费原始关节 frames、自由度、软限制
和驱动设置；Godot 内置 joint 参数不能直接视为 Chaos 等价物。随后把实际
Commit 姿势与角色速度接给 Main 物理所有者，处理 capsule/物理体切换、骨盆
追踪，再接 Ragdoll 触发、Get-up / Pose Recovery 及 Overlay 道具生命周期。
Mantle、完整 Camera、地形/起停/换髋联合观感和最终性能预算仍在目标范围内。
