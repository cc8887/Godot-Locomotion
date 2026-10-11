# Kimodo SOMA 到 UE Manny FBX 的实测路径

2026-09-30，在当前 Godot 主目录保存生成物，使用 `G:\Github\kimodo`、`F:\Github\GASP58` 和本机 UE 5.8.1 验证。没有修改 Kimodo 或 UE 源码，也没有使用 Blender。GASP58 的 `Plugins/Mocara` 是本次从 [Mocara](https://github.com/Conalh/Mocara-Unreal) 克隆并编译的插件；最终动画资产保存在 GASP58，BVH、FBX、日志及 JSON 记录保存在本项目被 Git 忽略的 `assets/generated/kimodo_ue/`。

后续根位移与 Manny IK 辅助骨骼处理见 [2026-09-30-kimodo-manny-root-ik.md](2026-09-30-kimodo-manny-root-ik.md)。下文保留首次重定向的原始结果。

## 路径与边界

`Kimodo-SOMA-RP-v1.1` 文本生成 -> SOMA BVH -> Mocara BVH importer -> 临时 SOMA 骨架和 IK Retargeter -> GASP58 的 `SKM_Manny` / `SK_Mannequin` 动画序列 -> UE `AnimSequenceExporterFBX` -> 动画 FBX -> 用同一 Manny Skeleton 回导 UE。

[K.ai 的项目记录](https://ctechfilmuniversity.github.io/project_kaspar/improv/layer/kai_layer_avatar_animation.html)采用 BVH -> Blender FBX -> UE -> MetaHuman，并记录了导入辅助几何体与手脚质量问题。本次用 Mocara 在 UE 内直接导入 BVH 并创建临时源网格，省去了该 Blender 中转，因此没有 Blender Python SDK 调用。Mocara 的 `Mocara.VerifyRetarget` 诊断入口会实际执行其导入、IK 建立、重定向和目标动作修正；本次只把最终 `UAnimSequence` 另存为持久资产，诊断用源网格和 IK 资产未另存。目标确实是现有 Manny 网格及其原有 Skeleton；Mocara 按骨骼结构选择了名为 `MetaHumanBody` 的内部 profile，此名称不改变目标资产。

Kimodo 的 `--bvh_standard_tpose` 只设置 BVH 静止姿态，不会插入首帧 T Pose，见 [Kimodo CLI 文档](https://research.nvidia.com/labs/sil/projects/kimodo/docs/user_guide/cli.html)。该 BVH 外层 `Root` 静止，位移在 `Hips`；本次目标动画与 FBX 的 `root` 同样静止，位移在 `pelvis`。尚未把骨盆位移转成可直接供 UE Root Motion 使用的 `root` 轨道。

## 本机复现

本次 Kimodo checkout 为 `58e781898b3d7e328a676a75d3e338c45dce3ad9`，Mocara 为 `e75573da621543426a49fdf8f3ebdf9fac150810`。先用本机已有的模型缓存和 Kimodo 虚拟环境生成四秒样本：

```powershell
$env:HF_HUB_CACHE = 'G:\Github\kimodo\.cache\huggingface\hub'
$env:HF_XET_CACHE = 'G:\Github\kimodo\.cache\huggingface\xet'
$env:KIMODO_LLAMA_BASE_MODEL_PATH = 'G:\Github\kimodo\.cache\modelscope\Meta-Llama-3-8B-Instruct'
$env:TEXT_ENCODER_DEVICE = 'cpu'
$env:TEXT_ENCODER_MODE = 'local'
& 'G:\Github\kimodo\.venv\Scripts\kimodo_gen.exe' 'A person walks forward with three steady steps and comes to a stop.' --model Kimodo-SOMA-RP-v1.1 --duration 4 --seed 42 --diffusion_steps 100 --bvh --bvh_standard_tpose --output 'F:\Github\Godot-Locomotion\assets\generated\kimodo_ue\walk_stop_42'
```

在 GASP58 安装并构建 Mocara：

```powershell
git clone --depth 1 https://github.com/Conalh/Mocara-Unreal.git F:\Github\GASP58\Plugins\Mocara
& 'C:\Program Files\Epic Games\UE_5.8\Engine\Build\BatchFiles\Build.bat' GASP58Editor Win64 Development '-Project=F:\Github\GASP58\GASP58.uproject' -WaitMutex -NoHotReloadFromIDE
```

`tools/unreal/retarget_kimodo_mocara.py` 要求新的 `/Game` 动画资产路径和新的 FBX 文件路径；复跑时更换下面两处名称，才能重新执行重定向。`KIMODO_REUSE_ASSET=1` 仅用于已保存动画的再次导出，不会重新运行 Mocara。

```powershell
$env:KIMODO_BVH = 'F:\Github\Godot-Locomotion\assets\generated\kimodo_ue\walk_stop_42.bvh'
$env:KIMODO_TARGET_MESH = '/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny.SKM_Manny'
$env:KIMODO_ASSET_PATH = '/Game/KimodoMocara/AS_KimodoWalkStop_Manny_Recheck'
$env:KIMODO_FBX_OUT = 'F:\Github\Godot-Locomotion\assets\generated\kimodo_ue\kimodo_walk_stop_manny_recheck.fbx'
& 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' 'F:\Github\GASP58\GASP58.uproject' '-run=pythonscript' '-Script=F:\Github\Godot-Locomotion\tools\unreal\retarget_kimodo_mocara.py' '-DisablePlugins=ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput
& 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' 'F:\Github\GASP58\GASP58.uproject' '-run=pythonscript' '-Script=F:\Github\Godot-Locomotion\tools\unreal\verify_kimodo_mocara_fbx.py' '-DisablePlugins=ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput
```

## 本次结果

| 项目 | 结果 |
| --- | --- |
| Kimodo BVH | 78 节点，120 帧，30 Hz；SHA-256 `e5a16aa7b2f4d54c86fe35afa647a5efef781fc51cb475d9de06a82047ada595` |
| Mocara / UE 编译 | GASP58Editor Win64 Development 构建退出码 0，45 actions；`ue_retarget_final.log` 包含 BVH 导入和 `SKM_Manny (161 bones)` 重定向成功记录 |
| 持久动画 | `F:\Github\GASP58\Content\KimodoMocara\AS_KimodoWalkStop_Manny.uasset`，绑定 `/Game/Characters/UE5_Mannequins/Meshes/SK_Mannequin` |
| 最终 FBX | `assets/generated/kimodo_ue/kimodo_walk_stop_manny.fbx`，动画专用 ASCII FBX 2020.2，3,014,390 字节；SHA-256 `18c880ce17875ec58f0d87265c90cd353800031682030f82292faf37807a9d82` |
| FBX 回导 | 原资产 161 条显式骨骼轨道，回导后 89 条；两者都是 121 个采样键、30 Hz、4.0 秒，绑定同一个 Manny Skeleton。FBX 省略了静态/冗余轨道，逐帧姿态仍一致 |
| 姿态比对 | 161 骨 x 121 帧 = 19,481 个姿态；最大位置差 `6.151015e-5 cm`，最大旋转差 `0.00013018 deg` |
| 位移 | 源资产与回导结果的 `root` 起终点位移都是 0 cm；`pelvis` 起终点位移均约 348.34646 cm |

原始记录为 `kimodo_walk_stop_manny.source.json` 与 `kimodo_walk_stop_manny.verification.json`；对应 UE 日志为 `ue_retarget_final.log` 和 `ue_readback_final_full.log`，分别含 `KIMODO_MOCARA_EXPORT_OK` 与 `KIMODO_MOCARA_FBX_READBACK_OK`，两次进程都退出 0。FBX 回导是动画导入，依赖已有的 Manny Skeleton；该 FBX 不含角色网格。Interchange 曾报告动画 FBX 的 bind pose 无效并采用零时姿态；回导后的上述逐骨骼逐帧数值校验仍通过。

这证明文件转换和 Manny 姿态数据回读成立，尚未做人工视口观感验收、脚底滑动/接触评价或 Root Motion 玩法验收。Mocara 日志报告左右膝源/目标关节角最大差约 16.87/23.76 度，且 Kimodo 扩展手指为固定放松姿态，不能据此声称最终动作的手脚质量已达标。初次测试用 `force_front_x_axis=True` 的 FBX 回导出现 90 度根朝向差；最终文件使用 `False`，并通过完整回读对比。无需使用该初次测试文件。
