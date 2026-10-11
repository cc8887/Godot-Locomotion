# Kimodo FBX 回导后的 UE Manny 可视播放

后续已完成 [CharacterMovement、非空 Virtual Bone 与脚部补充验收](2026-09-30-kimodo-acceptance.md)：根运动通过；子帧 VB 精度及严格无滑步／无穿地仍有未通过项。以下保留本轮历史证据。

2026-09-30，在 GASP58 / UE 5.8.1 中检查[根骨骼与 IK 处理后的 FBX](2026-09-30-kimodo-manny-root-ik.md)是否真的驱动现有 `SKM_Manny`。本次没有使用 Blender，也没有修改共享 `SK_Mannequin`、Mocara 或 UE 源码。

## 资产与播放

- 输入是 `assets/generated/kimodo_ue/kimodo_walk_stop_manny_root_ik_final.fbx`，SHA-256 `777e9c386744772f45bf1a7925b3a2c1038392a37d6ffdd3103074a23bf99baf`。
- `tools/unreal/import_kimodo_manny_visual_readback.py` 将该 FBX 作为**动画**回导到 `/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual`，复用原有 `/Game/Characters/UE5_Mannequins/Meshes/SK_Mannequin`。命令进程退出 0；`ue_visual_readback_import.log:2243` 记录 120 帧、121 键、4 秒、89 条显式轨道。Interchange 报过无效 bind pose 并使用零时姿势；该警告不能单独证明网格播放正确。
- `tools/unreal/probe_kimodo_mesh_pose.py` 在目标 `SKM_Manny` 上对原 UE 动画和 FBX 回导动画做压缩数据、启用重定向的采样。两者在 0、1、2 秒的根、骨盆、手脚姿态相近，`GASP58.log:5119` 有具体数值；先前[全 161 骨 x 121 帧回导比较](2026-09-30-kimodo-manny-root-ik.md)给出的最大位置差为 `0.00006115 cm`。

第一次向编辑器临时演员调用 `PlayAnimation` 后，PIE 复制出的演员虽然显示单节点动画模式，却没有动画资产，骨骼一直是 Manny 参考姿态。UE 的 `SkeletalMeshComponent.h:1251-1335` 明确指出 `PlayAnimation` / `SetAnimation` 不序列化到关卡组件，应调用 `OverrideAnimationData`。已在 `tools/unreal/setup_kimodo_visual_actor.py` 改正；再次启动 PIE 后，`GASP58.log:5321` 确认运行世界的组件持有 FBX 回导资产且骨骼已离开参考姿态。此前静止截图和未绑定资产的 PIE 截图**不是成功证据**。

## 同机位截图

在 `/Game/Levels/DefaultLevel` 放置临时 `SKM_Manny`，PIE Simulate 中将单节点播放器分别固定在 0、30、60、90、120 帧。每次等组件完成评估，再用 MCP `CaptureViewport` 在相同相机 `(-450, 165, 115) cm`、侧视 yaw `0` 拍摄。`tools/unreal/build_kimodo_visual_sheet.py` 只裁切、缩放原始 UE PNG 并加时间标签，原图均保留。

![UE Manny 五帧动作对照](../../assets/generated/kimodo_ue/visual/pie_fbx_5frame_contact.png)

| 帧 / 秒 | 运行时 root.Y cm | 骨盆 Z cm | 左/右 foot 骨 Z cm | 原始截图 |
| --- | ---: | ---: | ---: | --- |
| 0 / 0 | 0.0000 | 90.4095 | 26.2426 / 10.3939 | [PNG](../../assets/generated/kimodo_ue/visual/pie_fbx_wide_f000.png) |
| 30 / 1 | 118.2786 | 91.5536 | 14.6392 / 14.0543 | [PNG](../../assets/generated/kimodo_ue/visual/pie_fbx_wide_f030.png) |
| 60 / 2 | 235.5052 | 94.3459 | 10.6120 / 12.4850 | [PNG](../../assets/generated/kimodo_ue/visual/pie_fbx_wide_f060.png) |
| 90 / 3 | 325.5373 | 93.5678 | 8.4920 / 22.6637 | [PNG](../../assets/generated/kimodo_ue/visual/pie_fbx_wide_f090.png) |
| 120 / 4 | 346.3949 | 95.1633 | 9.7597 / 10.8419 | [PNG](../../assets/generated/kimodo_ue/visual/pie_fbx_wide_f120.png) |

`GASP58.log:5385,5404,5416,5427,5439,5574` 记录对应 PIE `SkeletalMeshComponent` 的资产、播放时间和骨骼变换。0 秒从末帧回跳时，停止的单节点实例曾显示时间为 0、渲染姿态仍停在末帧；改为播放速率 0 的活动实例后重拍，`GASP58.log:5574` 与首帧截图均回到原点。对照图 SHA-256 为 `d11a60049c5f92abe0f53d2e9a7eb34441ecfa2b17a3c102258399a929870cc1`。

截图中可见 Manny 交替迈步、双臂摆动并在末段收步；五帧均无明显骨骼塌陷、关节翻转或鞋底穿过平面。`foot_l/r` 是骨骼点而非鞋底接触点，表中高度不能当成脚底间隙。该短片尚未经过连续视频人工逐帧评价、脚锁/滑步量化、手指精细动作或所有地形测试。动画 Sequence 编辑器的单独预览窗口曾保持参考姿态，原因尚未定位；本记录的肯定结论来自**PIE 世界中实际绑定 FBX 回读资产的 Manny 组件**和对应视口截图。

根轨道让网格相对其场景组件前进约 `346 cm`，测试演员的 Actor 位置没有被 CharacterMovement 更新。因此本批确认了 FBX 可在 UE 驱动 Manny 及根轨道可被骨骼组件求值，**未验收角色移动组件对 Root Motion 的消费**。目标 Skeleton 的 Virtual Bone 定义仍为 0；其他非空 Virtual Bone 骨架也未在本批渲染验证。

拍摄完成后已停止 PIE，并仅移除本批临时演员；`GASP58.log:5694,5739` 记录移除及同名演员余量为 0。未保存或覆盖项目关卡。
