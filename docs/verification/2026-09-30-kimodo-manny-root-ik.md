# Kimodo Manny 根位移与辅助骨骼补验

2026-09-30，继续处理 [首次 Mocara 重定向](2026-09-30-kimodo-mocara-manny-fbx.md) 中静止的 `root`。源动画 `/Game/KimodoMocara/AS_KimodoWalkStop_Manny` 和 GASP58 的 `SK_Mannequin` 保持不变；新动画保存为 `/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Final`。本次没有使用 Blender，也没有修改 Mocara、UE 源码或共享 Skeleton。

后续实际 PIE 播放、Manny 截图及 FBX 回读验证见 [Kimodo UE 可视播放](2026-09-30-kimodo-ue-visual.md)。

## 实际骨骼层级

通过 `tools/unreal/inspect_kimodo_manny.py` 只读导出 Skeleton 文本和动画姿态。GASP58 的 `SKM_Manny` 有 161 条物理骨骼轨道，其中 151 条属于 `pelvis` 分支，`center_of_mass`、`interaction` 和两组 `ik_*` 属于 `root` 的其他子分支。该目标 `SK_Mannequin` 的序列化 Virtual Bone 清单为空，动画求值也只有 161 根物理骨骼；不能把 `ik_foot_l` 等真实辅助骨骼当作 Virtual Bone。旧动画的 `root` 完全静止，手脚运动时 7 根 `ik_*` 辅助骨骼保持参考姿态。

