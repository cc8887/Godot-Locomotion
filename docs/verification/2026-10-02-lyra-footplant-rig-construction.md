# Lyra FootPlant Construction 与接触夹具修正

2026-10-02。主目录直接推进，安装版 UE 5.8，忽略 5.8/5.9 小差异。当前关闭范围为原 Rig 初始 Construction 参数/参考层级；完整 Forwards Solve、真实 Godot 碰撞、Main73 生产接入和普通 Demo 仍开放。

## 原生地面夹具

上一批 footplant_rig_v1 虽然创建真实 UE Box，实际双脚扫掠全程未命中；骨盆偏移及坡度分支没有触发。因此上一批节点更新、输入绑定及缓存身份验证仍成立，但该轨迹不足以验证接触求解。原 JSON、探针和失败日志保持，不用新轨迹覆盖旧资源。

原 Rig TraceTypeQuery byte2 在 GASP58 转换为自定义 Traversable 通道，其默认响应为 Ignore。Box 的 BlockAll profile 没有显式覆盖这个自定义通道。新 Ground 探针显式设置全部通道 Block；每帧另做独立真实球体扫掠，验证开启地面命中、关闭地面不命中。保留原 Rig 查询及源参数，未替换为解析平面命中。

新增独立外部 AlsLyraFootPlantRigGroundLibrary，仅构建在 artifacts 插件包，不部署 GASP58、不修改引擎或保存资产。六条 OriginalMain/独立 OperatorBool × 30/60/120Hz 轨迹仍用 ALS81 目标及原 Rig 的完整程序。Ground v2 新增每条指令的真实 operand register/path、初始 Global 以及七控制器的 current/initial local/global offset。原字节码文本省略 Copy 子路径，现明确 404/405 为 Translation.X/Y→LeftFootOffset.X/Y，408/409 为右脚同序。

| 正接触夹具 | 输出帧数量 |
| --- | ---: |
| 更新 / UE 完整姿态 | 2520 / 2154 |
| 左脚 / 右脚命中 | 1947 / 1937 |
| 骨盆偏移非零 | 2112 |
| 坡度 / 坡度且蹲伏 | 1992 / 636 |
| 独立扫掠命中，含未求值帧 | 2400 |

两次独立 UE 进程实际退出0，各198条 Warning、0 Error；tag、动画依赖加载及工具集不可用等原环境警告保留。跨进程509628处缓存地址哈希按原双向身份语义比较，其余所有值精确，固定 native 字节不变。保护669包、793份旧 JSON。新 native SHA256 为 3ddc3ba4b9851a3e6cba3d5ae0b3c5ca64c30c98f75be86e4bb32417e5008ec9。

首次 operand 元数据探针调用 VM.GetExternalVariables 时，独立 ReadProgram 尚未绑定外部运行内存，UE 断言退出3；日志保留。改用 VM.GetExternalVariableDefs 的原编译寄存器顺序获取名称，随后完整重建、两次重采通过。没有跳过 operand 或外部属性路径。

## Construction 与 ALS 比例

LyraFootPlantRigConstruction 从原 Rig 初始资源加载91骨/7控制器的 current/initial local/global，以及控制 offset。按原401入口计算双脚初始 XY、左大腿/小腿长度，并设置 Body/Pelvis/Chest 的 global offset。保留控制器 local value；offset 与控制值分开存储，不能将控制 local value 当成普通父相对骨变换。

本机 RigHierarchy 的单父约束正向按 offset*parent、value*结果后 NormalizeRotation；单父逆向直接 GetRelativeTransform，不能额外正规化。SetControlOffset 保留 local value，初始 offset 变更同时同步 current；遵循 FRigComputedTransform.Equals 的原 .0001f 早退。此批固定原参考资源的三次 offset 写入均早退。随后变化控制器/offset专项已通过，Construction改为复用该层级helper，见 [最新内部层级验证](2026-10-02-lyra-rig-hierarchy.md)；完整Rig/生产调用仍开放。

原 Main73 bSetRefPoseFromSkeleton=false。Construction 在导入每帧动画之前使用 Rig 自带 Manny 参考骨；不能声称已自动换成 ALS 腿长：

| 参考骨架 | 大腿 cm | 小腿 cm |
| --- | ---: | ---: |
| 原 Rig Construction | 45.752037048339844 | 41.705421447753906 |
| ALS 目标参考骨 | 42.57203674316406 | 40.19668960571289 |

ALS 数值来自目标 calibration referencePose 的 calf_l / Foot_L 局部长度；对应父序分别为 Thigh_L / calf_l，scale均1。保留 ALS 网格/蒙皮的路线可继续，但最终 Rig 的目标比例策略仍需完整求解与视觉验证。先保持原节点配置形成可比较的移植基线，再单独以 UE 目标配置验证比例适配；不能静默改开启 ref-pose 设置后仍声称与原配置等价。

## Godot 验证

实际 Debug 与 ExportRelease Optimize 两次 Godot 进程退出0；构建和运行均0错误0警告。六条轨迹的独立初始 Construction 及重复执行共12次，98个元素的 current/initial local/global和控制 offsets共5040组TRS精确同UE；腿长按float逐位、双脚偏移按double精确比较。2520帧确认 Construction 四项变量在访问、隐藏、仅更新和重新初始化后的保持。

参考输入只来自独立 ReadProgram 初始资源，不把每帧 UE 输出作为计算输入。当前变换对照覆盖初始参考 Construction；中途重初始化仅比较上述四项变量，不能扩展成完整层级重初始化或 Forwards Solve 验收。接触计数是 UE 夹具证据，Godot 尚未执行地面求解。第一次 C# 将原 bool literal 当作字符串读取失败，原日志保留；已按 JSON bool 修正，无阈值放宽。

Optimize实际加载三份ExportRelease DLL，结束恢复六份Debug DLL/PDB并逐文件SHA验证。汇总工具 tools/verify_lyra_footplant_rig_construction.py 实际退出0；结果在 artifacts/lyra-analysis/lyra-footplant-rig-construction-verification.json，明确 fullRigPoseAccepted=false、production=false。

## 后续依赖

下一步变化控制 offset/父约束及目标骨输入映射专项→真实球体扫掠和五处独立SpringV2/AlphaInterp历史→骨盆/坡度/双腿IK完整 Forwards Solve→部分权重local additive→实际Main73角色事务与一次skin发布。正接触 Ground v2 取代旧无命中轨迹作为后续完整姿态对照的依据。

生产 Provider 换类/多角色、Notify/Montage队列、RootMotion碰撞消费、普通Demo、平台/握持观感与性能，以及通用 Animation Interface 的 self/Unlink/多组分支继续开放。整个 Lyra 移植目标未完成。
