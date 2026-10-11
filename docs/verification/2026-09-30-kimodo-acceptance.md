# Kimodo → Manny → FBX：角色根运动、Virtual Bone 与脚部验收

2026-09-30，UE **5.8.1 / CL 56057345**，`F:/Github/GASP58/GASP58.uproject`。承接 [FBX 可视播放](2026-09-30-kimodo-ue-visual.md)。本批完成补充验收，结论为：**转换及角色根运动通过；非空 Virtual Bone 可以工作，但有子帧误差；严格无滑步／无穿地未通过。不能把这条转换路径描述成免修整的成品动画流程。**

## 交付路径与资产

路径仍为 `Kimodo SOMA BVH → Mocara 导入与 IK 重定向 → Manny AnimSequence → pelvis XY 根运动与真实 IK 辅助骨烘焙 → UE FBX 导出 → 指定目标 Skeleton 回导`。本批使用 UE Python 和 MCP，没有使用 Blender，没有修改 UE 或 Mocara 源码。

最终 FBX 没有被本批测试覆盖：

- 文件：`assets/generated/kimodo_ue/kimodo_walk_stop_manny_root_ik_final.fbx`。
- SHA256：`777e9c386744772f45bf1a7925b3a2c1038392a37d6ffdd3103074a23bf99baf`，收尾复核相同。
- 原生动画：`/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Final`。
- 普通 FBX 回读：`/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual`。
- 本次测试包：`/Game/KimodoMocara/AcceptanceV2`，含 `AS_FBX_RootMotion`、`SK_Manny_VB`、`SKM_Manny_VB_Imported`、`AS_VB_Definitions`、`AS_FBX_RootMotion_VB`。

导入后的 Root Motion 开关必须在 UE 资产上开启；消费测试另将 AnimInstance 设成 `Root Motion from Everything`。FBX 不会替项目配置动画蓝图的根运动模式。示例只提取水平平移，不包含根 yaw 提取；角色 mesh 明确采用 `Rotator(pitch=0, yaw=-90, roll=0)`。

## CharacterMovement 实际消费

`tools/unreal/kimodo_acceptance.py` 在编辑器放置三个原生 `Character`，分别播放原生动画、FBX 回读动画、带四条 Virtual Bone 的 FBX 回读动画。三个演员使用相同尺寸胶囊、独立位置和同一水平地面；无控制器时允许 CharacterMovement 运行。PIE 中先静止 15 帧，再完整播放 4 秒并观察停止阶段。固定的是 PIE World 的时间步长，不是把动画时间手动跳到末帧。

每次游戏时间推进后记录 Actor、速度、MovementMode、动画时间、组件内根骨、世界空间手脚和虚拟骨。30/60/120 Hz 分别产生 **127/255/510** 组记录，每组包含三个角色。运行时数据见 `assets/generated/kimodo_ue/acceptance/runtime_{30,60,120}.json`，汇总见 [results.json](../../assets/generated/kimodo_ue/acceptance/results.json)。

| 检查 | 实测结果 |
| --- | --- |
| FBX 角色最终世界位移 | X `346.394897 cm`，Y `36.513599 cm` 左右，Z `0` |
| 三帧率、三角色的逐帧根运动位置误差 | 最大 `0.000064 cm`；检查界限 `0.01 cm` |
| 动画末时间 | 全部 `4.0 s` |
| 组件空间根骨平移 | 全部为 `0`，没有网格与胶囊重复移动 |
| MovementMode | 全部采样均为 Walking |

这次确实验证了胶囊和 Actor 的移动，补齐上一轮只有网格内部位移的缺口。范围限该四秒水平动画，不包括网络复制、Montage only 模式、跳跃、台阶及障碍碰撞等玩法。

## Virtual Bone：保留定义，不当作 FBX 实体骨

原始共享 `SK_Mannequin` 的 Virtual Bone 数量为 0。测试复制 Skeleton，通过 Manny 网格 FBX 导入绑定到独立 Skeleton，并在**动画导入前**定义以下四条虚拟骨：

| 源骨 | 目标骨 | UE 名称 |
| --- | --- | --- |
| root | foot_l | VB root_foot_l |
| root | foot_r | VB root_foot_r |
| pelvis | hand_l | VB pelvis_hand_l |
| hand_r | hand_l | VB hand_r_hand_l |

这些是非空定义的测试组合，不宣称每个 UE 项目都采用同一套 Virtual Bone。实际项目应保留其目标 Skeleton 上已有的 source/target 定义。原始共享 Skeleton 未修改。两个 FBX 回读动画仍各有 **89 条显式实体轨道、0 条显式 VB 轨道**；引擎根据 Skeleton 定义生成虚拟骨数据。

三帧率下实际 Manny 播放均正常；但“虚拟骨每帧与目标位置误差小于 0.1 cm”的严格探针结果为：

| 采样 | 最大位置误差 | 0.1 cm 探针 |
| --- | ---: | --- |
| 30 Hz | 0.037846 cm | 通过 |
| 60 Hz | 0.556475 cm | 未通过 |
| 120 Hz | 0.556475 cm | 未通过 |

峰值在 `0.283333 s` 的 `VB root_foot_l`。121 个源关键帧采样的最大误差约 `0.037848 cm`。关键帧与子帧的差异符合压缩虚拟骨轨道插值造成误差的解释；安装版 `Engine/Source/Runtime/Engine/Private/Animation/AnimCompressionTypes.cpp:643` 可见引擎生成虚拟骨轨道。这里未用修改压缩器的独立实验证明全部误差来源，不能把此解释当成已隔离的唯一根因。

