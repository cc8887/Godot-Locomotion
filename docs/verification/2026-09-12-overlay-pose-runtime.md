# Overlay 完整姿势图与惯性化前原生对照

日期：2026-09-12，第一百一十五批。工作区 `D:/GodotALS-p5a-events-actions`。

## 本批完成的内容

在上批五个状态机和 148 个来源基础上，实现 Overlay 完整姿势图的定义、
初始化、更新和求值。正式 `AlsMovementGraphDefinition.Load` 现加载该定义，
独立运行时使用真实资产完成原生对照；默认 Demo 的最终上身仍未接入。

| 节点 | 数量 | 保留的行为 |
| --- | --- | --- |
| SequenceEvaluator / SequencePlayer | 122 / 26 | 独立来源身份、初始化代次、更新上下文；采样不推进时钟 |
| TwoWayBlend | 43 | 实际曲线/变量输入、映射/限幅、独立插值历史、相关性及一个重置子节点的分支 |
| MultiWayBlend | 24 | 原 pin 顺序、归一化、相关性阈值、零总权重时参考姿势 |
| ApplyAdditive / ApplyMeshSpaceAdditive | 23 / 12 | 各自空间、实际 Alpha、原顺序曲线累加 |
| BlendListByInt / BlendListByEnum | 7 / 2 | 两/四子节点、逐目标时长、被中断权重、零权重旧子节点更新、初始化及 inactive 标记 |
| ModifyCurve | 4 | 手臂、脊柱、头部附加层曲线的原值及 presence |
| StateMachine / StateResult / Root / Inertialization | 5 / 25 / 1 / 1 | 嵌套机器、状态内求值缓存、转换栈的逐骨顺序、外围惯性化候选 |

共 295 个姿势节点；conduit 没有姿势根，不算一个 StateResult。
三个 BlendList 使用惯性化请求、五个 BlendList 使用 ResetChildOnActivate；
另一个 TwoWay 也重置新激活的子节点。普通节点按访问更新，不能把引用
同一资产的节点合成一个播放身份。

运行时以每角色两份预分配状态保存候选/提交历史。节点插值、BlendList、
嵌套状态机和外围惯性化一起提交；求值失败不发布部分输出，同帧取消后
可以重试。来源更新、同步上下文、生成通知和惯性化请求交给外围事务
参与者；这些接口不等于 gameplay 通知分发已经完成。

主要实现为 `AlsOverlayPoseDefinition.cs`、`AlsOverlayPoseWeights.cs`、
`AlsOverlayPoseRuntime.cs`（`src/Als.Core/Locomotion/`），以及
`src/Als.Import/Compilation/AlsOverlayPoseCompiler.cs`。
验证入口是 `src/Als.Godot/Animation/OverlayPoseSmoke.cs` 和
`scenes/tests/overlay_pose_smoke.tscn`。

## 修正的移植取值边界

编辑节点导出的结构默认值不能替代实际引脚值。局部附加节点结构 Alpha
默认为 1，实际引脚包含 0.25、0.5、0.75；BlendList 的结构时长 0.1 秒
也不等于引脚上的 0.75、0.5、0.3、0.2 或 0 秒。编译器现在按真实连线
和引脚读取这些值，结构字段只用于相应节点策略。

权重还包含上一已提交最终曲线的 Weight_Gait/Weight_InAir、BasePose_N/
BasePose_CLF、VelocityBlend 的四个字段、RelativeAccelerationAmount、
LandPrediction 和 OverlayOverrideState。保留 UE 双精度向量分量在动画
float 引脚处的转换，以及各节点独立的 FInterp 历史。

例如 Rifle Relaxed 的一个分支把 0–0.25 映射到 0–1，使用上升速度 20、
下降速度 0.5，并在子节点激活时重置。其他分支还存在上升速度 0、下降
速度 5/10 的非对称规则。不能统一成一个平滑常数。

## 原生对照及验证

在现有 UE 原生状态探针增加可选 capturePose 模式，运行实际 Overlay
顶层机器、所有被访问的子图和真实惯性化节点。被动取样节点记录惯性化
之前的输出，同时保留完整最终输出。探针只作用于临时实例，未保存资产。

最终轨迹为 54 组、938 帧，其中 52 隐藏帧、886 个 79 骨姿势。补充了
Torch/Binoculars 同时满足 Aiming 和 Weight_Gait=0 的静态用例后，实际
求值覆盖全部 148 个来源。该对照读取原生采样时间以隔离姿势图语义，
因此不证明跨图 Sync Runtime 或正式来源时钟已接入。

