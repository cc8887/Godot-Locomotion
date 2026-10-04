# Lyra 最终 Rig：显式 ALS 参考配置

2026-10-02。直接在主目录实施，无 worktree、提交或推送。模型、材质与原蒙皮保持；本批新增外部只读 UE 探针及显式 Godot 目标参考绑定。未编辑 UE 引擎源码或 GASP58 资产/项目描述；两次成功采集均实际退出0且资产保存数为0。整个迁移目标仍进行中。

## 目标参考绑定

原 Main73 `bSetRefPoseFromSkeleton=false`，Construction 使用 Manny Rig 的参考腿长45.752037048339844 / 41.705421447753906 cm。重定向动画不会自动修改它们。

本批新增 `LyraFootPlantRigReference.AuthoredRig / AlsCompactReference` 与不可变 `LyraFootPlantRigReferenceProfile`。`LyraFootPlantRigPoseHost` 构造时可显式选择目标参考；`LyraMainPoseHost` 把同一配置传入最终 Rig，未启用最终 Rig 却选择目标参考、或非法 enum 均拒绝。默认保留 authored 配置，现有原生 oracle 继续验证原行为；后续生产角色入口须显式选择 ALS。

依据安装版 `ControlRigHierarchyMappings.cpp:43`，原 `bSetRefPoseFromSkeleton` 的实际调用为 `UpdateControlRigRefPoseIfNeeded` → `SetBoneInitialTransformsFromCompactPose`。后者在 `ControlRig.cpp:3828` 按 imported bone 名匹配目标参考骨，将参考姿态写入 **InitialLocal**，保留 Rig 原父拓扑及 unmatched 骨，不把所有目标骨转换成 global。RequestConstruction 随后重置/执行原 Construction，由原 VM 计算足偏移、腿长及 Body/Pelvis/Chest 控制 offset。此次显式目标配置采用这一原生可选路径，不手工缩放腿长常量或覆盖已导出的 authored JSON。

实际原 Rig 为91骨、7 Transform controls：89骨 imported，`ik_ball_l/r` 两骨为 created；69个骨名匹配ALS81，22个不匹配目标的骨保留原参考，包括上述两自建骨及 Manny 独有脊柱/扭转骨。目标配置不改变已验证 PoseAdapter 的局部输入、父空间标记、部分alpha或输出规则。它是参考绑定配置，完整缺骨/脊柱/握持适配仍需后续姿态和视觉验证。

运行 policy 固定四个依赖哈希（目标 logical calibration、Rig graph、runtime program、control settings），并校验 bank.CalibrationSha256、骨身份与69/81映射。mutable hierarchy 只属于角色候选；目标InitialLocal先写入初始owner，Prepare在克隆上Reset/Construction，取消不发布初始化和求解历史。中途初始化沿用已绑定的目标初始姿态。

## 原生与 Godot 验证

新 `AlsLyraRigReferenceLibrary` 在 transient Rig 与 transient ALS weapon skeleton 上调用实际 public AnimNode 参考绑定桥；setter 为 private，因此不使用访问绕过或引擎源码修改。采集 authored/target 两组完整层级，包含 before、afterSetter、afterReset、afterConstruction、afterRepeatedConstruction 及四个Construction变量。随后独立UE进程逐值比较原捕获，保持原文件字节。

目标Construction输出：

| 项 | 原生目标值 |
| --- | --- |
| ThighLength | 42.57203674316406 cm |
| CalfLength | 40.19668960571289 cm |
| LeftFootOffset | (17.07627164309043, -8.07212722477947, 0) cm |
| RightFootOffset | (-17.076288769077756, -8.072148419019072, 0) cm |

Godot的参考绑定实现读取目标bank.Reference和policy，随后执行现有完整VM Construction。测试期望来自独立原生捕获；生产宿主不读取 native 期望输出。

