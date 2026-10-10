# GASP58 / ALS 攀爬资产、Root 与接触合同分析

2026-10-09。用户确认本次所称 Lyra 攀爬实际指 GASP58 的 Traversal / Climb / Mantle；不将这些资产归为原版 Lyra。任务为分析，没有修改运行代码、生成动画、活动指针或根运动策略，没有提交/推送。

## 范围与证据

- 当前活动指针是 `als_nonprop_20261009_idle_arms_v2`，允许诊断包；包仍为 diagnostic、productionReady=false。当前 `enableRootMotion=false`。不能沿用旧记录中的默认 v8 状态。
- 在真实 GASP58 工程、安装版 UE **5.8.1-56057345** 中以 Python commandlet 读取 8 个代表性 Sequence、7 个 Montage、12 个 Motion Warping 窗口，0 个读取错误，脚本不调用资产保存。抽查涵盖 Neutral/Relaxed、站立左脚起步的 Mantle/Climb，以及 Catch、Vault 对照；不代表遍历全部 walk/run/Rfoot 变体。
- ALS 原版依据当前 `assets/config/refactored_mantle_{animation_inputs,root_tracks,curves,inputs}.json`；新包读取自身 config 中相同文件。V4 开关和 CurveVector 身份来自当前 `assets/generated/als_v4/als_manifest.json`，V4 程序化移动来自本机已导出的 `ALS_Base_CharacterBP::MantleUpdate`。
- 完整采样、原始关键帧、窗口与输入 SHA256：`artifacts/research/2026-10-09-mantle-contract/gasp-authoring.json`、`measured-contracts.json`。UE 日志为同目录 `gasp-read-only.log`，成功标记 `MANTLE_CONTRACT_READ_OK sequences=8 montages=7 errors=0 assets_saved=0`。
- 可复核脚本为同目录 `inspect_gasp.py`、`measure_contracts.py`。后者使用 `G:/Github/kimodo/.venv/Scripts/python.exe` 和项目已有 FK，不加载生成模型。

本轮执行了真实 UE 资产读取与离线作者空间 FK；没有执行新的 Godot 攀爬、UE 整角色、地形/平台碰撞、最终蒙皮、人工视觉或性能验收。下面的手部位移是原始动画参考空间指标，不是角色运行中的世界接触滑移。

## 三种运动机制必须分开

| 实现 | Sequence 根运动开关 | 是否需要原始 Root 轨道 | 胶囊/角色怎么移动 |
| --- | --- | --- | --- |
| ALS V4 原版 | 三条 Mantle 均 `rootMotionEnabled=false`、`forceRootLock=true` | 开关不能用于判断原始轨道是否存在；本轮未新读取 V4 完整轨道 | Timeline + 目标/起始偏移 + 独立 correction CurveVector |
| ALS Refactored 原版 | 三条 Mantle 均 `enableRootMotion=false`、`forceRootLock=true`、RefPose | **需要**；Root 的完整原始轨迹由专门 motion source 读取 | `AlsMantlingRootMotion` 按起/终 Root 和目标建立锚点，读取 Montage 当前 Root，再输出角色变换 |
| GASP58 抽查 Traversal | 8 条均 `enableRootMotion=true`、`forceRootLock=true`、RefPose | **需要**；含整体 XY/Z 位移 | Montage 根运动 + Motion Warping，抓边目标为 `FrontLedge` |

Refactored 当前代码在根策略开启时读取完整轨道，姿态侧锁 Root，角色侧消费运动；根策略关闭时不读取轨道建立锚点，改用高度→开始时间映射和目标插值。这是项目提供的另一种运行策略，不是原版根轨迹已经消失的证据。两种策略都要求动画姿态与对应的角色路径保持接触一致。

原版 Refactored 原生厘米坐标为 XY 地面、Z 向上，Mesh 正面 +Y：High Root 从零到 `(0,65,200)`，Low Left/Right 从零到 `(0,65,100)`。Root 轨道、开关和实际消费方式是三个不同概念，不能只导出开关。

GASP 抽查 Climb_Start_2_5 的 Root 净位移为 `(0,125,250)` cm；Mantle_1_0 为 `(0,118,100)` cm。这些是作者轨迹，并不保证 Root 在所有时刻等于 Pelvis 的位置。

## GASP 的特殊骨：attach 是攀爬对齐点

读取到的 `FrontLedge` 窗口均为 Bone provider、骨名 **`attach`**、warp_translation=true、warp_rotation=true、**ignore_z_axis=false**。这是明确的三维对齐配置。

`attach` 是 root 的直接子骨；参考姿态位置为零，但其动画轨道保持作者组件空间的障碍物参考点：

