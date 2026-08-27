# P3 Demo 方向、输入与 Mannequin 展示层对齐设计

**状态：** 用户已确认，进入实施计划

**日期：** 2026-08-27

**Godot 工程：** `D:\GodotALS`

**行为参考：**

- 原版 ALS V4 工程：`D:\AdvancedLocomotionSystemV`
- `ALS-Refactored` 固定 commit：
  `b754d6f0f2bb03741d301f8fb88077ebfe561e17`
- Godot 基线：`main` commit
  `6ca4128bc61c5e1ff7c2bd827872202d6cb7c3cf`

## 一、范围与结论

本设计只修复当前 P3 demo 的基础可玩性问题：镜头看起来位于模型侧面、WASD 与屏幕方向观感不符、
角色视觉朝向和动画方向与逻辑位移错开、脚部高度错误，以及预建 spare 角色可能与 active 角色同时渲染。

研究结论是：

1. 当前 Godot 的 WASD 输入符号、camera yaw 捕获、相机相对世界方向解析和 locomotion blend 坐标合同
   基本正确；
2. 主要缺陷是 Godot 未应用 UE CharacterMesh 的固定 presentation 变换
   `(0, 0, -92 cm) / yaw -90 deg`；
3. inactive spare 未隐藏，可能造成重叠角色、出生点残影或替换时一帧错误姿态；
4. 当前 orbit camera 的固定 offset 不是 ALS Camera 合同，只能作为 P3 临时构图；
5. 完整 ALS Camera 必须单独实现 Socket 枢轴、曲线驱动 offset/lag、肩位、碰撞、第一人称混合与
   动画完成后的同步，继续保留在 P6。

因此本修复不翻转 WASD，不给 view/camera yaw 加 90 度补偿，也不修改 locomotion 核心方向公式。
固定资产校正只进入 visual presentation 边界。

## 二、三方实现对照

| 合同 | 原版 ALS V4 | ALS-Refactored | 当前 Godot P3 | 本阶段结论 |
|---|---|---|---|---|
| 移动轴 | W/S=`Forward +/-1`，D/A=`Right +/-1` | `Value.Y=forward`，`Value.X=right` | W 经 Y 反转后为 `MovementAxes +Y`，D 为 `+X` | 保持现状 |
| 鼠标 | MouseX 加 yaw，MouseY 经映射反号后加 pitch | X 加 yaw，Y 加 pitch；具体符号由 Input Mapping 决定 | X 减 yaw、Y 减 pitch，符合 Godot 屏幕拖动语义 | 不加补偿角 |
| 移动参考 | Control/Camera 水平 forward/right | 优先取 `Controller->GetPlayerViewPoint()` 的最终显示相机旋转，包含 rotation lag | 捕获无 lag 的 orbit camera yaw，resolver 转成世界方向 | 用真实 `Camera3D` basis 加测 |
| Mesh presentation | CharacterMesh 相对位置 `Z=-92 cm`、yaw `-90 deg` | 构造函数同样固定该变换 | visual root 直接等于 logical transform | 修为 `logical * presentation` |
| 第三人称 pivot | Character BPI 返回动态 pivot；AnimMan 使用 head 相关目标 | root 与 head Socket 位置的中点 | motor + 固定 `(0,1.45,0)` | P3 仅做构图补偿，完整 pivot 留 P6 |
| 相机 offset | CameraBehavior 动画曲线按状态输出 pivot/camera offset | AnimInstance 曲线；pivot offset 在 mesh 空间，camera offset 在 camera 空间 | SpringArm 长度和 FOV 固定 | 不在本修复伪造 ALS 曲线 |
| lag | rotation 与 XYZ pivot lag 均由曲线驱动 | rotation damper + camera-yaw 空间三轴独立 damper | 无 ALS 等价实现 | 留 P6 |
| 碰撞 | 肩部 trace 起点到目标做 SphereTrace | 肩 Socket、球形 sweep、穿透调整、距离平滑 | SpringArm 基础碰撞 | 留 P6 |
| 第一/第三人称 | CameraBehavior 权重混合位置与 FOV | Socket 位置、曲线权重、FOV 混合 | 只有第三人称 orbit | 留 P6 |
| 更新时序 | CameraManager 的 `BlueprintUpdateCamera/CustomCameraBehavior` 读取 CameraBehavior 曲线；只读图不足以证明更精确顺序 | `PostPhysics`，等待并行动画评估完成 | camera 跟主线程 motor，不读 worker visual | P3 保持线程所有权；P6 单独设计同步 |

