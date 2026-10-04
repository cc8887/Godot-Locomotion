# Lyra Align / SkewWarp 原配置与连续组件对照

后续指定四模板已接真实 ALS 角色物理边界，实际 bank context（Seek 除外）、typed 目标/销毁、单次胶囊移动及同帧 retry 通过两构建三Hz/GPU门禁，见 [真实角色验证](2026-10-02-lyra-motion-warping-physics.md)。默认已读 Shooter 配置不补组件/Align；原 GA 和完整 Main/世界物理原生验收仍开放。本文未接物理的描述保留为该批历史范围。

2026-10-02。直接在主目录推进。原四个 MW Montage 的窗口、目标、modifier 历史和 SkewWarp 算法已实现为可取消的 Core 组件，继续使用 ALS 68 skin / 81 logical 资源。**本批关闭指定原资产配置下的组件对照；尚未接角色真实胶囊更新，完整 Main 连续 native 和整个 Lyra 移植目标继续开放。**按用户要求忽略 UE 5.8/5.9 的小差异。

## 原玩法与资源调查

两独立 UE 进程读取原 `GA_Emote` 图及四个原 Notify 对象的完整属性，退出 0、内容相同、资产保存数 0。补原 CDO 和 EventGraph 原始 pin/连线读取，发现变量报告中的 `Montage to Play` 默认值为空并不代表实际值为空：原 CDO 实际绑定 FingerGuns MW，PlayMontageAndWait 输入连接该变量。原图还在蹲伏时 UnCrouch，并在 OldVelocity 大于 0 或再次蹲伏时 StopAnimMontage；这些 GA 行为仅作为证据，本批没有激活原技能。

在完整同步扫描 AssetRegistry 后，FingerGuns MW 的包引用包含 `/ShooterCore/Game/Emote/GA_Emote`，其余三段武器 MW Montage 没有包引用。该引用调查不代表任意动态加载或外部代码均不存在。原 GA 图没有设置 `Align`；原 Shooter Pawn 的 SCS 没有 MotionWarping 组件。另两独立进程读取 ShooterCore GameFeatureData：四个 action 中的 AddComponents 有七条组件注入，未包含 MotionWarping。本结论限这些已读取的配置，不能据此宣称所有 Experience / GameFeature / 运行实例都不可能添加组件。默认玩法不应凭空创建一个对齐目标。

| 原 Montage | 窗口 EndTriggerTime | WarpPoint provider |
| --- | --- | --- |
| MF FingerGuns Emote MW | 0.3568413555622101s | None |
| MM Pistol Reload Emote MW | 0.6692352890968323s | Static / identity |
| MM Rifle Reload Emote MW | 0.5888612270355225s | Static / identity |
| MM Shotgun Reload Emote MW | 0.8453130125999451s | Static / identity |

四条窗口原 TriggerTime 均为 -9.999999747378752e-5s，组件扫描时按原资产长度 clamp 到 0。统一配置是 Align、Linear 加位移、IgnoreZ、FeetLocation、Default/Slerp 旋转、rotation time multiplier 1、rotation max rate 0、translation max speed clamp 0；无 remaining-root subtraction、额外旋转或自定义 easing curve。原窗口结束时的 absolute root 另由原 MotionWarpingUtilities 读取，均为 identity。Static 缓存计算产生的正规化舍入（包括 W=1.0000000000000004）保留。

## 执行模型

原 CharacterAdapter 从本帧根 Montage 读取 Animation、Previous/CurrentPosition、weight 和原带 RateScale 的 play rate。MotionWarpingComponent 在 local→world 转换前直接扫描 Notify 窗口，以 PreviousPosition 是否落入区间决定创建 modifier；不等待普通 NotifyQueue。该原组件的 segment search 默认为 false。

Core 组件 `AlsMotionWarpingRuntime` 保留窗口创建顺序和 modifier 身份、Waiting/Active/Disabled/MarkedForRemoval 状态、播放参数、ActualStartTime、StartTransform、原 total-window motion、Static warp-point offset、缓存目标及两种 pause。缺少目标会永久禁用当前窗口；迟到目标不会自动重新启用。目标变化按原容差重置 ActualStartTime 和当前 visual root。只有实际提取到 root 时才执行 component hook；否则保留已存在的 modifier，不能擅自以空 context 清空。跳出窗口或切换动画的清理沿原 Update 规则执行。

位移使用原 in-place 分支：binary32 alpha、Linear 插值、忽略 Z、当前 actor 朝向及 mesh base rotation offset 转换；旋转使用原播放速率修正的剩余时间和 Slerp。具体 root 查询复用既有压缩 root bank，通过 typed `IAlsMotionWarpingRootSource` 读取第一轨实际区间，没有另建时钟。当前组件明确实现本套原 Align/in-place 配置，不宣称通用 Bone provider、任意旋转方式或任意有 authored translation 的 SkewWarp 支持。

目标命令、modifier 数组和序号作为帧候选保存。Cancel 不修改已提交历史；重试得到逐值相同的输出和 modifier；旧候选、跨 runtime、重复 Begin / Commit 与旧 frame 拒绝。Source 提取失败不会消耗序号或留下半准备帧。

## 原生参考与最终验证

