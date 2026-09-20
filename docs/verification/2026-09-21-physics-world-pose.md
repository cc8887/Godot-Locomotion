# Core 世界身体与 FBX 骨架姿态交接

主目录 `D:\GodotALS` / `main`，接续 `ea52319`。
完成普通角色接入所需的双向姿态/速度转换，并在实际平台上增加完整链生命周期回归。
**世界姿态交接通过；平台整体验收未通过，六项仅两项通过。普通 Ragdoll 尚未启用。**

## 坐标与状态所有权

此前 Core 关节回放把 native 身体直接转换成 FBX-local 形式，作为冻结代理的 GlobalTransform。
该方式能验证骨架层级，但世界的上轴/朝向与真实碰撞查询不同，不能用于普通角色。

新增 `AlsCorePhysicsPose`，消费已提交的 FBX local pose、Skeleton component-to-world、角色/代际身份，
生成 Core native **world** actor 身体状态；回程从 Core 世界身体生成指定 component 下的骨架 local pose。
没有身体的骨骼保留 seed local pose，跟随其父骨；有身体的骨骼保持自己的物理世界姿态。
该接口不读取 live Skeleton3D、不推进物理，也不改变胶囊。

两个基变换不能混用：

- native 局部点 → FBX：`(X,-Y,Z)`，cm 转 m。
- native 世界点 → Godot world：`(Y,Z,-X)`，cm 转 m。
- 世界骨骼 frame 需要保留 FBX 局部轴，所以在世界旋转转换后右乘固定的 `FbxToWorld` 旋转。
- 角速度是轴向量：Godot `(x,y,z)` → native `(z,-x,-y)`，不乘 100。
- 动态身体速度在 COM；输入 component 原点线速度加上 `angular × (COM-origin)`，再转换单位与基。
- 固定身体速度保持零，environment 数组后缀不由姿态接口覆盖。

所有 seed 输入先校验，再整体复制输出并更新 seed identity；非法末骨骼、错误代际或速度溢出不发布部分状态。
回写也先构建暂存 local pose，完成后统一复制。所有入口只允许 Main。
component 移动只改变输出 local pose 表示，不会移动 Core 的世界身体。

`AlsCoreJointHost` 新增显式 world-space 模式。
`PhysicsCoreJointReplay --scene-world` 使用该模式发布冻结代理，骨架重组后的世界姿态逐帧核对。
原 144 组 native-pair 仍保留原诊断坐标模式。两者共享相同 Core 求解，没有修改接触/关节公式。

## 实际资产交接验证

新增 `scenes/tests/physics_world_pose_smoke.tscn`。
两模型各四种世界位置与复合旋转，并对 spine/左上臂/右大腿施加非默认 local pose。
用既有独立 `AlsPhysicsBodySet.Seed` 的实际代理 COM 世界姿态作为对照，
同时比较直接 Core Capture 与代理 CaptureLocalPose，不能仅靠同一个转换函数的正反互逆宣称正确。

结果：8 组、160 次身体初始化、192 次物理步后回写（另有初始回写），32 次非法输入/Worker 拒绝。

| 指标 | 最大误差 |
| --- | --- |
| 世界/局部位置 | 5.8788432e-6 m |
| basis 向量 | 6.493369e-7 |
| 质心线速度 | 1.1920929e-7 m/s |

覆盖 component 平移/旋转后保持物理世界姿态、非物理骨骼 local 保持、环境后缀保持、失败 seed 不影响后续 capture。
最终产物 `world-verified.json/log`。
这里使用 actual imported rest + 明确的姿态扰动，尚未消费普通 demo 的实时已提交动画帧；不算普通 Ragdoll 接入。

## 完整链平台回归：明确保留四项失败

新增 `--platform=translate|rotate`，要求同时 `--chains --drop --sleep --scene-world`。
角色以横卧初始方向落到真实平台上，使用实际 4 m × 0.4 m × 1.49 m 形状；没有扩大平台或绑定角色姿态。
两角色仍在独立 island 中，共用平台观察输入，不处理彼此接触。

