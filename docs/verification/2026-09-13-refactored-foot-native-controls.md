# Refactored 脚部位置、膝目标与原生节点对照

日期：2026-09-13，第一百六十五批。承接第 164 批，属于原 P4 完整脚部链路。

## 已落地

新增 `AlsFootOffsetLocationModel`，保留原 `ApplyFootOffsetLocation` 的顺序：

1. 以骨盆高度限制最大脚部偏移，再以 PelvisOffset 限制偏移下界。
2. 首次初始化直接设置偏移，但重置 spring valid；下一次非零 delta 调用
   再由 UAlsMath 初始化弹簧并直接设置当次目标。
3. 后续使用原 float 弹簧，包含无刚度、无阻尼、欠阻尼、临界及过阻尼分支、
   目标速度量与小 delta 门控。
4. 插值之后才限制最大腿长，限制结果不反写弹簧历史。向量使用 double，
   腿长乘法和弹簧使用 float。

新增 `AlsFootPoleModel` 与向量平滑候选状态，保留 A=B、A=C 和共线的不同
处理。求解失败时保留此前全部成功输出，包含膝位置与投影；首次失败使用
原节点 ForwardVector 默认方向。平滑首次直接初始化，重入由 owner 显式重置。

上述状态与上一批脚踝旋转均为纯输入/候选输出，尚未接实际完整帧 owner。
本批没有把候选模型误称为已完成生产提交/回滚接线。

## 实际原生证据

新增 `AlsFootControlsProbe.cpp`，在临时 URigHierarchy 上调用真正的
FAlsRigUnit_ApplyFootOffsetLocation、FAlsRigUnit_ApplyFootOffsetRotation、
FAlsRigUnit_CalculatePoleVector 与 FAlsRigVMFunction_DamperExactVector。
原始骨架不保存，未修改 ALS 源码；探针与插件依赖同步到工具目录和 UE 项目。

导出覆盖 30/60/120 Hz × 阻尼 0/0.5/1/2，共 12 组、1440 帧：连续目标变化、
冷初始化、再次初始化、delta=0、曲线驱动参数、目标速度量、频率=0、腿长=0、
长距离脚目标、退化膝几何、零/反向法线及法线零半衰期。

正式夹具为 `tests/Als.Core.Tests/Fixtures/FootIk/native_refactored_foot_controls.json`。
冷启动、首次普通 Editor 和再次普通 Editor 的 JSON 完全一致，SHA256：
`AF915A0FFAF08364FC0B617CDE7874300902F266E0834AF56ECD352817B7DCF1`。

第一次对照失败 8 组：按文本展开多项式的 C# 计算与 UE 优化编译后的 Horner
计算顺序不同，平滑 alpha 相差一到两个 ULP，累积出现法线/膝目标差异。
将 `AlsRefactoredRigMath.InvExp` 改为与实际编译结果一致的嵌套乘加形式后，
原阈值保持，60/60 项 Release 测试通过，其中 1440 帧均做丢弃后同帧重试。

| 实测项 | 全部案例最大差异 |
|---|---:|
| 最终脚部位置 | 0.0000457763671875 cm |
| 内部插值偏移 | 0.0000457763671875 cm |
| 内部弹簧速度 | 0.0029296875 cm/s |
| 旋转的 1−绝对四元数点积 | 1.1102230246251565e-16 |
| 平滑法线 | 0 |
| 膝位置/投影/方向及平滑目标 | 0 |

弹簧位置/速度的最大误差来自无阻尼 30 Hz 案例；正式图使用阻尼 2。
正式图阻尼 2 三档频率的最大位置误差为 0.00000762939453125 cm。
保留 `foot-controls-native-165-tests.log` / `foot-controls-native-165.trx`
的首错；最终记录为 `foot-controls-native-165-fixed-tests.log` 和相应 TRX。

## 构建与加载

完整项目 Editor 目标构建、四个项目插件的依赖审计通过，BuildId 为
`d1c24df9-49f4-42ad-a6e3-8c496cedddda`，输入 fingerprint 为
`8DC11821710F4FE6FEB5A90763B7FFD2B374FF85116DAE91F44D6151EB979A2D`。
Godot Debug 优化构建零警告、零错误。UE DataValidation 退出零，记录
0 error(s)、3 warning(s)，详见 `artifacts/unreal/foot-controls-165-validation-console.log`。

首次普通 Editor 导出成功、退出返回 -1073741819；日志到正常退出结束，
本次日志未给出足以定位根因的故障栈，不声称已经修复引擎崩溃。
移除脚本启动回调内的立即退出请求后，重新启动、重复导出与退出返回零。
两条已有的 AutomationTest `Condition failed` 启动日志仍保留。
第二次日志为 `artifacts/unreal/foot-controls-165-editor-repeat.log`。

隔离 `BuildPlugin`（含 ALS 依赖）成功，产物位于
`artifacts/unreal/foot-controls-165-package/`，DLL、PDB 与 modules 文件齐全；
`foot-controls-165-package.log` 记录 `BUILD SUCCESSFUL` / ExitCode=0。
构建器曾因内存压力终止并重试一个编译进程，最终成功，原记录保留。
探针/头文件/Build.cs 的两个源码副本相同；两个 .uplugin 的插件依赖声明相同，
UE 项目原有 Installed/EngineVersion 等项目特定字段保持，未做整文件覆盖。

## 未完成及继续顺序

上述探针是实际节点在受控输入下的对照，没有执行完整 AnimBP、TwoBoneIK
或真实移动/物理接触，不能证明整角色视觉已通过。

下一项：严格编译 RefreshFootIk / ApplyFootIk 的真实图合同，迁移
TwoBoneIKSimplePerItem 对左右主/次轴、权重和子骨传播的语义；补实际地面
采样/骨盆输入与曲线映射，再把全部历史按顺序接入完整帧事务。
原图左腿主/次轴为 (-X,+Y)，右腿为 (+X,-Y)；权重读 FootLeftIk /
FootRightIk，移动限制读 PoseMoving。不得未经验证就用 V4 同名近似量替代。

之后重跑起步、左右换向、停步、转身、斜坡、平台及多帧截图，确认后再
默认启用完整入口。第 616 帧突转沿用旧失败，尚未重测或关闭。
原 P5A 至 P7、完整上身/接触/人工/性能验收继续保留，音频暂缓。