### 2.1 证据边界

- 原版输入映射来自
  `D:\AdvancedLocomotionSystemV\Config\DefaultInput.ini`；
- 原版 CameraManager、CameraBehavior、PlayerController 和 AnimMan CDO/Blueprint 图来自只读 UE 审计，
  核心源资产没有保存修改；
- 原版资产可确定相机是状态/曲线驱动，但只读导出不能严谨证明某一组提取曲线值就是 demo 初始状态的
  最终混合结果，因此不得把 `CameraOffset=(-320,0,50) cm` 等值硬编码为 P3 初始相机；
- C++ 参考直接证明输入组合、Mesh presentation、Socket pivot、camera-space lag、trace 和更新时序；
- C++ 参考中的隐藏 Camera skeletal component 挂在 Character mesh 下并带 local yaw `+90 deg`，默认与
  mesh 的 `-90 deg` 相消；这再次证明 mesh yaw 不是 movement/view basis；
- 当前问题截图位于
  `D:\GodotALS\artifacts\diagnostics\p3-direction-before.png`。

### 2.2 输入数学对照

原版与 C++ 参考都把二维输入定义为 `x=right`、`y=forward`。C++ 参考先把输入 clamp 到单位圆，
再使用最终 PlayerViewPoint 水平 yaw `psi`：

```text
forwardUE = ( cos(psi), sin(psi), 0)
rightUE   = (-sin(psi), cos(psi), 0)
movement  = forwardUE * input.y + rightUE * input.x
```

Godot 的等价定义是：

```text
forwardG = flatten(-Camera3D.GlobalBasis.Z)
rightG   = flatten( Camera3D.GlobalBasis.X)
movement = forwardG * input.y + rightG * input.x
```

`Input.GetVector(left,right,forward,back)` 的屏幕 Y 经 adapter 转换后，W/S 分别成为 `+Y/-Y`，
A/D 分别为 `-X/+X`。UE 到 Godot 的 yaw 手性相反，因此鼠标右移在 UE 增加 yaw、在 Godot 减少 yaw；
这两种写法表示同一屏幕旋转，不是输入反号缺陷。当前 P3 相机没有 rotation lag，所以捕获的 orbit yaw
应与实际 `Camera3D` 水平 basis 一致。

## 三、三个独立空间

### 3.1 逻辑角色空间

Godot gameplay 坐标固定为：

```text
Forward = -Z
Right   = +X
Up      = +Y
```

UE 到 Godot 的通用坐标转换保持：

```text
(xG, yG, zG) = (yUE, zUE, -xUE)
yawG = -yawUE
unitScale = 0.01
```

相机输入、移动命令、motor、逻辑角色 yaw、局部速度和 BlendSpace 只使用逻辑角色空间，不读取或反推
visual root 的朝向。

### 3.2 Mannequin presentation 空间

固定 Mesh component 变换转换到 Godot 后为：

```text
translationMeters = (0, -0.92, 0)
yawRadians = -PI / 2
```

实际窗口与正式导入场景证明 Mannequin visual root 的 raw `-X` 是视觉前向；Godot 负 90 度绕 Y
旋转把它映射到 gameplay `-Z` 前向。该轴由真实 `Skeleton3D` 的左右 `Foot_* -> ball_*` 水平
rest-pose 方向在 visual-root 局部空间内推导，不再由硬编码 `+X` 自证。
每帧 visual root 世界变换的组合顺序固定为：

```text
visualWorld = logicalCharacterWorld * mannequinPresentationLocal
```

presentation 作用于完整导入场景根，使 mesh、Skeleton3D 和动画轨道保留同一个局部空间。它不得写回：

- motor transform；
- `AlsFrameInput.CharacterTransform`；
- `CharacterYaw`、view yaw 或 aim yaw；
- `BlendCoordinates` 或实际速度。

### 3.3 Camera view 与 pivot 空间

camera view yaw 是输入和 Looking/Aiming 方向的逻辑来源；camera pivot 是画面构图目标。两者不能由
Mannequin presentation yaw 推导，也不能用 visual root 替代。

完整 ALS Camera 的第三人称 pivot 是动态骨骼/Socket 结果：原版 AnimMan 通过 BPI 提供目标，固定 C++
参考取 root 与 head Socket 中点。它不是固定 `0.53 m` 或 `1.45 m` 高度。

## 四、Profile schema v2