| 抽查资产 | attach 在作者组件空间的位置（cm） | 对齐窗口示例 |
| --- | --- | --- |
| Neutral Mantle 1m stand Lfoot | `(0,-50,100)`，全片固定 | FrontLedge 0.141–0.365、0.365–0.699 s |
| Neutral Climb 2.5m stand Lfoot | `(0,0,250)`，全片固定 | FrontLedge 0.405–0.732 s |
| Relaxed Climb 2.5m stand Lfoot | `(0,0,250)`，全片固定 | FrontLedge 0.333–0.646、0.646–0.959 s |

Root 运动时，attach 的父子局部变换会补偿，不能把它导出成参考姿态的零值，也不能随意替换为 hand_l/hand_r。Motion Warping 使用窗口结束时 Root 相对 warp bone 的偏移来确定目标 Root；将 attach 当成手腕或丢失它的轨道，会改变抓边的整身位置。

`AC_TraversalLogic::SetWarpTargets` 将 FrontLedge 设为探测边缘位置 +0.5 cm Z，朝向根据边缘法线生成。Vault 的另一窗口还使用 BackLedge；其 provider=None、warp_rotation=false。需要逐窗口导出，不能全片套一个静态终点。

本项目已有 `AlsMotionWarpingRuntime` 明确限定原版 Lyra Emote profile：目标名 Align、原地平移、忽略 Z，并对非零窗口平移抛 `NotSupportedException`。它没有通用 Bone provider/攀爬配置，**不能直接复用为 GASP 攀爬完成实现**。以后应在 Als.Core 扩展共享算法，保持角色已有时钟与事务；避免再增加局部求值链。

## 曲线及侧车应导出什么

| 类别 | 已确认的原版数据 | 对新动画的处理 |
| --- | --- | --- |
| 完整根轨迹 | Root XYZ、旋转、原始时间键、RootLock/evaluation policy | 从最终目标轨道独立导出，不能从已锁定 Root 的播放姿态反算；爬升保留 Z，普通平面 Distance 不够 |
| ALS V4 correction | 独立 CurveVector `Mantle_1m` / `Mantle_2m`：X=PositionAlpha、Y=XYCorrectionAlpha、Z=ZCorrectionAlpha | 与新动作的起始偏移、抓边、抬身、登顶时序一起重新适配；它不是 Root 位移曲线 |
| ALS Refactored | Sequence `PoseStanding`、`PoseGrounded`；Settings 的 referenceHeight/startTime/warpRange、Montage blend/Notify/Slot | 一并导出。原版高/低 warpRange 分别约 0.3–0.6 / 0.3–0.7 s；不是 V4 correction CurveVector 的同一机制 |
| GASP 手部重定向控制 | `retarget_hand_l_blendtosource` / `retarget_hand_r_blendtosource`，各片存在集合不同 | 保留完整 rich keys；这些是重定向控制，不能仅凭曲线名字当成运行时手锁 |
| GASP 脚/骨盆控制 | Neutral 样本存在 `disablepelvisadjustment`、`disableleftfootik`、`disablerightfootik` | 攀爬时避免地面 Foot IK/骨盆地面修正破坏作者动作。Relaxed 样本不一定含这些曲线，按实际资产集合导出 |
| GASP 其他数据 | 部分资产存在 `contact_l/contact_r`；Vault 存在 `distance_from_ledge`，Montage 可有 `MovingTraversal` | contact 的具体消费者/身体部位本轮未追完，不能宣称是手接触曲线。Distance_From_Ledge 由 SetWarpTargets 在窗口结束时读取，并参与 BackLedge/BackFloor 处理，是相对边缘几何量，不能替换为累计 Root 路程 |
| Warping 元数据 | 每个窗口的开始/结束、目标名、provider/bone/static transform、XYZ/rotation 参数；目标平台与边缘坐标约定 | 是必需侧车合同，不属于普通动画 float curve；保留与 Sequence/Montage 时间映射 |

Motion Warping 本身不要求一条统一命名的“攀爬曲线”；必需项是可用的根运动、窗口配置和对应目标。GASP/ALS 的额外曲线由各自玩法、图及重定向消费者决定。当前 Root Motion 独立曲线导出不能替代这些接触/玩法合同。

## 当前新包已确认的失配

| Refactored 资产 | 原版 Root 终点 Z | 新包 Root 终点 Z | 原版 Pelvis 局部 Z 范围 | 新包 Pelvis 局部 Z 范围 |
| --- | --- | --- | --- | --- |
| High | 200 cm | 54.16 cm | 67.97 cm | 0 cm |
| Low Left | 100 cm | 65.82 cm | 59.38 cm | 0 cm |
| Low Right | 100 cm | 65.82 cm | 60.09 cm | 0 cm |