因此 **Virtual Bone 定义及运行链路已验证，精确逐子帧约束未通过**。0.1 cm 是本次探针界限，不是 UE 官方通用质量标准。如后续 IK 消费者需要严格重合，应在最终姿态上重算 source→target 相对变换，或提高烘焙采样密度后再次测量；本批没有实现这两种补偿。

## 脚部接触与滑动

使用原始 `walk_stop_42.npz` 的 contact 标记，按 SOMA77 `[Lheel,Ltoe,LtoeEnd,Rheel,Rtoe,RtoeEnd]` 映射到 Manny `foot_l/ball_l/foot_r/ball_r`。仅比较共同的 120 个源样本及 119 个帧间差分，排除导出额外末键造成的静止偏差；水平速度采用起始帧 contact 标记。源位置由米转换为厘米。

| 骨骼代理点 | SOMA 接触期平均水平速度 cm/s | Manny 接触期平均水平速度 cm/s | Manny 接触期峰值 cm/s |
| --- | ---: | ---: | ---: |
| foot_l | 1.216 | 3.871 | 12.551 |
| ball_l | 2.290 | 5.570 | 41.870 |
| foot_r | 1.544 | 4.181 | 10.372 |
| ball_r | 2.663 | 7.220 | 35.274 |

连续接触区间内，左右脚踝代理点最大跨度约 `2.15 cm`，脚趾代理点最大跨度约 `4.00 cm`。这些值衡量代理点漂移，包含脚掌滚动的影响，并非蒙皮鞋底每个顶点的滑步距离。源 contact 也是模型预测，未人工逐帧标定。但重定向结果并非严格锁脚，且相对源动画有可测的漂移增加。

120 Hz 下左脚趾骨最低点低于夹具地面约 **0.623 cm**。地面 Z=10000，胶囊中心 Z=10098，mesh offset=-96，已计入真实 2 cm 的组件离地量。第 85 帧近景可见鞋尖与地面交叠，不能仅凭全身远景宣布无穿地。

![第85帧脚部近景](../../assets/generated/kimodo_ue/acceptance/feet_f085.png)

**严格无滑步、无穿地验收未通过。** 本批没有用移动地面、整体抬高角色或放宽精度界限隐藏问题；也没有给最终 FBX 烘入未经验证的脚锁修正。后续修整应保留当前根轨道，在 target 腿链上根据接触相位进行落脚点约束、鞋底高度及两骨 IK 处理，重新烘焙真实 IK 骨，然后重复 FBX 回导与相同验收。

## 同机位实际播放截图

额外跑了一次 30 Hz CharacterMovement 播放，在第 1/30/60/85/90/120 个游戏帧暂停，截图后继续播放；没有通过 SetPosition 伪造角色轨迹。原始全幅 PNG 名为 `played_f*.png`，对应逐帧记录为 `runtime_30_capture.json`。拼图仅对每张相同范围裁去空白天空／前景、缩放并加标签。画面由近到远为 Native、FBX、FBX+VB。

![六帧角色播放对照](../../assets/generated/kimodo_ue/acceptance/root_motion_contact_sheet.png)

六帧能确认角色实际移动、迈步及收步，没有明显身体崩坏；脚部近景与数值优先于全身远景的印象。本批不是连续视频逐帧人工审片，也不是全部 UE 通用骨架、所有姿态或复杂地形验收。

## 故障、修正与复现

1. 初版夹具直接修改网格 Skeleton 属性失败，安装版 Python 未提供该写接口；改为导出／回导网格并指定独立 Skeleton。
2. 动画导入后再调用 AddVirtualBone，UE 动画数据压缩线程发生 `161 into array size 161` 断言。`vb_first_failure.log/xml` 保留证据。现流程先保存 Skeleton，明确其 preview mesh，用空 AnimSequence 建立 VB，再导入动作；成功通过本批采样和三帧率播放。预览绑定和操作顺序同时更改，尚未单独隔离哪个因素决定性，不能声称修复引擎 bug。
3. 初版 `Rotator(0,-90,0)` 被实际 Python API 解释为 pitch=-90，Walking 模式丢掉了转到竖直方向的根位移。改显式 keyword 后重建夹具。失败数据保留为 `failed_pitch_runtime_30/60.json`，不计入通过项。
4. 崩溃后跳过临时测试包／测试关卡自动恢复，改在 AcceptanceV2 建立完整保存的资产；首次不完整 Acceptance 包保留作故障证据，不作为交付资产。

复现：在 GASP58 Editor Python 导入 `tools/unreal/kimodo_acceptance.py`，调用 `prepare(); setup()`，启动 PIE Simulate 后 `run(30)`；结束 PIE 后分别重启运行 60、120。截图轮用 `run(30,(1,30,60,85,90,120))`，每次 `KIMODO_CAPTURE_READY` 后 CaptureViewport，再 `GameplayStatics.set_game_paused(world,False)`。停止 PIE 后 `cleanup()`。普通 Python 运行 `tools/unreal/analyze_kimodo_acceptance.py` 重算结果与拼图。

日志：`assets/generated/kimodo_ue/acceptance/editor_visible.log`。末尾 `KIMODO_ACCEPTANCE_CLEANUP 4` 确认移除三个测试角色及地面；未保存或覆盖项目关卡。未提交 Git、未更改共享 Manny Skeleton。本机同时存在其他 UE 命令行任务，本次未终止这些进程，并使用独立日志避免混淆其输出。
