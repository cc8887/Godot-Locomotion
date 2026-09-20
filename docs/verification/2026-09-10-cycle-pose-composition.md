# 第六批：Cycle 局部姿势合成

日期：2026-09-10。工作区：`D:\GodotALS-p5a-events-actions`。
本批有实际 Demo 接入，不只是标量权重或源数据合同；完整目标仍未完成。

## 源证据

读取本地 UE 5.9 源码及已有 ALS V4 AnimBP 图导出：

- `Engine/Private/Animation/BlendSpace.cpp` 的 GetSamplesFromBlendInput / GetAnimationPose_Internal：
  权重降序排序，剔除小于 0.00001 的样本后归一化；姿势采样顺序就是排序后的缓存顺序。
- `Core/Public/Algo/IntroSort.h`：最多八项走非稳定选择排序，同权重顺序也须保留。
- MultiWayBlend 按 F/B/L/R 引脚顺序混合，剔除小于等于 0.00001 的通道，输出旋转归一化。
- `Engine/Private/Animation/AnimNode_StateMachine.cpp`：每层以此前中间姿势作为源，
  用 FTransform 的加权与 AccumulateWithShortestRotation 累积，整条栈结束才归一化。
  不能每层 Quaternion.Lerp/Slerp，也不能直接把全部叶动画权重混为一个平面。
- 同函数的曲线使用普通 transition alpha；BlendProfile 不将曲线改成腿部权重。

这些运算适用于当前 Cycle 的 standard transition logic。Custom blend curve 是 alpha
曲线，不等于 Custom Transition Graph；未实现的任意自定义过渡图不因此获得支持。

## 实现与所有权

`AlsPoseBlender` 实现不归一化的最短路径累积及显式归一化。
`AlsStandingCyclePose` 依次合成六方向四角姿势、六个方向状态和活动过渡栈。
`AlsWalkRunBlendSpace.SampleEvaluation` 补齐原生排序、同权重顺序和剔除规则。

`AlsCyclePoseSampler` 在每个角色自己的资源上采样：静态 Pose/Idle 缓存在初始化，
Walk/Run/Sprint 使用各自现有的映射时间；不改源动画资源。
通过单关键帧的固定拓扑载体将合成后的局部姿势送到原动画图的 Locomotion 入口，
再经过既有 Lean、Turn/Rotate 和后续 P4 骨骼处理，而不是在整个图之后覆盖骨骼。

载体 `als_cycle/pose` 属于派生输出，不是 ALS 源动画或事件身份。
真实 26 个采样源及其时间仍独立存在；P5A 的真实身份、Sync 和事件接线尚未完成，
后续不得将这些源身份压成一个载体身份。

候选帧中计算并写入载体，Controller finalize 后提交派生姿势快照，失败时恢复。
单独保留输出快照覆盖“此前尚未进入 Cycle / 已离开 Cycle”的恢复情况，
不能只依赖当前 committed 分支是否带 Cycle 数据。首次候选帧失败也有回归用例。

原型曾因逐骨骼修改关键帧触发大量 Resource changed 回调，在长循环中崩溃。
修复后载体的轨道、路径、关键帧数量、时间全部固定；注册后阻止值更新发出资源变化
信号，仅更新已有键值，不反复重建 mixer 缓存。此限制是设计约束，不能在这个载体上
运行时新增/删除轨道或关键帧。修复后的九组长循环、销毁/重建及单/多线程测试通过。

## 原生探针

按 UE 插件构建检查技能执行完整 Editor target 构建与插件审计，之后运行只读
`AlsPoseBlend` commandlet；不保存 UE 资产。

构建 fingerprint：`258C22CF7021EEB6F75DAB4948751914F7499D14E029F53C490F4F190ED93733`。
日志：`artifacts/pose-blend-native-20260910.log`，退出码 0，汇总 0 errors / 0 warnings。

```text
ALS_POSE_BLEND_OK cases=64 grids=384 assets_saved=0
```

fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_pose_blend_native.json`。
SHA256：`312E2D205094CF9995021FAE0D9A27970DCEB8C9DD8A995D41E75DC208E0EB21`。

64 个合成局部姿势案例含 400 层过渡，共 992 个混合/中间/最终变换对照；
另有六个实际 WalkRun 资源共 384 个排序/阈值案例，包含相等权重和阈值边界。
探针直接使用 UE FTransform 运算与 UBlendSpace 采样，不是完整 AnimBP 回放，
不能证明源动画压缩、导出采样、状态事件和所有图节点已经等价。
未运行常规 GUI Editor 重启、数据验证和打包，因此不作插件发布验收声明。

## 验证结果

- Core Locomotion：297/297，包含上述原生对照及错误逐层归一化的反例。
- Godot 构建：0 warnings / 0 errors。
- StandingCycleSmoke：30/60/120 Hz 各三个起始相位，63 次全骨骼载体输出对照，
  9 次首次采样回滚、9 次普通回滚、9 次中断回滚，36 次方向组合；稳定和活动路径均 0 B 托管分配。
- 中性换髋仍为 9 次，许可等待 231 帧，最大活动过渡数 11。
- 新 Cycle 单/多线程各 180 帧：result `BE7BC81EDF4F1B91`，
  full_pose `B684F207D0AC5DA1`，root `309E8D0E0BEEB2CB`，lag/stale 为 0。
- 新 Cycle 两种模式的 late_transaction 失败注入均通过。
- 旧路径 `verify-p4-pose.ps1` 通过 graph / pose / 两种 foot placement / 两种 late transaction；
  该结果只作旧路径回归，不代替新 Cycle 验证。

三组回放均 720 帧、120 张截图，输出位于 `artifacts/pose-composition-*`：

| 回放 | 最大单帧脚旋转 | 起步低位脚位移峰值 |
| --- | ---: | ---: |
| strafe | 14.197° | 5.0096 cm |
| rapid | 12.616° | 不以此回放评估固定镜头横移起步 |
| run | 12.046° | 6.2287 cm，左脚 5.4584 cm |

已检查横移及快速移动的多帧图像，角色资产与姿势变化正常；现有旋转/直立门禁通过。
固定镜头起步仍约 5 cm，上身动态分层仍缺失，不宣称观感已达到原版。
相机与输入文件保持第五批记录的 SHA256，没有更改人工认可的控制方式。

## 下一步

继续 ShouldMove / NotMoving-Moving-Stop / Feet_Position / Lock-Plant，补 Mesh Space
落脚混合、状态重置和源状态事件，再接 P5A 原定同步、通知及 Action 主流程。
临时外层 Idle/Sprint 混合、固定 Walk F marker leader、重复 Stride/PlayRate/Phase，
图内 ModifyCurve/YawOffset、动态 Layering 和后续 P5B/P5C/P6/P7 仍未完成。
