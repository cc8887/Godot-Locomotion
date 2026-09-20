# 原始来源上的完整附加动画求值（第 105 批）

日期：2026-09-12。工作区：`../GodotALS-p5a-events-actions`。
承接第 104 批的 GetBonePose 通用原始采样，增加 GetAnimationPose 的真实
additive 求值层。入口为 `scenes/tests/additive_animation_source_smoke.tscn`。
本批仍是 Main Movement 生产迁移的前置组件，默认 Demo 尚未使用新来源。

## 实际语义与实现

76 个来源中有 21 个附加动画：14 个 LocalSpaceBase、7 个 RotationOffsetMeshSpace。
这 21 个资产均为 AnimFrame、参考帧 0，但参考资产不同，分别涉及 Run Base、
Fall Loop 与 N Pose。因此不能将“使用零秒”本身解释为当前 21 个资产的错误。
本批修复能力在于完整原始采样、真实基准绑定、空间差分及曲线求值的组合。

`AlsAnimationPoseSourceSampler` 共享不可变 source bank，每个实例独占目标/
参考采样工作区。普通资产走 GetBonePose；有效 additive 则先分别采样目标
和参考，各自执行逐键 VB、插值、retarget 和 Root Lock，再进行差分。
参考资产本身即使带 additive 配置，也按 GetBonePose 读取，不递归差分。
提取上下文的 shouldRetarget、extractRootMotion、ignoreRootLock 同样传给参考。

- RefPose 使用完整目标骨架参考姿势，参考曲线为空。
- AnimScaled 用原始输入 CurrentTime / SequencePlayLength 的夹紧比例映射
  到参考长度，不能先对目标时间量化，也不能混用 DataModel 与序列长度。
- AnimFrame / LocalAnimFrame 使用参考帧 / sampled key count，并夹紧至
  [0,1] 后乘实际参考长度；分母不是 key count - 1。
- Local additive 保持局部平移/缩放及旋转差分；MeshRotation additive 只
  先将旋转转换为组件空间，平移和缩放仍保持局部空间。
- 曲线复用已有 `AlsLayeringCurves.Difference`，按名字并集相减；仅参考侧
  存在的名字保留为负值，两侧都缺席的名字保持缺席，存在的零值不丢失。

`AlsAdditiveReferenceTime` 实现四种参考时间策略并有边界回归。真实 UE 姿势
对照覆盖当前全部 21 个资产的 AnimFrame=0 配置；其他三种策略目前依据
所查原生源码与 Core 测试实现，不声称本批已经获得真实资产的逐骨骼原生对照。

直接参考：`AnimSequence.cpp` 的 GetAnimationPose、GetBonePose_Additive、
GetAdditiveBasePose、GetSequencePose、GetBonePose_AdditiveMeshRotationOnly；
`AnimationRuntime.cpp/.ispc` 的 ConvertPoseToMeshRotation/ConvertPoseToAdditive；
`AnimCurveTypes.h` 的 InitFrom 和 ConvertToAdditive。没有将 additive 再还原
为 full pose 后当作差分真值，也没有以单位姿势或相同资产播放身份代替参考。

## 对照数据

新增只读原生 `ReadRawAnimationPose`，使用 RAW BoneContainer 并直接调用
GetAnimationPose，禁用 RootMotionProvider 自定义属性采样。旧 ReadRawBonePose
接口保持原行为。`export_additive_source_poses.py` 校验第 104 批索引哈希、
源键及当前 UE 求值元数据后导出独立测试夹具，不改源文件或保存 UE 资产。

`tests/Als.Core.Tests/Fixtures/P3/v4_additive_source_pose_native.json` 含 21 个
资产、每资产 13 个时间 × 4 个提取上下文，共 1,092 个 79 骨姿势。每个样本
明确标记实际进入了原生 additive 分支。源索引、普通 RAW 姿势与 152 项时间
夹具的 SHA256 保持第 104 批值。

冷导出与普通 Editor 导出的所有数值、数组顺序和名字精确相同，使用
JsonNode.DeepEquals 完整核对。两份 JSON **并非字节相同**：转换后的曲线
字典在部分样本中呈现不同成员顺序，例如 Mask_Sprint/Mask_FootstepSound。
对照按曲线名字进行，不能把对象成员顺序当作曲线身份。