`p3_locomotion_profile.json` 从 schema v1 升级为 schema v2。顶层属性集合仍严格验证，并精确新增
`presentation`；该对象只允许以下两个字段：

```json
{
  "presentation": {
    "translationMeters": [0.0, -0.92, 0.0],
    "yawRadians": -1.5707963267948966
  }
}
```

合同要求：

- `translationMeters` 必须恰好三个有限数值；
- `yawRadians` 必须是有限数值；
- 缺失、额外、错误大小写、重复或类型错误字段全部失败；
- compiler 输出纯 C# 只读值类型，例如
  `readonly record struct AlsPresentationDefinition(Vector3 TranslationMeters, float YawRadians)`；
- yaw 测试使用浮点容差验证其编译结果等于 `-MathF.PI / 2`，不要求 JSON double 与 runtime float
  位级相等；
- generator、tracked profile、compiler、profile 持有的只读 presentation 值和 exact-property tests
  同步升级到 v2；
- presentation 数值是由原版 AnimMan CDO 与固定 C++ commit 双重锁定的行为常量；generator 明确写出
  该常量，不能声称它由不包含 component transform 的 manifest 推导；
- 不按资产名、文件名或 stable ID 在运行时猜测 presentation；
- 不提供 identity 静默回退。

## 五、运行时与生命周期

初始化数据流为：

```text
tracked profile schema v2
  -> strict profile compiler
  -> readonly presentation value
  -> main-thread precomputed Godot Transform3D
  -> active/spare visual workers
```

Worker 继续消费同一份 `AlsFrameInput`，先计算逻辑 locomotion，再以预计算矩阵得到
`logical * presentation`，验证结果有限后写入其拥有的 visual subtree，最后推进 AnimationTree 并发布
pose/full-pose/root digest。steady state 不创建矩阵、字符串或集合，不增加跨线程 Node 读取。

Configure 阶段在 process thread group 尚未运行时，由主线程用初始 logical transform 应用一次
`logical * presentation`，并建立 corrected root rollback 基线。这样首个 Worker frame 失败时不会恢复到
未校正 root；该角色继续隐藏并按既有 failure 合同处理。

### 5.1 Active、spare 与 visual-ready

当前 slot 同时预建 active 和 spare，但 `SetActive(false)` 只关闭 processing/collision，没有隐藏 `Node3D`。
本阶段新增明确合同：

- 角色在 `AddChild()` 前即设置 `Visible=false`，避免配置窗口闪现 raw rig；
- `Active/ProcessMode/Collision` 与 `Visible` 是彼此独立的状态；
- visual-ready 精确定义为：当前 generation 的 `logical * presentation`、AnimationTree advance、
  pose/full-pose/root digest 已由 Worker 成功发布，并由主线程 Commit 消费同一 identity 的有效结果；
- Worker 只发布带 `frame/character/generation` identity 的 ready candidate、数值 transform snapshot 和
  digests，不修改 main-thread character 的 `Visible`；
- 只有主线程 Commit/slot 可以在 identity 与 active generation 匹配后设置 `Visible=true`；
- generation/replacement 会重置 ready；inactive 角色无论 ready 与否都必须不可见；
- spare 在 replacement recovery 的首个有效结果提交前保持不可见；
- 任一时刻每个 slot 最多一个角色可见；
- retirement 先隐藏旧角色，再释放其 visual/runtime；
- generation 切换不得展示 identity presentation、旧 root transform 或陈旧 pose；
- active 与 spare 使用同一不可变 presentation 定义。

本修复只保证 presentation、线程所有权与可见性正确性。当前 replacement 流程先释放旧代，再让新代完成
generation mismatch/recovery，因此明确允许这段诊断流程中暂时 `visibleCount=0`；禁止的是 unready rig、
stale rig 或双 rig 可见。当前流程也不复制旧 motor 的世界位置、速度和全部运动状态，所以 camera target
和角色世界位置连续性不属于本设计的完成条件；如需无空窗、无位置跳变，应单独扩展 slot/motor state
transfer 与原子 visual handoff 合同。

### 5.2 诊断合同

现有 runtime 内部已经计算 corrected visual root digest，但公开 frame diagnostics 和 P3B single/parallel
摘要未包含它。本阶段明确：