| 门禁 | Debug | Optimize |
| --- | --- | --- |
| 最终构建 | 0 警告/0 错误 | 相同 |
| 两profile完整层级 | 98元素、4200组TRS | 相同 |
| 最大位置/scale/四元数分量差 | 全0 | 相同 |
| clone重复Construction / 非法配置 | 2 / 2 | 相同 |
| 目标Main真实物理 | 三profile×30/60/120Hz，共2520帧、2484完整姿态 | 相同 |
| 取消重试 / 初始化 | 2520 / 18 | 相同 |
| 实际命中 / 未命中 | 30744 / 2976 | 相同 |
| 晚期组件失效 / 非法拒绝 | 21 / 2716 | 相同 |
| Slot覆盖 / 部分alpha / 关闭 | 2358 / 468 / 387 | 相同 |
| 最大接触点平面差 / 法线分量差 | 3.762543201446533e-5 cm / 6.66742780985885e-8 | 相同 |

真实物理继续执行上一批全部球查询边界及完整Main候选事务，新增每组运行中途重新初始化。18次初始化均含取消后重试；地面实际升降/斜坡、组件非均匀缩放及21次求值后组件改变保持原门禁，平面/法线阈值仍为 .02cm / 1e-4。

Optimize还运行原authored Rig完整输出：2520帧/2154姿态/174474骨、10279440比较/43080通道检查，最大vector2.842170943040401e-14cm、quaternion2.220446049250313e-16。运行结束六Debug DLL/PDB恢复并再次SHA256核对。669个UE包、813份旧JSON和原探针源/原canonical source/package保持哈希。

## 构建和失败证据

- 第一外部package路径超过Windows260字符限制，构建退出6；改为短路径`../GLRigRef`，保留长路径失败目录和日志。
- 短路径首轮76动作构建遇private setter访问错误，退出6；改用实际public `FControlRigHierarchyMappings` bridge，增量4动作（compile/lib/dll/metadata）成功。最终 DLL BuildId=55116800，与安装版引擎相同。没有把这一结果称为引擎全量Editor重编；UBT执行了目标metadata写入。
- 首次UE读取错误假定91骨均imported，被实际`ik_ball_l` created标记拒绝。明确89 imported/2 created并保留两自建骨，最终两个采集退出0。两成功日志保留已有工具注册/DSL映射等warning，不称UE零warning。
- 首次Godot比较将外部double长度直接拆箱为float，测试退出1；改为double精确比较原变量，两个最终配置全0差。生产Rig数学未改，TRS门槛仍为1e-8cm / 1e-10。

新探针在独立外部`artifacts/unreal/gasp58-lyra-rig-reference/source`及`package-ready`，不覆盖原`gasp58-lyra-masks`包。BuildPlugin短路径首次失败、bridge成功及UAT/UBA日志归档均保留。

不可变捕获`rig_reference_v1_native.json`（919885字节），SHA256 `feae2406be8327f7ce0bafe91aa1560583163ce7164cad51a9bae16b2e3654f3`；runtime policy5633字节，SHA256 `f617d206f5477dbbea4dc866b468c3adadd2e08778023846adb389b919bd52e8`。最终证据位于`artifacts/lyra-analysis/rig-reference-{ue-fixed,ue-repeat,godot-debug-final,scene-debug-final,godot-optimize,verification}.log`、`rig-reference-scene-{debug-final,optimize}.json`及`lyra-rig-reference-verification.json`。复核脚本为`tools/verify_lyra_rig_reference.py`，Optimize脚本为`scripts/verify-lyra-rig-reference-optimize.ps1`。

## 尚未关闭

此批证明目标参考绑定、完整Construction层级和实际Main/Jolt执行的上述范围。没有新目标配置完整ForwardSolve或整Main连续UE oracle，未接普通Demo，没有模型渲染/人工比例/脊柱/握持视觉验收或性能验收。保留原Rig拓扑的缺骨语义须按后续目标姿态对照和实际画面评估。

provider另一FootPlacement真实地面分支仍为上一批NoGround测试输入，Chaos/Jolt通用接触逐值等价未证明。完整目标仍包括生产角色入口、普通Demo、provider换类/多角色、统一Notify/root physics消费、原生整链与视觉/性能。下一步补目标配置连续原生姿态对照，接生产模型最终发布与普通Demo。
