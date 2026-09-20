# 原生整岛睡眠、唤醒与接触历史生命周期

本批在 `.` / `main` 实现可选 Core 整岛睡眠，并导出/回放真实 UE 睡眠轨迹。
原生对子与 Godot 简单身体生命周期通过；**两套角色均在十秒内入睡的整链验收仍未通过**。
普通角色 Ragdoll 尚未启用，本批不以基础算法通过代替完整落地稳定性。

## 原生规则与 Core 接口

对照 UE 5.9 `Chaos/Island/IslandManager.cpp` 的 `GetIslandParticleSleepThresholds`、
`UpdateParticleSleepMetrics`、`ProcessIslandSleep`、`PropagateIslandSleepToParticles`，
`ChaosCore/Private/Rotation.cpp` 的 `CalculateAngularVelocity1`，以及
`PBDRigidsEvolutionGBF.h` 的 `ResetVSmoothFromForces`。

实际导出确认：PartialIslandSleep=0，SmoothedPositionLerpRate=0.30000001192092896，
AngularSleepThresholdSize=0。两套角色全部身体的材质阈值为线速度 1 cm/s、
角速度 0.05000000074505806 rad/s、counter threshold 4、multiplier 1、MaterialSleep。
这些来自实际原生 particle/material，不把无材质的 CVar fallback 当作资产参数。

`AlsIslandSleep` 采用以下规则：

- 只让动态身体参与投票；任意 NeverSleep、两项阈值均零或任一速度超限，阻止整组入睡。
- 根据 actor X/P 的位移和 actor R/Q 的最短弧四元数差计算隐式速度；不是 COM 位移或最终冲量速度。
  以 double 平滑状态累积，保留 float particle quaternion 和 float 阈值平方边界。
- 每个身体的计数按原生方式饱和至 min(127, 自身阈值)，运动超限归零。
  整岛取最大的身体 counter threshold，所有身体都低于阈值时才累加；严格 `counter > threshold` 入睡。
- 入睡将动态身体最终 V/W 清零，保留姿态和平滑状态。
- 外部 acceleration / impulse velocity 使用原生固定 1/30 s 预估先更新平滑状态；重力不走这条外力预估。
- 先 Stage，全部身体、接触和睡眠验证成功才一起 Publish；失败不提前改变已提交睡眠状态或消耗 RequestWake。

`AlsJointIsland` 构造时可选配置每身体 sleep settings；默认仍保持原有清醒路径。
`Step` 的 `allowSleep=false` 表示该步世界侧禁止入睡，例如显式变换更新；这同时唤醒休眠组。
`RequestWake` 请求在下一成功步生效。新非零外力、重力改变、更换接触 owner 或几何失效也请求唤醒。
Reset 重置睡眠状态；身体瞬移后仍需同步重置接触历史。

此类针对调用方已经组装好的完整 group，不含世界岛发现/合并/分裂，不是孤立粒子专用计数算法，
也未实现实验性的 partial sleep/wake。运动 kinematic、未注册的场景动态身体、CCD 和完整运行时场景接入仍待完成。

## 接触生命周期

睡眠期间不调用 Gather/Solve，`AlsWorldContacts.CompletedSteps` 停止前进，因此旧锚点和观察 epoch 保留。
唤醒后的下一成功步继续历史，形状 revision / body generation 仍控制是否允许复用。
`AlsContactRegistry.ChangeVersion` 标记注册、替换、移除、身体复用和禁碰变更；无变化的禁碰设置不制造更新。
接触 owner 将已提交版本与当前版本比较，避免移除支撑后身体仍悬空睡眠。

Godot 查询资源 Dirty、丢失/旧绑定、资源释放会要求唤醒；Dirty shape 未重新绑定时仍明确失败。
已移除的 shape 即使旧 wrapper 发出 Changed，也不会永久阻止剩余世界睡眠。
不会在睡眠期间继续增加接触统计或执行窄相；代理发布保持姿态不变。

## 原生参考与对照

新增离线入口 `PhysicsSleepOutput=<新绝对文件>`，承接原有 fixed-parent 单关节原生场景。
144 组 × 60 步：两模型、两个关节、三频率、三轴、两方向、两 drive 模式。
第 31 帧通过原生 FBodyInstance.AddImpulse 施加 `(20,-10,30)` cm/s 速度增量。
导出每帧 awake、身体/岛计数、VSmooth/WSmooth、实际姿态/速度，以及全部 40 身体有效睡眠参数。

初始输入是显式 teleport 与 V/W 清零，原生第一步禁止 island sleep。
回放按这项已知输入语义令第一步 allowSleep=false，而非读取参考 awake 标志控制 Core。
之后的睡眠、保持和唤醒均由 Core 自身积分、姿态变化、计数和显式第 31 帧冲量决定。

144 组全部通过：**3300 个睡眠样本，85 次从睡眠状态施加冲量后唤醒**。
状态、每身体计数及岛计数逐帧一致。
最大位置差 1.021891e-6 cm、旋转差 1.788140e-7 rad、线速度差 1.051466e-5 cm/s、
角速度差 3.208264e-6 rad/s；平滑线/角速度范数误差共同上界 3.672974e-5（各自单位）。
这是无接触、固定父身体的实际原生轨迹，不外推为接触整链睡眠等价。