High 起始 Pelvis 局部 Z 从原版 96.75 cm 变为 236.82 cm；Low 为 128.39 cm。原版保留抬身、压髋等相对 Root 的高度变化，新包三个动作的 Pelvis 局部 Z 都恒定。`root_ownership.py` 的 mantle 分支令 Root Z 等于“首帧 Root Z + Pelvis 世界 Z 相对首帧的差”，并反解 Root 直接子骨；因此在该分解下 Pelvis Z 必然压平。该策略只保证分解前后身体 FK 重建，并不保证匹配原版角色路径/锁 Root 播放合同，也不会修好输入的悬空起点。

Root 高度不同单独不足以证明动画不能 warp 到目标：合理的新动作可以有不同标称高度并适配对应 profile。但当前同时存在异常起点、残差压平和缺失抓边接触，不能靠调整目标高度宣布修复。

原版 High 在 0.6–1.0 s 的双手作者空间位置几乎不变；同时间段新包左/右手从段首最大位移分别 **20.66 / 23.60 cm**。这是同窗对比，不将旧窗口强认作新动作真实接触窗口。

**左右语义也有具体错误。** 原版 Low_Left 在 11–22 帧（30 Hz）主要固定 `hand_r`，位移约 0.45 cm，而 hand_l 在运动；Low_Right 反之。当前 DAG 的 `candidate:Mantle_Low_Left` 提示词却写 `using the left hand for support`。因此不能将 Left/LH 后缀直接解释为支撑手，必须按原骨骼轨迹、Overlay 选择与持物语义核对。新包同参考段的对应支撑手位移变为约 33.88 / 33.18 cm。

新包 `ik_hand_l/r` 与实际手骨位置完全一致，原版误差也很小；**IK 辅助骨跟随手骨正确不等于手贴住台面**。需要台面/边缘相对的接触锚点，而不是再把同一错误手轨迹复制到 IK 骨。

当前 graph 对 Mantle 仅提供线性 root2d 和抬高的末端 fullbody Stand anchor；没有开始/抓边/抬身/登顶的分段锚点，也没有手部 endEffectors。`root2d` 只约束水平，`endHeightMeters` 只作用于已有 fullbody anchor，不形成连续的竖直约束。因而“爬 1m/2m”的提示词和终点不能确保途中双手/双脚与障碍物接触。

原版与新包 `PoseStanding/PoseGrounded` 的关键时间和值仍相同，`curve_values()` 也没有 Mantle 接触相位分支；例如 High PoseGrounded 仍在 2.033–2.333 s 回到 1。不能将原曲线按旧时间保留视为它已经和新动作的登顶接地对齐。

## 后续资产合同建议（本轮未实施）

1. 先固定动作身份：高攀双手；低翻越逐条核实真实支撑手/持物手；GASP Lfoot/Rfoot 是起步脚变体，不能用它推定支撑手。
2. 在作者阶段明确起点地面、边缘高度/法线/深度、左右掌面位置与朝向，以及抓边→承重→抬身→手释放→足部登顶的时间窗。双手有各自锚点，不能都约束到同一点。约束应使用掌面/指根相对 wrist 的偏移，而不是要求手腕中心等于边缘。
3. Root 保存整体行进/抬升/转身；Pelvis 保存相对 Root 的屈伸和重心转移。先确定目标平台与 nominal Root profile，再分解，不把所有 Pelvis Z 原样塞到 Root。源、目标 raw、锁 Root 运行姿态和 actor 路径须有可重建的统一合同。
4. 为 GASP 保留 attach，或在目标骨架明确引入等价参考点与 Root-relative offset 侧车。特殊参考点和 IK 骨需在目标骨架与导出器中显式映射；不能通过不存在的骨名触发静默回退。
5. 对支撑手验收平台局部位置漂移、掌面朝向、手肘可达性/翻折、左右锚点间距；对登顶脚验收最终蒙皮接触、穿透/滑移。上下身 Layering/装备手 IK 与动作 Slot 的最终优先级必须一起验收。标准 Root Warping 可移动整身，无法单独修好两个不一致的手接触。
6. 根轨迹、状态曲线、实际接触权重、Foot/Pelvis IK 控制、Notify 和 warp windows 从新动作真实相位产生；按各原版消费者的名字与语义导出。新引入的手接触权重/锚点是项目自己的合同，不能称 ALS 原版已有强制曲线。
7. 完成作者检查后按既有完整 pipeline 转换，保留独立资产与失败清单，再验证根运动开/关、多个高度、静态/移动/旋转平台、不同 Overlay、取消/重试和最终角色姿态；不降低现有阈值。

官方 Motion Warping 文档确认 Bone provider、三维 translation/rotation 配置和 Montage 窗口：<https://dev.epicgames.com/documentation/en-us/unreal-engine/motion-warping-in-unreal-engine>。GASP 与 ALS 的具体数据以本轮本机读取为准，不能把官方通用教程当成全部样本已经运行验收。