惯性化前 886 帧逐骨、曲线值及 presence 对照通过；同帧取消/重试全部
一致。采样骨骼来自真实 79 骨来源库，位置使用 Godot 米制。

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| 冷启动惯性化前对照 | 886 姿势、148/148 来源通过；位置最大误差 7.6685126e-7 m，四元数距离 3.0172552e-7，曲线 5.9604645e-8 | `overlay-pose-before-inertial-native-final.log` |
| 普通 Editor 惯性化前对照 | 同样 886 姿势通过，误差相同 | `overlay-pose-before-inertial-editor.log` |
| 新专项测试 | 6 通过；四独立所有者一致，10,000 次 Prepare/Evaluate/Commit 热调用零分配，含晚期求值失败不发布 | `overlay-pose-unit-tests.log` |
| Release Import 全集 | 1,919 通过，1 项既有跳过 | `overlay-pose-import-regression.log` |
| Release Core Locomotion 命名空间 | 519 通过 | `overlay-pose-core-regression.log` |
| 正式 Worker single / parallel | 各 600 帧通过；result EAAF62E4D0A80A76、full pose 3103E3B355BF1F3B、事件 28、lag/stale 0 | `overlay-pose-production-single.log`、`overlay-pose-production-parallel.log` |
| Godot Debug Optimize=true | 0 错误、0 警告 | `overlay-pose-godot-build-final.log` |
| UE 整项目构建及插件审计 | 通过 | `overlay-pose-native-build-2.log` |
| UE 资产验证 | 0 错误、3 条既有告警 | `overlay-pose-data-validation.log` |
| 隔离插件打包及打包后审计 | 退出码 0、三个项目插件审计通过 | `overlay-pose-plugin-package.log`、`overlay-pose-post-package-audit.log` |
| 既有 Overlay 状态探针复验 | 重新导出退出码 0，与第 114 批夹具 SHA256 相同 | `overlay-state-after-pose-export.log` |

Import 跳过项仍为
`AlsLayerBlendingRuntimeTests.NormalEditorRepeatsTheConsumedGraphAndInputSemantics`。
上述 Worker 结果证明已有移动路径保持，不是最终上身接入的证据。

正式夹具 `tests/Als.Core.Tests/Fixtures/P3/v4_overlay_pose_native.json` 的 SHA256：
`3682BAFB9114F1C7604F4D025A7DA75CFDF43625637C5B45F371283A5DC46D22`。
普通 Editor 重复输出 SHA256：
`BA710EC5C1955F64E2623EE8C581B9C202DFA133CBE8EC18C888ED2C95DE1D24`。
两者不是字节相同：递归比较发现值一致、曲线对象字段顺序不同。两个
独立输出均通过上述消费对照，不能把字段顺序差异说成动画数值差异。

新增姿势捕获之后，既有状态探针重新导出的 SHA256 仍为
`95FCFD755ECFF6F64BB4B1307003F83411E404A796F4FCB386F89C27DB049D01`，
与第 114 批正式状态夹具字节一致。

最终 UE BuildId 为 `a4192a27-77ab-4bb5-9996-c71b2d55f53d`，构建指纹
`28DA3AAF4178852D44C6BF2F6EC0D9D739D7080CF0DA62B7C2E7C4B3A5EE30CD`。
插件包位于 `artifacts/unreal/AlsOverlayPosePluginValidation-20260912-115`，
DLL 1,349,120 字节。普通 Editor 的两条既有 Condition failed 日志保留。

## 未通过项：惯性化后的旋转

完整输出检查没有通过。最终 886 姿势的全差异检查记录 2,014 个超限骨骼
条目，四元数距离最大 0.0017207011，位置最大 9.597685e-7 m，曲线最大
2.3841858e-7，实际退出码 1。最终失败日志为
`overlay-pose-final-output-differences-final.log`；此前 884 姿势的
`overlay-pose-final-output-differences.log` 亦保留，两次最大误差和超限数量相同。
正常消费模式对第一个超限立即报错，诊断模式汇总差异后同样非零退出；
没有修改容差使完整输出通过。

已定位的精度边界是 UE 在 `AnimNode_Inertialization.cpp` 中以 FQuat
双精度记录历史、计算相对旋转，再对 W 做 acos；当前 Core 姿势和历史
为 Quaternion 单精度。接近单位旋转时，上游极小的归一化差异可能被
放大。原生取样证实惯性化前姿势通过，最终旋转仍有偏差；这说明不能
只依靠早期四骨合成惯性化测试宣布完整角色惯性化已达到当前误差门槛。

本轮尝试了在差量边界恢复单位长度并以 double 计算旋转，原有 11 项
惯性化测试通过，但完整角色仍有 1,693 个超限条目。该尝试未保留，共享
`AlsInertialization` 算法恢复为本轮开始时的内容，原有用户修改保留。
后续需要核对从原始采样/混合输出到历史存储的完整精度边界，不能仅在
最后一步改类型就声称与 UE 等价。

## 继续推进的顺序

1. 修复并验证惯性化旋转的完整精度链；现有前后双取样夹具保留为失败
   回归。该未通过项不从最终验收中删除。
2. 将 148 个来源的独立初始化/更新、播放历史、SecondaryMotion/
   IdleAdditive/Locomotion 同步组及生成通知接入真正的外围帧事务。
   本批原生供时的验证回调不能替代生产时钟或 P5A 通知消费者。
3. 接入正式 Aim/Overlay/BasePoses/LayerBlending 最终层，再接脊柱/手部、
   最终曲线反馈及完整 Foot IK/Lock/pelvis/平台。按原计划对当前上身、
   换髋、交错步和起步滑步做同输入/同脚相位多帧与人工验收。
4. 保留上批全 Core 的 23 个 P4/P5A 基准失败；本批未重跑整个 Core 集合，
   不将 519 项命名空间回归写成全仓通过。前批性能 p95 2.559 ms 未过
   2.5 ms 的问题亦保留。继续 P5A 通用动作、P5B Overlay/道具玩法、
   P5C Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera、P7 十分钟预算。

音频仍暂缓。本批未 commit/revert/merge，未删除资产或用户源码。