[Epic 的 Virtual Bone 文档](https://dev.epicgames.com/documentation/unreal-engine/virtual-bones-in-unreal-engine)说明 Virtual Bone 定义在目标 Skeleton 中，由源骨与目标骨关系驱动，随 Animation Sequence 求值。因此对带 Virtual Bone 的其他目标骨架，先重定向其真实骨骼，再用目标 Skeleton 自身生成 Virtual Bone；不复制 SOMA 的骨骼名，也不往共享 Manny Skeleton 增加定义。`verify_kimodo_manny_root_ik.py` 会读取目标定义，并在存在 Virtual Bone 时检查其组件空间姿态是否跟随指定目标；**本次目标清单为 0，该分支未经过实际资产验证**。动画 FBX 不承载 UE Skeleton 的 Virtual Bone 定义；回导须选择保留这些定义的目标 Skeleton。如需在独立 FBX 中交付实体化的辅助骨骼，应在独立导出骨架中烘焙，不能靠添加 Virtual Bone 轨道到共享 Manny 动画实现。

## 转换方法

`tools/unreal/bake_kimodo_manny_root_ik.py` 仅接受 `root` 和两个 IK 根为单位变换、`pelvis` 直接位于 `root` 下的目标动画，并要求 Manny 的辅助骨骼父链精确匹配。它逐帧把 `pelvis(t).XY - pelvis(0).XY` 写入 `root.XY`，从 `pelvis.XY` 减去同一位移；`root.Z` 固定在地面，`pelvis.Z` 保留原始上下起伏。这样身体分支的组件空间姿态不变，新动画开启 UE 的 `EnableRootMotion`。该策略适用于这段步行；转身的根 yaw 和跳跃/攀爬的根 Z 尚未抽取。UE 5.8 的 [Root Motion 操作](https://dev.epicgames.com/documentation/unreal-engine/retargeting-operation-stack-in-unreal-engine-5-8)同样区分从目标骨盆生成根位移、是否随骨盆旋转及根高度来源。

Mocara 当前 `MocaraTargetProfile.cpp` 将 SOMA 导入后的 +X 位移旋转到这套 Manny 动画的 +Y 运动基准；最终根位移以 +Y 为主与该 profile 一致。根处理保持 Mocara 已确定的运动基准，没有再次旋转轨迹。

同一帧把 `ik_foot_l/r` 的组件空间姿态对齐 `foot_l/r`；`ik_hand_gun` 和 `ik_hand_r` 对齐 `hand_r`，`ik_hand_l` 对齐 `hand_l`。新的辅助骨骼局部键由目标组件姿态相对各自真实父骨计算；`ik_foot_root`、`ik_hand_root` 仍跟随 `root`。`center_of_mass`、`interaction` 保留其本地轨道，因此随新的根运动；没有推测它们应绑定到手或骨盆。这些 IK 映射面向当前 Manny 层级，换 Quinn、MetaHuman 或其他 UE 骨架时需先核对实际目标骨名和父链。

手部对齐针对当前无道具样本，不代表已校准武器握持点或双手持械关系。

## 复现命令

原始 Mocara 动画与插件构建按首次记录准备。复跑时使用新的资产和 FBX 名称，脚本会拒绝覆盖现有结果：

```powershell
$env:KIMODO_INPUT_ASSET_PATH = '/Game/KimodoMocara/AS_KimodoWalkStop_Manny'
$env:KIMODO_TARGET_MESH = '/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny.SKM_Manny'
$env:KIMODO_ASSET_PATH = '/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Recheck'
$env:KIMODO_FBX_OUT = 'F:\Github\Godot-Locomotion\assets\generated\kimodo_ue\kimodo_walk_stop_manny_root_ik_recheck.fbx'
& 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' 'F:\Github\GASP58\GASP58.uproject' '-run=pythonscript' '-Script=F:\Github\Godot-Locomotion\tools\unreal\bake_kimodo_manny_root_ik.py' '-DisablePlugins=ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput
& 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' 'F:\Github\GASP58\GASP58.uproject' '-run=pythonscript' '-Script=F:\Github\Godot-Locomotion\tools\unreal\verify_kimodo_manny_root_ik.py' '-DisablePlugins=ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput
```

## 验证结果

最终生成 `F:\Github\GASP58\Content\KimodoMocara\AS_KimodoWalkStop_Manny_RootIK_Final.uasset` 和 `assets/generated/kimodo_ue/kimodo_walk_stop_manny_root_ik_final.fbx`。FBX 是动画专用文件，3,009,628 字节，SHA-256 `777e9c386744772f45bf1a7925b3a2c1038392a37d6ffdd3103074a23bf99baf`。UE 资产 SHA-256 为 `f69cd62342ff99ddee71a050263a2970f7e19fe3de246cc306799befc8464758`。源记录与全部指标见同 basename 的 `.source.json`、`.verification.json`；命令日志为 `ue_bake_root_ik_final.log` 与 `ue_verify_root_ik_final.log`，两次进程均退出 0。

| 验证 | 结果 |
| --- | --- |
| 根轨道 | 121 键、30 Hz、4 秒；`EnableRootMotion=true`；终点 `(-36.51364, 346.39487, 0) cm`，位移长 `348.31401 cm`；逐帧相对骨盆水平位移最大差 `7.3e-6 cm` |
| 身体保持 | 151 根 `pelvis` 分支 x 121 帧 = 18,271 姿态；相对旧动画最大全局位置差 `0.00010034 cm`，旋转差 `0.00007198 deg` |
| Manny IK | 5 个手脚目标 x 121 帧 = 605 对；最大位置差 `0.00009424 cm`，旋转差 `0.00012912 deg` |
| FBX 回导 | 仍绑定同一 `SK_Mannequin`，121 键、4 秒；回导 89 条显式轨道，对全 161 根物理骨骼 x 121 帧 = 19,481 姿态比对，最大位置差 `0.00006115 cm`，旋转差 `0.00013904 deg` |
| Virtual Bone | 目标定义 0 条；未修改 Skeleton；带 Virtual Bone 的目标骨架动态验收尚未执行 |

第一轮烘焙写出了可读资产，但 UE 在命令脚本完成后的 Python 退出清理阶段崩溃，进程码 3，见 `ue_bake_root_ik.log`，不计为正式通过。释放脚本持有的 Controller/姿态对象后，在全新路径完成上述最终退出 0 的烘焙和回导。Interchange 对动画专用 FBX 仍报告 bind pose 警告，但逐骨骼逐帧回导通过。尚未做 UE 角色移动组件的 Root Motion 消费、转向/跳跃策略、Virtual Bone 非空骨架以及人工观感验收；这些不能由数值回导代替。