- lifecycle diagnostics 新增 `IsVisible` 与 `IsVisualReady`；
- slot diagnostics 新增稳定 replacement phase 和 `VisibleCharacterCount`；
- frame diagnostics 暴露当前 identity 的 numerical visual snapshot 与 root digest；
- single/parallel 摘要统一使用无歧义 marker：`digest=` 表示 result aggregate，`pose=` 表示
  `PoseDigest` aggregate，`full_pose=` 表示 `FullPoseDigest` aggregate，`root=` 表示 corrected visual
  root aggregate；P3B harness、frame-order smoke、parser 和 pair tests 使用相同语义；
- parity 只能发现 single/parallel 差异，不能证明两边没有同时漏掉 presentation。因此独立的
  expected-transform smoke 仍是主正确性门禁，root parity 只是补充门禁。

## 六、P3 相机与输入行为

### 6.1 P3 临时 orbit 构图

当前 P3 orbit target 相对未校正 raw visual root 的既有构图高度为 `1.45 m`。应用 presentation 的
`Y=-0.92 m` 后，为保持同一相对画面，临时 follow offset 相对 motor 调整为：

```text
presentationY = -0.92
legacyPivotAboveRawVisualRoot = +1.45
followOffsetFromMotorY = -0.92 + 1.45 = +0.53
```

站立初始 motor 中心离地 `0.90 m`，所以临时 pivot 离地约 `1.43 m`。`0.53 m` 只是 P3 orbit
composition-preservation 值，不是 ALS head/root Socket pivot，也不得被后续完整 Camera 复用为 ALS 常量。

相机继续跟随主线程 motor `MovementAnchor`，不读取 Worker 拥有的 visual root。水平 yaw/pitch 输入符号
保持现状。

### 6.2 WASD

任意相机水平 yaw 下都必须满足：

```text
W = flattened Camera3D forward
A = flattened Camera3D left
S = flattened Camera3D backward
D = flattened Camera3D right
```

这里的 forward/right 必须从实际 `Camera3D.GlobalBasis` 读取并归一化验证，而不是由被测 resolver 使用的
同一 yaw 公式再次合成。

### 6.3 Rotation mode 子集

本阶段只锁定 P3 已实现并可验证的 ALS 子集：

- `LookingDirection`：角色逻辑前向主要跟随 view yaw；Sprinting 时目标转为 velocity yaw，A/D 在非冲刺
  locomotion 中可表现为侧向移动；
- `VelocityDirection`：有可靠速度时角色逻辑前向朝实际移动方向；
- `Aiming`：角色逻辑前向朝 aim yaw，移动仍为相机相对方向。

完整 ALS 还包含第一人称、空中瞄准、冲刺优先级等额外策略，本设计不声称 P3 已全部实现。
presentation 的负 90 度校正叠加在逻辑 yaw 之后，只对齐资产，不改变 rotation mode 语义。

## 七、动画方向

二维动画坐标保持：

```text
blend.x = dot(actualVelocity, characterRight)
blend.y = dot(actualVelocity, characterForward)
```

其中 `characterForward=-logicalBasis.Z`、`characterRight=logicalBasis.X`。Standing profile 没有纯 L/R
sample，纯侧移由 LF/LB 或 RF/RB 等边界 sample 混合；测试不得假设存在 standing L/R clip。
Crouching profile 有独立 L/R sample。

presentation 后，F/B/侧向混合、Jump、Fall 和 Land 都在同一个校正模型空间播放。动画参数仍从逻辑角色
空间速度生成，禁止用 corrected visual yaw 再计算局部速度，也不通过修改 play rate 掩盖错误 sample。

## 八、测试设计

### 8.1 Pure、generator 与 profile

- schema v2 精确属性集合通过；v1、缺失、额外、重复、非有限、错误长度和错误类型全部失败；
- compiler 在容差内得到 `(0,-0.92,0)` 与 `-MathF.PI/2`；
- generator 确定性输出 schema v2 presentation；
- 代表性 F/B/L/R 完整 object path 到 stable ID 和 sample 坐标逐项锁定；
- compiler 产物进入 runtime context 后不丢失 presentation。

### 8.2 真实 Camera3D/input smoke

覆盖 camera yaw `0`、`+PI/2`、`-PI/2` 下 W/A/S/D 共 12 个组合，并验证：

```text
resolved W == flattened camera forward
resolved D == flattened camera right
resolved S == -forward
resolved A == -right
```