新增独立可选 `LyraMotionWarpingOracle` 模块。临时真实 ACharacter、CharacterAdapter、MotionWarpingComponent 和原 Montage/Notify modifier template 执行本帧 Montage Advance/Consume，再经实际绑定的 CMC pre-convert delegate、原 mesh 世界转换。角色下一位置/旋转手动积分；没有运行 Chaos/CMC 全世界物理或整个 Main 动画图。

四资产 × 30/60/120Hz × 十模式，共 120 条两秒轨迹 / 16,800 帧。十模式为正常目标、改目标、删目标、先缺失后添加、Stop/重播、非根 Montage 替换/重播、seek 出窗口、反向、warp/root 分别暂停，以及 DisableAll。包含 mesh 0.7/1/1.4 缩放、17/-9/-90cm 偏移和不同 actor/mesh yaw。参考每帧记录实际 context、原 local root、warped local、world motion 和完整 modifier 快照。

| 门禁 | 最终结果 |
| --- | --- |
| 只读原配置 | 两 policy 进程退出 0；四窗口、原 GA 图 / CDO / pins 及 original window-end root 保持 |
| 原组件连续参考 | 两最终 UE 进程各 120 轨迹 / 16,800 帧，退出 0、结构逐值相同；3,030 非零 warped translation 帧 |
| 原 modifier 历史 | 3,662 Active / 1,289 Disabled 帧；身份、状态、顺序、float 时间/weight/rate、起始姿态、缓存目标与 offset、pause 均逐帧比较 |
| Debug / 实际 Optimize | 每构建 16,800 帧对照和同数 Cancel/retry；50,400 次旧候选 / 重入 / 重复提交拒绝；完整结果相同 |
| 精度 | 最大 position L2 5.859285502108464e-14cm，quaternion L2 4.577566798522237e-16，scale L2 0；门槛分别 1e-8cm / 1e-10 / 1e-12 |
| Core Release | 193 项通过、0 失败 / 跳过；含新增五项目标取消、禁用后迟到目标、无 root 保留历史、提取失败及跨 owner / 重放门禁 |
| 既有回归 | 两构建各原 root 8,564 帧、真实六角色 60Hz 2,880 移动 / retry、五 Jolt 非零人工案例 / 迟到帧拒绝、Montage 15,870 采样、武器 5,040 帧及真实角色 / 回调生命周期通过 |
| 普通十角色 | 每构建 4,800 胶囊移动 / 人物发布；完整报告与前批 root-movement-final6 冻结基线逐值相同，`motionWarpingConsumer=false` |
| 构建 / 运行 | Debug 与实际 ExportRelease Optimize 构建均 0 错误 / 警告，各六进程退出 0，无 Godot ERROR/WARNING；六 Debug DLL/PDB SHA 恢复 |
| GameFeature 调查 | 两独立进程退出 0；ShooterCore 四 action / 七组件注入读取相同，未激活 feature |
| 资产保护 | policy 保护 847 旧 JSON / 707 原包；runtime 保护 848 JSON / 708 原包；feature 调查保护 851 JSON 及原 ShooterCore 包；项目和 Config 字节保持、0 资产保存 |

汇总 `artifacts/lyra-analysis/motion-warping-verification.json`，矩阵日志 `motion-warping-{debug,optimize}-gate-final.log`。新五个 `motion_warping_v1_*.json` 为被忽略的运行/参考资料，旧资产未统一格式化。临时插件构建结束即移回 `artifacts/unreal/lyra-motion-warping-oracle/package-full`，没有保留在 GASP Plugins，自己的 module manifest 使用当前 host BuildId。

首次未等待 AssetRegistry 完成，得到的空 referencer 列表证据不足；该次三份数据逐 SHA 移入 `artifacts/lyra-analysis/motion-warping-native-first-incomplete-registry`。完成扫描后重新采集，原 16,800 帧 runtime trace 与 requests 仍逐值相同，最终两进程都采用完整扫描。C++ helper 名称被 UObject::Serialize 隐藏和首次 Core Math 命名空间编译失败日志也保留；后续修正未放宽误差门槛。

## 下一步与边界

本次是原组件算法加记录的原 context / adapter 输入对照。既有物理回归运行真实 Godot 场景，但其中没有调用新 SkewWarp，不能作为新组件已接生产物理的证据。本批没有新增 GPU / 人工观感验收。

下一步把读取原窗口的资源 profile 与实际根 Montage context 接入角色移动前的 hook，使用真实 ALS component、当前胶囊 feet、初始 base visual offset 和 typed 目标命令。非零 warped motion 要在一次 MoveAndSlide 和物理发布后重试边界下验证，覆盖碰撞、目标更新/丢失、取消、换类和多角色；默认 Shooter 配置沿已读取的原行为，不自动创建 Align。Interface / Layer 继续沿角色共享组实例，目标/胶囊属于角色物理服务，不由 Linked Layer 拥有。

原 GA 的完整激活/取消、任意自定义 delegate / Blueprint MotionWarping notify dispatch、Bone / follow-component 目标、通用 SkewWarp、整个 Main 连续 native、完整 NotifyState/命名事件、Shotgun/Feminine 完整 Main、复杂地形 / 材质 / 握持 / 性能和所有原目标仍开放。音频、道具物理及头颈专项继续暂缓。
