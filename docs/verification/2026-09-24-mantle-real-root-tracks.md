# Mantle 真实根骨轨道绑定与原生对照

本批在 `D:/GodotALS` 的 main 完成；Mantle 尚未接入普通 demo 的攀爬玩法。

## 数据与实现

- 只读导出 6 个 Refactored montage 的 first-slot 精确分段参数，以及 3 个实际源动画的原始 root 通道。High 有 81 个关键帧，Low_Left / Low_Right 各 46 个；均为 30 Hz、Linear，速率 1。当前全部是单 slot、单 segment，导出器对其他拓扑明确拒绝。已有高低攀爬设置导出保持不变。
- `refactored_mantle_root_tracks.json` 保留原始通道长度、float 分量、帧率、root reference、序列速率；文件 SHA256：`7D02D011E3721C1AAA911356C6C5712F9D7CB5C97BFCF32605C3D61C2FE88963`。
- `AlsMantlingRootCompiler` 创建只含根骨的采样资源并按序列路径绑定各段，不是完整角色 pose。生产编译不读取 `references` 或旧文件的 `rootSamples`。测试删除 oracle 后仍能编译，修改原始关键帧会改变输出。
- exporter 拒绝带 transform curves 的源动画；尚未实现该种根骨叠加，也不声称支持任意压缩 codec / Sequencer 数据模型的等价采样。

## 实测发现并修复的差异

首轮 Import 定向测试 14 过 / 1 失败。旧 616 个区间内原生样本已通过，但新增的尾部越界样本相差 210.29740844813091 cm，见 `artifacts/mantle-real-roots/mantle-real-roots.trx`。

对照本机 UE `AnimSequence.cpp::GetBoneTransform_Lockless` 与 `AnimDataModel.cpp::EvaluateBoneTrackTransform/GetBoneTrackTransform`：绝对根骨的 raw 分支不是完整 pose 的夹紧采样。它只做一次 FFrameTime 转换，Step 取就近帧，越界 key 为 identity；边缘分数帧可在实际 key 和 identity 之间插值。各原始通道都含该 key 才能返回原子。

`SampleAbsolute` 改为此路径，并保存最短原始通道长度。缺失 root track 仍回退 root reference。旧 `Extract` 保持此前区间提取的采样行为，未趁此修改其他动作的 Root Motion。之前合成测试中的“超出范围夹紧到尾帧”预期已按实际原生路径修正；没有扩大比较容差。

## 验证

- 616 个已有原生样本 + 30 个新增负时间、非整帧、尾部越界样本 + 6 个原生 ExtractLast 结果，共 **652** 个。位置距离、四元数 dot 偏差、scale 距离本次全部为 **0**。门槛分别为 0.001 cm、1e-7、1e-6，未调整。
- Import Release：本批 7 项 + 既有 Mantle settings 8 项，**15/15**；日志 `artifacts/mantle-real-roots/mantle-real-roots-fixed.trx`。
- Core Release：绝对采样和旧 montage Root Motion 共 **18/18**；包含最近帧 Step、identity 越界、短通道及原有分段边界/速率/loop，日志 `mantle-root-boundaries.trx`。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning / 0 error。
- UE 完整 Editor 目标 0 action + 插件审计通过；构建日志前缀 `20260924T130146140Z-e237d4495a2a4e5fb54f4c08796cacf2`，fingerprint `5507EC2FCBD1D34CCE8C479248800626CB6EFA1E0DE171EF91A041FCBDA81BC6`。
- 冷导出 first / repeat / final：日志和 JSON 保存在 `artifacts/mantle-root-tracks-*`；没有修改原生插件或保存 UE 资产。本批未跑普通 Editor、DataValidation、完整测试套件或 Godot 渲染场景。

## 仍需继续

下一步移植 Mantling RootMotionSource 的目标变换、位置/旋转 warp、montage 时钟同步；随后接入 Main 障碍探测、移动目标和动作生命周期、目标销毁转 Ragdoll。当前只是实资产根骨采样闭环，不能视为攀爬玩法或观感验收通过。

旧物理稳定性 9/12、Flail 0/3、复杂相机碰撞/完整场景视觉验收、整体角色物理缩放和十分钟性能预算仍未完成。头颈与道具物理继续按用户要求暂缓。用户 P4 文档及 3 个头颈诊断文件未修改、未纳入提交。
