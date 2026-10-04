# Lyra SkeletalControls 双腿 LegIK

2026-10-01，在当前主目录继续推进 `FullBody_SkeletalControls`。本批完成原 node107 的 ALS81 双腿求解组件、独立出现位置的弯曲历史及取消/提交宿主，经过连续原生对照。当前共同 ItemAnimLayers 执行组仍为13/14入口；完整第14入口和普通 Demo 尚未接入，不能将本组件当作完整最终足部链。

## 原图与资源边界

原12节点闭包按输入到输出依次为：LinkedInput112 → LocalToCS111 → HandRetarget103 → CopyBone102 → RootModify104 → RightTwoBone110 → LeftTwoBone109 → FootPlacement105 → LegIK107 → WeaponModify106 → CSToLocal108 → Root113。实际手部节点已在此前组件对照中验证；本批只关闭 LegIK，不跳过 FootPlacement 来宣称整个入口完成。

沿用 ALS skin68/raw69/logical81 模型、骨架和六条已重定向动画。双腿使用原 ALS 的 thigh/calf/foot 和 ik_foot_l/r；没有新增皮肤骨或更换人物模型。原 LegIK 定义顺序为左腿、右腿，均为两节 limb，Y foot-forward、Z hinge，膝盖扭转修正开启，旋转限制关闭，TwistOffset 曲线 None，ReachPrecision 为 float0.01，SoftPercentLength/SoftAlpha 均1。

独立 UE进程实际读取 CVar：Enable=1、EnableTwoBone=1、ForceAlwaysSolve=0。因此本配置走原两骨解析求解，不是通用多骨 FABRIK 实现，也没有声称支持其他 SoftIK/旋转限制配置。LegIK 的原 Alpha 是 DisableLegIK 曲线经 scale=-1/bias=1；本组件接收上层已解析的 float alpha，完整图前曲线更新尚待接入。

Main 当前 CDO UseFootPlacement=false、EnableControlRig=false。FootPlacement 自身仍有 bool混合、地形探测、骨盆和锁足历史，完整入口实现不能据此删除该节点。根骨 bool混合、武器 ScaleDownWeaponR 缩放、完整单一组件姿态链与最终 Main 位置也继续保留在后续目标中。

## 求解与事务

`AlsLegIkController` 先读取同一组件姿态上的两腿，再一次排序应用全部变更，保持原 OrientLeg → Reach → KneeTwist → FootRotation 顺序。不可达目标沿直线伸展；可达目标使用原解析公式；直腿难以提取弯曲方向时使用缓存 RealBendDir/BaseBendDir。原骨骼重新缓存按 FK foot 身份保留历史，不能在每次 Initialize/CacheBones 时清零。

`LyraLegIkNodeHost` 保存每个出现位置自己的不可变已提交历史和候选历史。Evaluate 消费只读姿态包，完整传递曲线 presence/value/flags、整数动画属性和 RootMotion；重复 Evaluate 从已提交历史重新计算。Commit 才发布弯曲方向，Cancel/异常不发布；旧候选、跨宿主候选、取消/提交后的姿态视图拒绝读写。隐藏和仅更新不产生求解历史，没有独立动画时钟、Sync 或 Skeleton writer。

本批修正公共 `AlsComponentPose`：实际 FCSPose 使用 FTransform::BlendWith，其包含 float端点与 Blend 的 Abs(alpha-1) 分支不同。按本机 Win64 SSE2 运算次序实现组件四元数 Dot4/Normalize；在180度退化姿态上，浮点加法顺序会影响最短旋转路径选择，不能只以一般姿态下的近似误差替代。

## 原生验证

新增独立 `UAlsLyraLegIKLibrary` 探针，复制三个真实 Provider 的原 LegIK CDO，在临时 ALS81骨架和六条原扩展动画上执行原节点。每帧记录受控输入姿态、实际输出、求解变更骨数和求解前后弯曲历史；重新缓存调用原 InitializeBoneReferences。临时目标包含自然位置、偏移、不可达、近直腿、与髋部重合、反向，另有目标旋转、人工直腿和八档 alpha，覆盖原两个权重端点。

Godot 验证的边界是 **LegIK operator**：其输入是原探针记录的受控骨骼输入包，由 Godot 自行求解输出和连续历史。它不是自主动画源/完整 SkeletalControls/Main 联合 oracle；未把此输入包当作已经从玩法生成的最终姿态。

三 Provider × 30/60/120 Hz × 6秒：3780帧/306180骨、2769有变更帧、1002禁用、1878部分混合、603直腿输入、30重新缓存、1491弯曲历史更新；15120整数属性、3780曲线及3780 RootMotion完整透传。每帧取消重试3780次，27048坏操作拒绝；另294隐藏与294仅更新帧保留历史。

保持原门槛位置1e-8 cm、四元数1e-10、缩放1e-12，历史1e-10。最终最大位置差7.944109290391274e-15 cm，四元数/缩放/弯曲历史差0。没有放宽门槛或排除退化帧。

两个独立 UE5.8.1采集均权威退出0，不可变输出一致，各0 error/794 warnings；原资产 GameplayTag 和临时依赖警告保留。508原包、667此前JSON及旧探针源哈希保持。新增 ignored `leg_ik_v1_requests/policy/native.json` 分别1144134/5318/116620535字节。代码检出仍需本地 ignored 资源才可运行。

公共组件运算修正后的回归：Core31、Import88均0失败0跳过；双手原生48例；Main LocomotionSM连续11340帧/9762姿态/12595根姿态；Aiming7560帧/6075姿态，以及13入口共同 Main Aiming 11340帧保持。Debug/Optimize ExportRelease均0警告0错误。未做新的完整 Main原生采集、普通Demo、渲染、人工地形、多角色或性能验收。

## 失败记录与复跑

首UE构建因 InitializeBoneReferences 为 private 失败，改为通过基类虚函数访问；首采集对空骨变更列表调用 LocalBlend 触发断言，按原调用者的非空门控修复。随后一次构建遇其他 UBT 的互斥占用，未终止外部进程，稍后重试成功。首C#构建存在局部名/属性Span错误，已修复。Godot首轮最小alpha、第二轮髋部重合姿态失败，分别定位 BlendWith端点和 SSE2运算次序；相关失败证据全部保留。

证据在 `artifacts/lyra-analysis/`：`leg-ik-ue-build*.log`、`leg-ik-ue-export.log` 为初次编译/空结果断言，`leg-ik-ue-build-empty-guard-retry.log` 为最终构建；`leg-ik-ue-export-fixed/repeat.log` 为两个成功采集。`leg-ik-godot-first/blendwith/dot.log` 保留严格失败，`leg-ik-godot-final.log` 为最终通过。`leg-ik-debug-final/optimize-final.log`、`leg-ik-core/import-regression.trx` 及 `leg-ik-*-regression.log` 保存构建和回归。

复跑 `scripts/export-lyra-leg-ik.ps1`，运行 `scenes/tests/lyra_leg_ik_smoke.tscn`，再执行 `python tools/verify_lyra_leg_ik.py`。下一步完成原 FootPlacement、Root/Weapon 控制和同一组件姿态上下文，将完整入口接入同一 Item组，再推进最终 Main 和生产验收。