冷导出 SHA256：`FDA379A2B65B520CBF21EBFA5F1A10D2048DDA80BBFD7A53151D9BEB7D27C05A`。
普通 Editor SHA256：`77A3DAE6C5E4B63FDAFAC3C7E3A6E99FEF696420C9269E81E8490C4420BD2FA9`。

## 验证结果

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| Core 时间、局部/mesh 差分、曲线及原始采样回归 | 77/77 | additive-source-core-final.log |
| Godot 构建 | 0 警告、0 错误 | additive-source-godot-build-final.log |
| 真实 additive 原生对照 | 1,092 姿势 / 86,268 骨 / 832 曲线值 | additive-animation-source-smoke-final.log |
| 包含 additive 的共享来源并行 | 4 独立所有者，single/parallel 各 36,480 次采样，全部 TRS/曲线位值相同 | 同上 |
| 真实 76 来源热采样 | 4,864 次，托管分配 0 字节 | 同上 |
| 原 GetBonePose 回归 | 1,648 姿势，原误差保持；普通 RAW 热采样同样零分配 | additive-source-raw-regression.log |
| 默认生产 single/parallel | 各 600 帧、退出 0，完整原摘要保持 | additive-source-production-single.log / parallel.log |

Additive 最大位置误差 `6.599748e-7 m`、quaternion 欧氏误差 `3.5838008e-7`、
scale 误差 0、曲线误差 `8.940697e-8`，比较门限均保持第 104 批值。
并行覆盖完整 76 资产，普通资产也经过新包装器的普通采样分支。
零分配检查在创建与预热后测量，不包括主线程导入/构造成本，不代表 P7
十分钟性能预算完成。生产结果仍为 `DFA5F7A4F3296FA3`、完整姿势
`88AAD97FC78B8895`、角色根 `A4F6C26CBAB8A0E7`，通知 28，lag/stale 0。

## UE 构建与加载门禁

继续按 UE 插件构建诊断技能执行完整 Editor target → 冷导出 → 普通 Editor
→ DataValidation → 隔离 BuildPlugin → 包后审计，均退出 0。
首次构建因系统 dotnet 不含 .NET 10 未能启动 UBT，首错保留；随后显式使用
所选引擎自带的 .NET 10 runtime 完成，不安装或修改系统运行时。

最终 BuildId：`99c3b330-908c-40ac-9bd5-0ecf1e359e7f`。
构建指纹：`4A59B8E7BBDD073014F8D1B21052E6E6928EBFEEA1CDAC9606623C5E55FC569F`。
构建前缀：`20260912T082706070Z-2c0e820ac2fc420eb2a510a3927add86`。
DataValidation 为 688 资产、0 错误、3 条既有警告；普通 Editor 保留两条
既有 AutomationTest Condition failed。日志为 `artifacts/additive-source-*`。

隔离包：`artifacts/unreal/AlsAdditiveSourcePluginValidation-20260912-105`，
实际构建 UnrealEditor Win64 Development；不是游戏发布包。
仓库、UE 项目与包中的新头文件/实现 SHA256 各自一致，包后三插件审计 PASS，
相关 UE/UBT/UAT 和测试 Godot 进程已结束。

## 下一项与未完成范围

下一项把普通/附加来源共同接入 Main Movement/BaseLayer：Standing、Stop、
Crouching、Air/Jump、Detail/Lean/Landing 与 Turn/Action Slot；同时迁移
缓存、惯性化、QuickFeet/方向掩码和曲线 presence，修正 DiagonalScale
坐标常量，在最终物理写回才做 79→68 投影。保留现有来源时钟/身份及
候选提交、取消和晚期失败回滚，不能再次把同资产视作同一播放身份。

之后继续真实 Overlay/Aim/外围主图、LayerBlending 最终反馈及完整脚部
消费者，再验收双臂、侧身、起步滑步、交错步/换髋。原 P5A–P7 全部保留，
音频暂缓。新采样器通过不代表完整 AnimBP、实际 Demo 视觉或玩法验收完成。
本批未 commit/revert，原有工作树修改保留。
