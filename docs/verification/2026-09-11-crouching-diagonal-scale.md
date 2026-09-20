# Crouching DiagonalScale 组件

日期：2026-09-11；完整性补完第五十批。

## 完成范围

实现 CLF Cycles 中 StrideBlend 之后、Lean 加法之前的 ik_foot_root 缩放。
包含实际源图编译、组件空间计算、局部回混及恢复，不直接改变后代骨的局部偏移。
真实 Godot 资源组件已经按 Stride -> Scale -> Lean 执行；实际更新权重和缓存
仍由组件测试提供，没有接完整主蹲姿、Main 或 Demo，不宣称视觉修复。
现有用户修改保留，未提交、合并或撤销；UE 资产、相机和输入不变。

## 实现与依据

`AlsCrouchingDiagonalScaleCompiler` 读取原生 `(CLF) Locomotion Cycles` 图的
ModifyBone：ik_foot_root、BMM_Additive、BCS_ComponentSpace，忽略平移/旋转，
LOD=-1，Float Alpha 从 DiagonalScaleAmount 读取，不从序列化 alpha 默认值读取。
禁止未支持的映射、额外滤波和生命周期；原生尺度 (1.4,1.4,1) 经既有坐标转换器
变为 Godot (1.4,1,1.4)，骨索引从实际骨架名称绑定。

`AlsDiagonalScalePose.Apply` 对照本机 UE：

- `AnimNode_ModifyBone.cpp` 的组件空间乘法缩放；这里 BMM_Additive 是乘法，不是加一。
- `BonePose.h` 的 FCSPose 懒计算：仅计算目标及其祖先的组件姿势，不预先计算所有后代。
- `LocalBlendCSBoneTransforms` 保存原局部姿势、应用完整组件修改、转换回局部，再以反向权重调用 BlendWith。
- `TransformVectorized.h` 的 BlendWith 阈值、局部平移/缩放插值与最短旋转混合。
- `ConvertComponentPosesToLocalPoses` 恢复剩余已计算祖先；未访问后代的局部姿势不变。

输入 alpha 截断到 0..1，<=1e-5 旁路，>=1-1e-5 使用完整控制。候选输出可与
整个输入原地重合，但拒绝错位重叠及 scratch 别名。父索引和祖先变换先校验，
失败不修改输出；热路径零分配。该 API 当前支持 1..256 骨和非负缩放祖先，
包括零缩放的安全倒数；负缩放所需的 UE 矩阵分解分支不支持并明确拒绝。
这覆盖本项目当前 ALS 资产，不等于通用 UE FTransform 的全部输入域。

## 原生与真实资源验证

`AlsMeshSpaceBlend` 新增 `-ComponentScale` 选项。探针在 ALS 原生 79 骨上，
运行实际 FCSPose 组件/局部转换和 LocalBlendCSBoneTransforms，控制体按源节点
所用乘法缩放构造。三种姿势（参考、旋转/非均匀缩放、含零缩放祖先），三个目标
（ik_foot_root、spine_02、root），12 个 alpha，共 108 案例、8532 组骨骼。
坐标转换后位置、尺度和四元数分量距离误差均不超过 3e-6；四元数比较允许等价符号。
这是原生函数级合成姿势探针，不是完整 AnimBP 或真实播放的逐帧最终骨骼对照。

Godot `CrouchingSourceSmoke` 使用正式源表及共享 Sync 秒数，按原位置连接缩放。
30/60/120 Hz 共 420 帧：262 帧有效、158 帧旁路，重复候选完全一致；Lean 的
263 帧单样本直接验证改为使用缩放后的基础姿势。没有新建动画时钟。
方向/Stride/DiagonalScale 输入和非 Lean 来源权重仍是夹具，实际缓存相关性、
来源停更/恢复和主蹲姿姿势尚未接通，不能以该链通过关闭可玩 Demo 验收。

## 回归

证据目录：`artifacts/test-results/crouching-diagonal/`。

| 检查 | 结果 |
| --- | --- |
| 新增专项 | 17/17 |
| Core 常规，排除既有 P5A golden/schema | 1882/1882 |
| Import 全套 | 1034/1034 |
| 蹲姿/Lean 相关 Release | 163/163 |
| Godot 构建 | 0 warning / 0 error |
| Crouching 来源、方向、Stride、Scale、Lean | 420 帧、7140 来源采样、30 通知，重试一致 |
| Standing/Pivot/Detail/Sprint | 5040 / 5040 / 1890 / 1260 帧 |
| Main 资源组件 | 来源 1050、受控混合 420、中断 104，零分配 |
| 单/多线程 Worker | 各 180 帧、10 通知、首次第 25 帧 |
| 双模式 late_source_event | 候选回滚通过、无回调泄漏 |

旧生产摘要不变：result `A9DF0647AFC3574C`，full_pose `04D4A5651B87E0E4`，
pose `2DED5435A66BCAEC`，root `309E8D0E0BEEB2CB`。
未改旧期望/阈值，未做本批全套 P4 脚本、新移动截图或十分钟性能测试。

## UE 门禁

遵循 `ue-diagnosing-plugin-build-load` 技能，在完整项目 Editor 构建/审计成功后
运行原生导出，没有依赖单插件 DLL 拷贝或修改 BuildId。技能提及的额外
superpowers 调试/完成技能当前未提供，使用源码、日志和测试核验。

首次编译失败日志保留：
`Saved/Logs/PluginBuild/20260910T234753851Z-6ca870530ce049aa8ade0260bbdca31e-ubt.log`。
UE 5.9 四元数乘整数 -1 发生 VectorSetFloat1 重载歧义，改为浮点 -1.0 后重新
完整构建，不绕过审计。

- 最终构建/审计退出零：`Saved/Logs/PluginBuild/20260910T235020743Z-9fa001a4e78a4b8c82a910840daa0131-ubt.log`。
- 构建指纹 `848B287B80EB5ED57BA738D464221067E2D606509C7AC16794E8DF67D1CB20B8`。
- 独立包 `artifacts/unreal/AlsDiagonalScalePluginValidation-20260911` 成功，之后项目插件审计再次通过。
- 两次缩放冷导出退出零，fixture 同 SHA256：`E033ED29E1A69484386D2CD3B68F782827198F9AB13DF74F466C690064567D51`。
- 旧默认 Mesh Space 冷导出与旧 fixture 同 SHA256：`22F7E22E7B93D0E57466F65196A072C2D58D64ABA3BBB360BD0D2A885D9008A3`。
- 仓库与项目部署源码同 SHA256：`86A41C9D6BE7D2BA5C656437035E274D9485657C506F8C65105C6A25B99F1578`。
- DataValidation 688 资产退出零，保留既有三条 ActionsComp/Navmesh 相关警告。
- 普通 Editor PID 37092 初始化成功，CloseMainWindow=True，正常关闭并取得退出码 0；仍有两条既有 AutomationTest 错误，不称为无错误启动。

本批构建、测试、导出和普通 Editor 进程均已结束。

## 下一步

推进完整 Cycles 的源更新权重、缓存生命周期与主蹲姿姿势执行，再接 Main 的
生产权重/初始化/动作反馈；随后继续最终曲线与动态分层、原 P5A 至 P7。
不要把已验证的 Stride、Lean、DiagonalScale 又退回成待从零实现的组件，
也不要将完整图与视觉验收改成仅要求这些组件测试通过。