每个组合必须走端到端 production 路径：用 `Input.ParseInputEvent`/`Input.ActionPress` 驱动 orbit 和
movement action，经 `CaptureGodotFrame(..., orbit.Yaw)` 再进入 resolver。测试独立读取真实
`Camera3D.GlobalBasis`，由 flattened forward 反算
`basisYaw=atan2(forward.X,-forward.Z)`，先断言 `orbit.Yaw` 与 `basisYaw` 的归一化角差在容差内，再把
resolved movement 与真实 basis 比较。这样不会用同一个硬编码 yaw 同时生成输入和期望值。

同时覆盖带 pitch 时的水平投影、零输入清理和 Aiming 下仍为 camera-relative movement。

### 8.3 真实 visual 与生命周期 smoke

使用正式 Mannequin/profile 验证：

- 角色尚未 active、Worker idle 时，从真实 `Skeleton3D` 左右脚掌到脚趾的 rest pose 推导 raw `-X`
  forward，并验证 presentation 将其映射为 world `-Z`；
- Worker 内部断言 visual root 等于 `logical * presentation`，并发布 numerical snapshot/root digest；
- motor `Y=0.90` 时 visual root `Y=-0.02`；
- logical yaw `0`、`+PI/2`、`-PI/2` 时相对校正不变；
- 正常主线程测试只读取发布快照/diagnostics，不直接读取 worker-owned visual Node；需要直接读 Node 的
  专项测试必须在 Worker thread group 内断言，或先 suspend 并等待 `WorkerInFlight=0` barrier；
- active/spare 初始化时最多一个角色可见，且可见角色必为 visual-ready；
- replacement recovery 允许短暂零角色可见，但不会显示未校正 spare、陈旧 pose 或两个 rig；
- worker failure rollback 恢复 corrected root；
- lifecycle/slot diagnostics 可逐 phase 证明 ready、visible 与 visible-count；
- frame diagnostics 与 single/parallel 摘要包含 corrected root digest；
- `digest/pose/full_pose/root` marker 含义固定，single/parallel 聚合与错误计数一致；
- expected-transform smoke 独立锁定正确值，不能只依赖 single/parallel parity。

### 8.4 动画方向矩阵

- LookingDirection 的 W/S 对应正/负 forward blend；
- LookingDirection 的 A/D 对应负/正 right blend；profile 单独锁定 LF/LB/RF/RB exact ID/坐标，固定相位
  的左右 pose digest 必须不同，但本阶段不新增 sample-weight instrumentation；
- Aiming 的 W/A/S/D 保持相机相对移动，角色朝 aim yaw；
- VelocityDirection 的四向移动最终收敛为角色局部 forward；
- 代表性 forward/back/left-side/right-side pose digest 不互相混淆；
- Jump/Fall/Land 保持既有状态转换和 pose advance。

### 8.5 实际窗口验收

自动门禁通过后启动正式 demo，保存 after 截图并与 before 对照：

1. 初始镜头位于角色背后，角色脚部接触地面且没有重叠 spare；
2. 不转镜头时 W 向屏幕深处、S 向镜头方向、A/D 与屏幕左右一致；
3. 相机旋转约 90 与 180 度后重复四键检查；
4. LookingDirection、VelocityDirection、Aiming 的朝向差异符合各自合同；
5. Walking、Running、Sprinting、Crouching、Jump、Fall、Land 的动作方向与位移一致；
6. replacement 过程不显示 unready/stale/双 rig；允许诊断 recovery 期间的已声明短暂空窗；
7. HUD 无 runtime、thread、missing、stale 或 generation 错误。

### 8.6 回归与性能

- 全部 .NET tests；
- P3B single/parallel 全矩阵与摘要比较；
- P3A、P2B、P1、P0 既有门禁；
- Worker steady state 继续零托管分配；
- presentation/visibility/root digest 不增加每帧字符串、集合或跨线程 Node 访问。

## 九、错误处理

- presentation 编译失败时拒绝启动，不回退 identity；
- 组合后的 `Transform3D` 任一分量非有限时进入现有 worker failure/rollback 合同；
- headless/debug 中 presentation、identity/ready 发布或可见性违规立即使场景失败；
- inactive 角色可见、同 slot 多角色可见或 active 在 visual-ready 前可见均作为生命周期错误；
- smoke 失败信息区分 camera basis、resolved movement、logical transform、presentation、visual root 和
  visibility 状态。

## 十、完整 ALS Camera 的后续边界

完整相机仍属于主规划 P6，后续按独立设计与实施计划实现：

