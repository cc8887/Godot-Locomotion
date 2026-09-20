# Foot IK 最终控制链与原生伸展对照

第一百三十二批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 完整性归属与本批结果

本批继续原 P4 的 Foot IK/Foot Lock/pelvis，属于当前移动外观修复的前置，
不推迟到 P5C/P6。原 P5A 剩余通用动作、P5B 全 Overlay/道具、P5C Mantle/
Roll/Root Motion、P6 Ragdoll/恢复/完整 Camera、P7 最终验收仍在范围内。

已完成原 Foot IK 九个骨骼控制节点的组件运行时与正式图编译校验，并将
定义装入 `AlsMovementGraphDefinition.FootIk`。尚未接入 Worker 每帧执行，
没有因此改变默认 Demo 的视觉；不能将本批组件通过记为滑步/换髋已修复。

## 源图和执行语义

直接规格为 `assets/config/v4_foot_ik_inputs.json` 中 ALS V4 `Foot IK` 图，
及本机 UE 5.9 `AnimationCore/Private/TwoBoneIK.cpp`、
`AnimNode_ModifyBone.cpp`、`AnimNode_TwoBoneIK.cpp`、
`AnimNode_SkeletalControlBase.cpp/.h` 与 `AnimationRuntime.cpp`。

顺序为：左/右脚锁 Replace → 左/右虚拟脚 World Additive → pelvis World
Additive → 左/右虚拟膝 BoneSpace Additive → 左/右 TwoBoneIK。沿用同一
懒计算组件姿势，保留控制节点之间缓存骨骼恢复局部空间及部分 alpha 的
局部链混合，不将部分 IK 改写为插值目标位置。

- 腿 IK 允许伸展，StartStretchRatio=1、MaxStretchScale=1.5；按求解前
  实际段长同比伸展，保留骨骼 scale，不把伸展变成设置 scale。
- 膝目标偏移为 UE 厘米 `(20,30,0)` / `(-20,-30,0)`，使用目标骨空间。
- IK 的 effector 与 joint target 均为原虚拟骨，BoneSpace 零偏移；
  foot rotation 取 effector，允许 twist。
- 地形旋转与位移分别往返世界空间，骨盆使用全向量世界偏移。
- 脚锁 alpha 与 pelvis alpha 来自属性；曲线 alpha 独立使用 Update 阶段
  Enable_FootIK 值，钳制到 [0,1]。Evaluate 输入曲线原样保留，包括缺失标记。
- Prepare/Evaluate/Commit/Cancel 支持候选取消、非法输入失败与同帧重试，
  无场景/物理副作用，不提前推进已提交身份。

`AlsFootIkCompiler` 验证完整九节点连接、属性输入、曲线名字及完整 Node
设置。T3D 未列字段依据上述引擎构造函数：ModifyBone 默认 ComponentSpace/
忽略 scale；TwoBoneIK 默认 start=1、零偏移、允许 twist、关闭相对末端
旋转保持；SkeletalControl 默认 float alpha 与无 LOD 限制。本批不是新的
完整 compiled-node 属性导出；严格拒绝 Node 设置变化，不静默近似接受。

## 验证结果

1. 原生 UE 构建与插件审计：完整 `AdvancedLocomotionSystemVEditor Win64
   Development` 构建/审计退出 0，目标 up-to-date；AlsGodotExporter、
   AutoTestTools、BlueprintLisp 通过。构建日志前缀
   `20260912T213709746Z-95ec96d20c4a4c90afab76c7b751e79e`。
   本批使用 UE 插件构建技能确保对照进程的目标/插件身份一致。
2. `tools/unreal/export_foot_controller_math.py` 使用反射调用原生
   `K2_TwoBoneIK`（实际转调 AnimationCore）和 `Conv_RotatorToQuaternion`。
   导出 96 组伸展/不伸展、阈值附近、超长、反向、退化平面与零目标位置，
   另有 7 组角度/绕圈旋转。脚本不保存资产。
3. 首次启动失败是 Python 名称不等于 C++ 类名：实际 ScriptName 为
   AnimGraphLibrary。已改用 `/Script/AnimGraphRuntime.KismetAnimationLibrary`
   原生类路径。保留首个 `artifacts/foot-controller-native-math.log`；
   最终 `foot-controller-native-math-v2.log` 退出 0，含
   `ALS_FOOT_CONTROLLER_MATH_OK rows=96 rotations=7 assets_saved=0`。
4. `Fixtures/FootIk/native_controller_math.json` 为真实原生输出。
   对照验证关节/末端位置误差 ≤ 1e-8 cm；旋转在实际骨骼 float 输出边界
   比较。此为原生求解位置与 rotator 数学对照，不是完整 UE Foot IK 图的
   最终姿势回放，也不证明 IK 全部旋转逐比特一致。
5. `AlsFootIkControllerTests` 18 项通过；与 FootIkInput、PelvisIkInput、
   HandIk 回归合计 **67 通过，0 失败**。
   覆盖 8 类源图变异拒绝、脚锁→虚拟目标→腿顺序、旋转/缩放组件下的
   世界位移、骨盆后伸展、独立曲线时序、局部链部分混合、膝空间、候选
   取消/失败/重试，以及热身后九节点 2,000 帧零分配。
   结果：`artifacts/test-results/foot-controller-native.trx`。
6. `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore`：
   0 warning / 0 error。
7. 真实生产 `p3b_frame_order_smoke`，parallel、cycle、full movement、
   layered-frame，600 帧退出 0；223 players/257 samples，28 events，
   lag/stale=0。result `946C9F387EA43F26`，full pose
   `6B39A65B68F3FAE3`，root `A4F6C26CBAB8A0E7`。
   日志 `artifacts/foot-controller-worker-parallel.log`。
   仍报告 `owner=layered_through_hands`：新脚链未每帧接入，digest 保持是
   定义编译与既有路径回归的证据，不能当作新脚链生产验收。

## 下一步与未关闭项

接真实脚/根 socket 变换、组件/角色历史、地面 trace/walkability、世界与
动画 delta 及正确历史曲线；让 FootIkInput 与 FootIkRuntime 由统一候选帧
所有者更新，提交/取消一起处理，正式替换旧 feet/pelvis，避免双重应用。
随后完成根生命周期与默认入口切换、UE 同输入多帧和人工支撑脚/平台验收。

既有全套 Core 23 项失败、Import 零分配不稳定与此前 p95 2.559 ms >
2.5 ms 本批未解决。本批只报告上述专项通过，不声称全套测试、最终视觉
或十分钟预算通过。音频继续暂缓；未提交、回滚或合并现有工作区修改。
