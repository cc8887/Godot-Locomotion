# Control Rig 双骨 IK 与层级写回

日期：2026-09-13，第一百六十六批。属于原 P4 完整脚部控制链。

## 实现

新增 `AlsRigTwoBoneIk`，对照 UE5.9 的 `FRigUnit_TwoBoneIKSimplePerItem`
与 `FControlRigMathLibrary::SolveBasicTwoBoneIK`：

- 以参考骨架位置和当前/参考缩放计算 float 腿段长度；支持显式长度。
- B 初始旋转、缩放复制 A，只保留 B 的当前位置，不能直接使用当前 B 的旋转。
- 分别沿左右腿主轴与次轴对齐；伸直时从 pole 确定次轴方向，保留次轴权重。
- pole 所在空间只应用旋转/位移，不应用该空间的缩放；Location/Direction
  的转换不同。无空间时按实际源码传入 pole，未额外加 root 位置。
- Weight <= SMALL_NUMBER 不写回；部分权重混合组件旋转，再使用已求解的
  局部位置重建关节，不能用 V4 的局部姿势 alpha 混合替代。
- 可复用候选组件数组按父先顺序写回，保留传播开启/关闭时的子骨行为；
  调用者提供 scratch，函数不拥有提交状态。

`AlsTwoBoneIk` 提取显式长度的位置求解入口，V4 原入口仍根据当前骨段测量
长度并执行原来的旋转修正。此次提取没有将新 Rig IK 默认接入 Demo。

## 原生与回归证据

新增实际节点探针 `AlsRigTwoBoneIkProbe.cpp`。临时层级包含三段链、脚端
子骨、大腿侧支和小腿侧支，测试左右轴、7 档权重、3 档次轴权重、6 种
目标/缩放/空间情形及传播开关，共 504 组、3024 个骨骼结果。

冷启动与普通 Editor 输出完全一致，正式夹具为
`tests/Als.Core.Tests/Fixtures/FootIk/native_rig_two_bone_ik.json`，SHA256：
`DC76418BECC9E172F76900C3BCCC9F219D24CEF37855AA6F6AD1CEBED4BDEF82`。

Core 新旧专项 Release 62/62 通过，包含 504 次候选重试、144 组跳过写回、
216 组部分权重以及前批 1440 帧脚部节点对照。

| 实测项 | 最大误差 |
|---|---:|
| 位置 | 4.128624940553091e-6 cm |
| 旋转 1−绝对四元数点积 | 1.176836406102666e-14 |
| 缩放 | 1.1102230246251565e-16 |

最大误差出现在 Weight=1e-7 的低权重案例，原门槛保持。
记录：`artifacts/rig-ik-166-tests.log`、`artifacts/tests/rig-ik-166.trx`。

V4 手/脚控制器 Import 专项 70/70 通过，见
`artifacts/rig-ik-v4-regression-166-tests.log`。Godot Debug 优化构建零警告、零错误。
生产 single / parallel 各 960 帧通过，结果 digest `DB9FEFC95ADA4B15`、
完整姿势 `765E1669B4501131`，与上一批相同。日志为
`artifacts/rig-ik-production-{single,parallel}-166.log`。
这些生产回归证明提取公共位置求解器未改变既有路径，不代表新 Rig IK 已接入。

完整 Editor 目标构建和项目插件审计通过；BuildId
`9161b624-d10d-497c-bdea-7b4acb7ae7f3`，fingerprint
`2AB931B6EB14F588F88FBEC624093C1F444E54B824644286E10A988281B7564A`。
冷启动、普通 Editor 导出/退出均为零。DataValidation 退出零，0 error(s)、
3 warning(s)。已有普通 Editor 的 AutomationTest 启动报错不算本节点回归消失。
隔离插件包（含 ALS 依赖）构建成功，ExitCode=0；DLL、PDB、modules 文件
齐全，位于 `artifacts/unreal/rig-ik-166-package/`。探针/头文件两个源码副本
相同，冷/普通 Editor/正式夹具三份数据一致，未部署隔离包的 DLL。

## 整链仍缺什么

已扩展原图检查脚本，报告 `artifacts/refactored-foot-graph-166.json` 包含
RefreshPelvisOffset、RefreshFootOffset 和 TraceFootOffset。确认：

- 骨盆使用独立 `SpringInterpV2`，Strength=2、CriticalDamping=1、
  TargetVelocityAmount=0、InitializeFromTarget=true、UseCurrentInput=false，
  偏移 clamp [-30,40] cm，不能用本批腿部弹簧或 V4 VInterp 直接代替。
- FootOffsetTrace 从目标 XY 的组件空间 Z=50 到 Z=-80 探测，界面 FootHeight 默认 13.5 cm。
  第 167 批进一步确认该引脚有变量注入，运行值来自初始化时左脚参考全局 Z，
  不能将显示默认值视为实际脚高，见 `2026-09-13-refactored-foot-environment.md`。
  它按 VM 空间法线判定坡度，并用 height/normal.Z−height 修正坡面高度。
  与 V4 的 root/world 法线偏移和位置平滑不是同一输入合同。
- IK 权重读当前 FootLeftIk / FootRightIk，动态腿部约束读 PoseMoving。
  仍需验证 V4 资产曲线的对应方式、图节点执行/失去相关性时的历史，以及
  地面采样进入多线程帧快照的阶段；不能将缺失曲线默认为理想值。

下一步严格编译完整图合同、补骨盆和专用探测边界，再把已验证的节点
按原顺序接入完整帧 owner，重跑移动接触、平台和多帧截图。
新节点尚未生产接入，原第 616 帧视觉失败没有关闭，默认完整入口、P3/P4
整角色验收与 P5A–P7 继续推进，音频仍暂缓。
