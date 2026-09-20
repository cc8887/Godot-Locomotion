# 完整移动资产闭包与通用原始采样（第 104 批）

日期：2026-09-12。工作区：`D:/GodotALS-p5a-events-actions`。
本批承接原 P3/P4 完整性修复：通用采样器和完整移动来源通过原生对照，
实际入口为 `scenes/tests/raw_animation_source_smoke.tscn`。默认 Demo 仍由
原 BaseLayer/FBX 路径输出；不能据此关闭上身、起步滑步或换髋问题。

## 来源与求值边界

`RawSequenceSourceRequest` 从实际共享图绑定生成闭包：75 个 player、109 个
sample 引用、76 个直接动画资产。导出器递归包含 additive base，实际基准
已在这 76 个资产内，仍保留 dependencyOf 边。共 5,168 条原始轨道，一套
68 实体骨 / 79 逻辑骨架；55 个普通、14 个 local additive、7 个 mesh additive。

正式索引 `assets/config/v4_movement_source_inputs.json` 绑定 manifest digest、
binding digest、实际根资产集合、逐文件 SHA256 及完整骨架元数据。
`assets/config/raw_sequences/` 保留原始 float 关键帧、轨道存在性和真实求值
设置；不经过 FBX Euler。骨名/曲线名遵循 UE FName 大小写不敏感身份，导出
仍保留原拼写。编译器验证时间、骨架、闭包、哈希、参考姿势选择和受支持策略。

源数据与测试姿势严格分离：生产加载器不读取
`tests/Als.Core.Tests/Fixtures/P3/v4_movement_source_pose_native.json`。
该夹具直接调用 `UAnimSequence::GetBonePose(forceRaw=true)`，即使源资产带
additive 配置，输出也仍是差分前姿势。RootLockFirstFrame 是原生
FRootMotionReset 使用的固定输入，来自 ExtractRootTrackTransform(0)，不以
原始首键替代它；其内部可选择压缩根轨道。序列真实长度与 DataModel 长度
分别保存，供后续 additive base 时间公式使用。

通用采样顺序为：调用者给定秒数 → 原生 frame-time/取键 → 两个原始键各自
生成缺失虚拟骨 → shortest quaternion nlerp → tracked 实体骨平移 retarget
→ 完整根 TRS 锁定。曲线使用同次采样的量化时间，保持真实 presence。
缺轨从所选参考姿势读取；显式 VB 轨道不被覆盖；不从混合结果事后重建 VB。
每个所有者独占采样工作区，资源键表不可变，采样器不推进时钟/同步/通知。

时间边界的初测暴露当前 UE/MSVC `/fp:fast` 的行为差异：头文件中的
`(subframe + .5f) - .5f` 在实际编译产物中被折叠，C# 额外执行它会再次舍入。
修正后按 152 项原生样本逐位比较 Alpha 和 double SampleTimeSeconds，未
修改夹具或放宽精度。这一契约针对当前原生构建，不能声称所有 UE 编译配置
都会产生相同浮点行为。首错保留在 `raw-sequence-core-native.log`。

## 验证结果

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| Core 通用采样及原 BasePoses/虚拟骨/retarget | 103/103，包含 152 项原生取键逐位对照 | raw-sequence-core-regression.log |
| Import 新旧源数据/图/参考配置 | 182/182，含完整真实 76 资产闭包 | raw-sequence-import-regression.log |
| Godot 构建 | 0 警告、0 错误 | raw-sequence-godot-build-final.log |
| 原生完整来源对照 | 1,648 姿势、130,192 骨、2,812 曲线值 | raw-animation-source-smoke-final.log |
| 共享来源并行 | 4 独立线程所有者，单/并行各 36,480 次采样；全部 TRS/曲线位值摘要相同 | 同上 |
| 默认生产回归 | single/parallel 各 600 帧；最终完成标记齐全且进程结束 | raw-sequence-production-single.log / parallel.log |

所有源资产覆盖首帧、亚帧、中间、尾部附近和末帧，各含不重定向、重定向后
锁根前、资产锁根及 Root Motion 提取锁根四种上下文；两项代表资产增加密集
时间样本。最大位置误差 `4.702727e-7 m`、quaternion 欧氏误差 `2.3466372e-7`、
scale 误差 0、曲线误差 `5.9604645e-8`。原门限依次保持 `2e-5 m`、`2e-5`、
`5e-5`、`2e-5`。缺席曲线逐项检查，无全局补零。

