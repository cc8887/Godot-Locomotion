# 物理接触后端与普通运行回归

本批在主目录 `.` / `main` 基于 `cd3053e` 推进。
上批独立刚体在 GodotPhysics 的低 Hz 落地失败，现改用项目级 Jolt Physics，
并设置 `physics/jolt_physics_3d/simulation/penetration_slop=0.002`。
没有改变原始资产、碰撞几何、质量、惯量、阻尼或材质系数。
这是一项 Godot 后端适配，不代表复制了 Chaos 求解器。

## 依据与接触结果

实际引擎 ProjectSettings 查询确认，Jolt 默认 penetration_slop 为 0.02 m。
上批 Jolt 对照锁骨约 2 cm 的穿入，与该默认尺度一致；设置成 2 mm 后，
30/60 Hz 原四秒落地检查通过。读取 PhysicsServer 的通用
ContactMaxAllowedPenetration 得到的 0.01 不是 Jolt 的该设置，诊断现在同时
打印两个值，避免混淆。

GodotPhysics 的额外低 Hz bias=0.1/0.2/0.4 对照没有解决问题，低 bias 反而
导致颈部持续下沉；这些值没有加入正式形状配置。此前增加迭代、等待更久、
换地板几何的失败记录仍保留，没有将旧后端问题宣称为已修复。

120 Hz Jolt 的小腿在四秒时仍摇摆（速度约 0.3583 m/s，最低点接近地面），
十秒时已稳定。这与旧后端十秒后仍持续抖动不同。为检查是否真正稳定，
smoke 默认改成十秒观察，最后整整一秒每个物理 tick 都检查所有动态形状，
而非只检查四秒时的一个瞬间。最低点仍必须在 (-2 cm, 3 cm)，速度仍必须
小于 0.2 m/s；没有放宽空间/速度阈值。原四秒瞬时失败不能写成四秒通过。

| 用例 | Hz | 最后一秒动态体观测数 | 最低点最小值 | 最大速度 |
| --- | ---: | ---: | ---: | ---: |
| 原始落地 | 30 | 1140 | -2.002 mm | 0 m/s |
| 原始落地 | 60 | 2280 | -6.558 mm | 0 m/s |
| 原始落地 | 120 | 4560 | -2.000 mm | 0 m/s |
| 高速倾斜落地 | 30 | 1140 | -5.152 mm | 0 m/s |
| 高速倾斜落地 | 60 | 2280 | -2.523 mm | 0 m/s |
| 高速倾斜落地 | 120 | 4560 | -1.902 mm | 0 m/s |

高速用例 `--high-drop` 从 10 m 起始，继承水平 2 m/s、向下 12 m/s，
模型 Euler 倾角 (0.5, 1.1, 0.2) rad。两套资产都执行质量/主惯量、几何、
暂停恢复及姿势回传检查；最大惯量相对误差约 1.16e-6，几何误差约 0.028 mm。
2 mm 是求解器容许穿入参数，不是“任何睡眠姿势都不超过 2 mm”的承诺。
这些用例隔离了体间碰撞和关节，不能代替完整 Ragdoll。

## 后端切换兼容检查

普通动画运行代码没有因这些测试而调整输入、方向或锁脚算法。
两个旧夹具存在后端假设，现修正为实际行为检查：

- P3A 的 AnimatableBody 以前只填 ConstantAngularVelocity，没有旋转变换。
  [当前 Jolt 实现](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/objects/jolt_body_3d.cpp)
  的运动学更新由实际新旧变换产生速度。夹具改为每 tick 实际旋转 2 rad/s，
  等同步生效后仍严格检查报告角速度为 (0,2,0)；没有取消角速度断言。
- FootGather 的静止平台在 Jolt 下通过复用的 TestMove 探针确认支撑，
  无 slide wrapper 分配，原“必须分配内存”的断言错误。现在实际采用 slide
  证据时仍要求计入 wrapper 分配；复用探针允许零分配，真实射线字典的正分配
  断言保留。平台身份、台阶边界、多碰撞体、销毁/普通离开判别继续检查。

这两项夹具在 Jolt 与 GodotPhysics 都复验通过。FootGather 在 Jolt 报
`bytes=0 slide_evidence=False`，GodotPhysics 报 `bytes=408 slide_evidence=True`。
临时后端对照 override.cfg 已移除，正式设置仅位于 project.godot。

本批优化构建通过（0 warning / 0 error）。Jolt 下通过：

- P3A Motor 全场景；实际平移/旋转平台 Motor。
- FootGather 生命周期；FootPlacement 的平地、坡地、台阶、平移/旋转平台、
  跳跃、换基底、传送及两次预期故障回滚。
- 完整生产动画链路旋转平台 360 帧，锁定目标最大误差约 4.75e-6 m。
- 普通键鼠回放 360 帧：Alt 180、左右移动 150/120、鼠标事件 4、脚趾约束 83。
- Roll gameplay 420 帧，两次接受/完成；落地动作 104 帧，三次落地与取消路径。
- 十角色单线程/并行各 3621 帧；2 次取消、1 次 Commit hold，50 次动作接受、
  20 次替换、20 次取消、10 次完成，Overlay 2400 帧。摘要一致：pose
  `8F892C1CE59B9B6E`、root `D915E83847A64B58`、result `9B94C40FE7D6B208`。

完整平台图首轮启动遗漏 `--foot-ik-frame`，被入口前置条件拒绝；补齐完整
诊断参数后通过。FootPlacement 的两个 worker 错误是夹具主动注入的故障，
最终回滚断言通过。原失败日志均保留。

日志位于 `artifacts/physics-bodies-20260920/`：`jolt-policy-*`、`jolt-high-*`、
`jolt-fixed-*`、`jolt-final-*`、`godot-fixture-*`、`jolt-dispatch-*`。未重跑未修改的 Core/Import
全库，未宣称整角色人工观感或最终十分钟预算已验收。

## 下一阶段的关节边界

两套资产总计 38 个原生约束，包含非对称 swing cone、twist 的锁定/限制、
软限制、TwistAndSwing 位置/速度驱动、质量调节和投影。
root-pelvis 的各自由度均 Free，不能为了保持模型完整而固定它。

核对当前 Godot 的 C# PhysicsServer3D API 和
[Jolt Generic6DOF 实现](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/joints/jolt_generic_6dof_joint_3d.cpp)：
常规 6DOF 使用 Pyramid swing；不是原生椭圆锥限制的等价实现。部分 Godot
softness/damping/restitution 参数在 Jolt 下无效。下一阶段必须逐项适配并
做受力/关节极限对照，不能只填入角度后宣称完成。随后继续 Main 物理所有权
与真实角色接线、Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和性能预算。