最终 `assets/config/v4_physics_sleep_reference.json` 为 **29,768,879 字节**，两次冷导出字节一致。
SHA256：`9D12170CAF8832890DCF31F91B9F42A3ABA6AF1177A392161F98D08E2ADFB46D`。
初版未带完整 rigs 的参考移到 artifacts/prototype-reference.json 保留，未删除其他用户资产。
`AlsSleepSettingsCompiler` 校验 mesh/PhysicsAsset/索引/bone identity、默认 sleep 模式与有限阈值，拒绝不支持的 partial/size-scale 参数。

## Godot 验证与未通过项

Godot 4.7.2 Mono `ed1daf0bf`，仍由 Core 积分、Jolt 仅查询几何。
`physics_core_contact_smoke.tscn` 新增五项真实查询检查，30/60/120 Hz 全部通过：

1. 球落在地面上自然入睡，保持一秒；pose/velocity、contact epoch 和窄相查询次数均不变。
2. 向上施加 100 cm/s 冲量，立即唤醒并积分。
3. 再次入睡后修改地面 resource，下一步拒绝旧绑定且身体/历史/睡眠均未部分发布。
4. Replace + Bind 后成功唤醒重试。
5. 再次入睡后移除地面，重力使身体重新下落。

原三场景动态响应与六项几何/主线程检查也通过，动态对动量差 0。
JSON 中 gravity/sleeping=false 描述原三场景；新增有重力的生命周期结果单列 sleep_checks=5。

完整角色使用 `physics_core_joint_replay.tscn --chains --drop --sleep --hz=...`。
门槛要求**两套**角色十秒内都自然入睡并各保持至少一秒；未修改原生阈值或加定时强制睡眠。

| Hz | 已入睡并保持的角色 | 十秒仍清醒的角色 | 末帧示例超限身体 |
| --- | --- | --- | --- |
| 30 | AnimMan，第 64 帧 | Mannequin | hand_l：平滑线速度 1.6284 cm/s、角速度 0.18388 rad/s |
| 60 | Mannequin，第 263 帧 | AnimMan | calf_l/r：平滑线速度 1.2085 / 1.2569 cm/s |
| 120 | Mannequin，第 486 帧 | AnimMan | spine_03：1.0672 cm/s；calf_l/foot_l：约 0.06194/0.06170 rad/s |

三轮整链睡眠验收均退出 1，失败日志完整保留，未生成成功 report。
已睡角色后续每帧 pose 和接触 epoch 不变；未睡角色仍通过前批的粗略落地/约束门槛，
但这些门槛不等于原生睡眠阈值。当前证据定位到持续低速运动，尚不能把根因归结为某一窄相或求解阶段。
下一步检查这些身体的接触点/法向、流形变化、共同迭代及原生稳定化差异，并补多帧观感验证。

## 构建、回归和故障记录

新增 Core 六项生命周期测试，通过原生阈值边界、整组阻止睡眠、失败唤醒事务、
支撑移除、重力/owner/Reset 变更和连续 2048 组睡眠/唤醒零分配。
首次地面单测误用了全零 `default` 材质（Stiffness=0），改为显式构造正常 stiffness 的材质后通过；没有改运行时来迁就测试。
Import 新增一项 144 组原生回放及三项全身体参数编译/拒绝测试，均通过。
Core Release 固定 JIT 全量 **2689 通过**，保留既有 P5A Golden/TraceSchema 过滤。
Import Release 固定 JIT 全量 **2363 通过 / 1 既有条件跳过**。Godot 优化构建 0 warning / 0 error。
原有 144 组清醒关节对子在 Godot 回调中仍通过，数值与前批一致。
旧 12 步关节参考重新冷导出，和已提交文件字节一致；SHA256
`18243B9F6F948C65216D73DC17FF95DD5C8E6422E508C58FF87811819464D0FD`。

使用 `ue-diagnosing-plugin-build-load` 完整构建 Editor 目标、闭包审计、冷导出、普通 Editor 重启和 DataValidation。
扩展全身体参数时首次编译因 TObjectPtr 容器不能推导 const auto* 失败，显式 USkeletalBodySetup* 后全目标重建通过。
最终 BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`；fingerprint
`5A0AD5165BE2BA56C3CEE6EAFD5D6E45A5FD0794B8341F8D94D16D85537CD221`；
UE PluginBuild 日志前缀 `20260920T221932113Z-847590e26f6a41b28d5ef3e584c4fef6-*`。
主仓库与 UE 项目 exporter 三个变更文件哈希一致。
普通 Editor 输出 `ALS_SLEEP_EDITOR_RESTART_OK`、退出 0；仍有既有 UnifiedErrorTest/Condition failed，不能称启动日志无错误。
DataValidation 退出 0，0 error / 3 既有 warning。Editor-only 导出工具无适用的仓库打包要求，未打包。
技能引用的两项 superpowers 技能本机缺失，采用直接源码/构建审计/运行验证，没有因此暂停工作。

日志与报告目录：`artifacts/physics-sleep-20260921/`。
原生最终为 `export-rigs-*`、`export-repeat-rigs-*`、`native-rigs-replay.log`；
整链失败为 `sleep-{30,60,120}.log`；简单生命周期通过为 `contact-sleep-{30,60,120}.log/json`；
其余为 `core-full.log/core.trx`、`import-full.log/import.trx`、`godot-pairs.log/json`、
`editor-restart.log`、`data-validation.log` 和各轮构建/初次失败日志。

普通 demo 本批未切换；Ragdoll、Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算仍未整体验收。
原 Jolt 整链失败也未关闭。保持总目标，继续完成接触稳定性及真实场景接入。