1. root/head Socket 动态第三人称 pivot 与 first-person Socket；
2. 左/右肩切换与 shoulder trace Socket；
3. CameraBehavior/AnimInstance 状态和曲线数据合同；
4. mesh 空间 pivot offset、camera rotation 空间 camera offset 与 FOV offset；
5. rotation lag、camera-yaw 空间 XYZ 独立 pivot lag；
6. movement-base 相对状态、teleport lag reset 与 time-dilation 处理；
7. sphere sweep、起点穿透调整与 trace-distance smoothing；
8. first/third-person location/FOV 混合、post-process 与 camera shake；
9. PostPhysics 和并行动画评估完成后的确定性同步；
10. 完整相机单人/多人行为、性能和十分钟稳定性门禁。

P3 不用固定 SpringArm 参数冒充上述系统，也不把本阶段 `0.53 m` 写入完整 Camera 合同。

## 十一、明确不修改

- 不修改 UE 源资产、FBX、GLB 或 Godot import cache；
- 不翻转 `Input.GetVector()` 的现有 Y 转换；
- 不给 orbit yaw、view yaw、aim yaw 或 motor yaw 加 90 度补偿；
- 不把 visual root 作为 camera 或 movement 的逻辑方向来源；
- 不修改 resolver、motor 或 `Als.Core` yaw/blend 数学；
- 不在本修复中实现 AimOffset、Foot IK、Turn/Rotate in Place 或完整 ALS Camera；
- 不借机重构无关的 P0/P1/P2/P3 生命周期和性能测量代码；
- 不宣称 replacement 已具有完整 motor world-state continuity。

## 十二、完成定义

本修复只有同时满足以下条件才完成：

1. schema v2 presentation 数据通过 generator、compiler、runtime context 和严格负例测试；
2. visual root 初始化及每帧都使用 `logical * presentation`，Core/motor 合同不变；
3. inactive spare 始终隐藏，active 只在 visual-ready 后显示；
4. P3 临时相机继续跟 motor，构图 offset 正确且未冒充完整 ALS pivot；
5. 三个 yaw 下 W/A/S/D 与真实 `Camera3D.GlobalBasis` 一致；
6. Mannequin 初始面向 gameplay `-Z`，脚部落地且没有重叠 rig；
7. LookingDirection、VelocityDirection、Aiming 的移动/朝向语义不混淆；
8. 真实方向动画、空中动作与 replacement smoke 通过；
9. corrected root digest 进入公开诊断和 single/parallel 摘要且保持一致；
10. P3B、P3A、P2B、P1、P0 与全部 .NET tests 通过；
11. 关键 Worker 路径保持零托管分配；
12. 实际窗口截图与键鼠验收确认原始问题消失。

## 十三、参考

- `D:\GodotALS\docs\superpowers\specs\2026-08-25-godot-als-port-design.md`
- `D:\GodotALS\docs\superpowers\specs\2026-08-26-p3-basic-locomotion-design.md`
- `D:\GodotALS\docs\architecture\p3-basic-locomotion.md`
- `D:\GodotALS-References\ALS-Refactored\Source\ALS\Private\AlsCharacter.cpp`
- `D:\GodotALS-References\ALS-Refactored\Source\ALSExtras\Private\AlsCharacterExample.cpp`
- `D:\GodotALS-References\ALS-Refactored\Source\ALSCamera\Private\AlsCameraComponent.cpp`
- `D:\GodotALS-References\ALS-Refactored\Source\ALSCamera\Public\AlsCameraSettings.h`
- `D:\AdvancedLocomotionSystemV\Config\DefaultInput.ini`
- `D:\AdvancedLocomotionSystemV\Content\AdvancedLocomotionV4\Blueprints\CameraSystem\ALS_PlayerCameraManager.uasset`
- `D:\AdvancedLocomotionSystemV\Content\AdvancedLocomotionV4\Blueprints\CameraSystem\ALS_PlayerCameraBehavior.uasset`
- `D:\AdvancedLocomotionSystemV\Content\AdvancedLocomotionV4\Blueprints\CharacterLogic\ALS_Player_Controller.uasset`
- `D:\AdvancedLocomotionSystemV\Content\AdvancedLocomotionV4\Blueprints\CharacterLogic\ALS_AnimMan_CharacterBP.uasset`
- `D:\GodotALS\src\Als.Godot\Locomotion\AlsP3WorkerRoot.cs`
- `D:\GodotALS\src\Als.Godot\Locomotion\AlsOrbitCamera.cs`
- `D:\GodotALS\src\Als.Core\Locomotion\AlsLocomotionCommandResolver.cs`