流程总长 24 s：前 10 s 保持平台静止，要求两角色自然入睡且保持至少 1 s；
随后 4 s 平移 1 m（0.25 m/s）或转动 0.8 rad（0.2 rad/s）；再停止 10 s。
运动期间要求唤醒、动态质量中心留在平台上、相对于平台刚性携带期望的水平滞后 <0.25 m。
末秒沿用 <0.1 rad 约束残差与自然休眠保持门槛。所有原生睡眠参数不变。

| 场景 | Hz | 结果 | 已观察的原因/指标 |
| --- | --- | --- | --- |
| 平移 | 30 | 失败 | 前 10 s 两模型均未入睡，尚未到运动阶段 |
| 平移 | 60 | 失败 | Mannequin 第 90 帧入睡，AnimMan 未入睡 |
| 平移 | 120 | 通过 | 前段入睡 M168/A793；停止后 A1718/M1721；最大水平滞后 0.008858 m |
| 旋转 | 30 | 失败 | 前 10 s 两模型均未入睡，尚未到运动阶段 |
| 旋转 | 60 | 失败 | Mannequin 第 90 帧入睡，AnimMan 未入睡 |
| 旋转 | 120 | 通过 | 前段入睡 M168/A794；停止后 M1745/A1914；最大水平滞后 0.001524 m |

M = Mannequin，A = AnimMan。120 Hz 平移/旋转的最大锚点误差分别为 0.09796778 / 0.09796736 cm，
末秒约束残差 0.00188366 / 0.00184101 rad，末秒速度为零。

60 Hz 平移第 600 帧 AnimMan 的 upperarm_r / lowerarm_r 平滑角速度为 0.0712296 / 0.0665805 rad/s，
超过原生 0.05 rad/s；相应平滑线速度都远低于 1 cm/s。
30 Hz 平移的两模型未入睡，AnimMan upperarm_r 在该采样为 0.0525712 rad/s。
这些是失败定位证据，尚未证明根因是接触流形、约束耦合还是原生缺失行为。
没有提升阈值、强制睡眠、延后启动时间或跳过失败。30/60 Hz 的移动响应本身仍未被此探针覆盖。

产物：平移 `platform-metrics-{30,60,120}.log`（只有 120 有成功 JSON）；
旋转 `platform-rotate-30.log`、`platform-rotate-first-60.log`、`platform-rotate-120.json/log`。
首轮 60 Hz 平移/旋转失败日志也保留。

## 既有场景回归与额外定位

Godot 优化构建 0 warning / 0 error。
旧 144 组 ×12 步 native pair 回放通过，位置/角度/速度差与前批一致。
旧 PhysicsBodySet 60 Hz flight/suspend/resume/stop/dispose 验证通过。
实际 World 的普通落地 60/120 Hz 仍通过，sleep 帧和物理误差与前批一致；
切换到正确的世界代理坐标没有改变 Core 轨迹。
本批只更改 Godot 交接/诊断代码，未重复执行未改动的 Core/Import 全量；前批结果不可当成本批的新运行。

新增最大锚点来源记录，复现了前批 120 Hz 普通落地的 6.708990 cm 瞬时误差：
**AnimMan，foot_l，第 54 帧（约 0.45 s）**。
父 connector native Z = -47.85517 cm，子 connector Z = -52.50561 cm，地面顶面为 -43.39840 cm。
这发生在早期碰撞阶段；仅凭 connector 位置还不能证明形状隧穿或具体求解错误。
后续应检查该帧前后的接触点/法向和投影阶段，不把它记作已修复。
产物 `scene-anchor-120.json/log` 保留原误差与来源。

全部产物在 `artifacts/physics-world-pose-20260921/`。

## 下一步

先针对横卧平台 30/60 Hz 休眠失败和第 54 帧 foot_l 瞬时误差补接触/关节阶段诊断及必要的原生整链对照，
保持当前失败作为修复门槛。同时把已经验证的 seed/capture 接到普通角色的已提交动画帧与明确的物理 owner 生命周期，
接入 pelvis 地面查询和胶囊/相机跟随，再完成退出、Get-up 与 Pose Recovery。
flail joint motor、初始速度限制、环境材质组合、接触图休眠、CCD 与性能预算仍未完成。
普通 demo 未切换，旧 Jolt 后端历史失败未关闭。