生产两模式结果 `DFA5F7A4F3296FA3`、完整姿势 `88AAD97FC78B8895`、
角色根 `A4F6C26CBAB8A0E7`；通知 28，lag/stale 为 0。三项最终 Godot 命令
的工具等待被中断，随后通过终态日志及 Win32 进程检查确认完成，没有重启
或将观察中断当成运行失败；本记录不虚构丢失的进程返回值。

## 原生导出与构建

新增 `AlsSourceAnimationLibrary.h/.cpp`，保留第 103 批 API 及正式数据。
使用 UE 插件构建诊断技能要求的完整 Editor target 与统一身份审计，冷导出、
普通 Editor、DataValidation、隔离 BuildPlugin、包后审计全部退出 0。
普通 Editor 与冷导出的 76 文件、索引、姿势及时间夹具逐字节一致；未保存资产。
DataValidation：688 资产、0 错误、3 条既有警告；普通 Editor 仍有两条既有
AutomationTest 日志错误，不据此宣称全项目所有日志无错误。

新 BuildId：`56ff4dac-58a1-4e28-b4ab-417ab8f938d1`。
构建指纹：`109526917C0ACC7AF06F1703DDB7BBD4D097C681AA65E76C942322E998FDA65E`。
构建前缀：`20260912T075757468Z-dd1a07d1951640d09a7f27cbc4854a6c`。
隔离包：`artifacts/unreal/AlsMovementSourcePluginValidation-20260912-104`，
实际编译目标为 UnrealEditor Win64 Development，不是游戏发布包。

首次编译使用不可访问的 protected 接口，已改为公共 DataModel 检查。引擎
NetCore 重建后身份改变，构建门禁准确拒绝旧 receipts；核实为生成物后将
9 项项目输出恢复性隔离至 UE 项目
`Saved/BuildReceiptBackup/20260912T075757236Z`，没有删除源码或修改 BuildId。
首次导出因 pelvis/Pelvis 大小写误判失败，现按 FName 核对。所有首错保留于
`artifacts/movement-source-*` 日志。

| 产物 | SHA256 |
| --- | --- |
| 索引 | FDC615D8C2C8FAA8C14BACE29F2408432C51ED4C1C003CD77E757C6C2091C5D4 |
| 姿势夹具 | F624FEA56865031A4A9590AA0BE474EA6F3E00A8958A023AE64A68CE67FF1171 |
| 时间夹具 | 6887BA10166CC7A2CCB87A6B0D1FD6D96D61E615129A152234BBD88216DCD948 |

## 紧接的生产迁移

1. 补通用 GetAnimationPose additive 层：按 RefPose/AnimScaled/AnimFrame/
   LocalAnimFrame 求参考时间，目标与参考各自完整采样后进行 local/mesh
   差分和曲线差分；不能继续统一使用 0 秒基准。
2. Main Movement 所有源入口统一为新银行：Standing/Stop/Crouch/Air/Jump、
   Detail/Lean/Landing 和 Turn/Action Slot。时钟、75 player/109 sample
   身份和既有候选事件事务继续由原所有者负责。
3. 缓存、惯性化、骨掩码及 QuickFeet 一起保留完整 79 骨；索引按逻辑拓扑。
   Crouching DiagonalScale 常量当前是 canonical 空间，必须转为实际 FBX
   骨空间，不能只替换骨 ID。Main/Crouch/Detail 曲线接口全程携带 presence。
4. 仅在 AlsProductionMovementRuntime.WritePose 和旧 Cycle WriteOutput
   两个物理写回出口投影为 68 骨，包括初始化/Restore 路径。后续原生
   LayerBlending 接在 79 骨侧，不在最终输出后重建虚拟骨。
5. 真实 Overlay/Aim/外围主图、最终曲线反馈和脚部 IK/Lock/pelvis 接入后，
   再以真实输入和多帧画面验收上身、滑步和换髋。P5A 通用动作、P5B 道具、
   P5C Mantle/Roll/Root Motion、P6 物理恢复/Camera、P7 十分钟均未关闭。

未 commit/revert，保留原有工作树修改。
