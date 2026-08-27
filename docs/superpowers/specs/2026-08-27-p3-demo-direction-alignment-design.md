# P3 Demo 方向与 Mannequin 展示层对齐设计

**状态：** 已确认，待实施

**日期：** 2026-08-27

**Godot 工程：** `D:\GodotALS`

**行为参考：** `ALS-Refactored` commit
`b754d6f0f2bb03741d301f8fb88077ebfe561e17`

## 一、问题与证据

当前 P3 demo 启动后从角色侧面观察 Mannequin，角色视觉模型与逻辑前向相差 90 度，模型还缺少
相对胶囊体的垂直偏移。结果是相机虽然位于逻辑角色后方，画面看起来却位于模型侧方；WASD
虽然产生正确的相机相对世界速度，模型朝向和动作却与位移错开，因而三个问题同时表现为错误。

已确认的证据如下：

- Godot 的相机水平前向为 `-Camera3D.GlobalBasis.Z`，右向为
  `Camera3D.GlobalBasis.X`；
- `AlsPlayerInputAdapter` 和 `AlsLocomotionCommandResolver` 在任意 yaw 下产生的 W/D 方向分别与
  上述相机前向/右向一致；
- `AlsLocomotionModel` 的二维动画坐标合同为 `X=local right`、`Y=local forward`，profile 中的
  F/B/L/R stable ID 与坐标符号一致；
- 固定 C++ 参考在 `AAlsCharacter` 构造时对 `GetMesh()` 设置相对位置
  `(0, 0, -92 cm)` 和相对 yaw `-90 deg`；
- 当前 `AlsP3WorkerRoot` 直接把 motor 的世界变换写给导入的 Mannequin 根节点，没有应用该组件
  相对变换；
- 当前 main 的实际窗口截图确认初始镜头看见角色侧面，且角色阴影与脚部存在明显分离。

因此本修复不翻转 WASD，不给 view yaw 增加补偿角，也不修改 locomotion 核心方向公式。唯一的
固定资产校正位于 visual presentation 边界。

## 二、坐标与展示合同

### 2.1 逻辑坐标

Godot gameplay 坐标继续固定为：

```text
Forward = -Z
Right   = +X
Up      = +Y
```

UE 到 Godot 的通用轴映射保持：

```text
(xG, yG, zG) = (yUE, zUE, -xUE)
yawG = -yawUE
unitScale = 0.01
```

相机、输入命令、motor、角色逻辑 yaw、局部速度和 BlendSpace 都只使用该逻辑坐标，不读取或反推
visual root 的朝向。

### 2.2 Mannequin presentation

固定 C++ Mesh component 变换转换到 Godot 后为：

```text
translationMeters = (0, -0.92, 0)
yawRadians = +PI / 2
```

Godot 正 90 度绕 Y 旋转把导入 Mannequin 的原始 `+X` 视觉前向映射到 gameplay `-Z` 前向。
每帧 visual root 的世界变换必须按以下顺序计算：

```text
visualWorld = logicalCharacterWorld * mannequinPresentationLocal
```

presentation 只作用于完整导入场景根，因此 mesh、Skeleton3D 和全部动画轨道保持同一个局部空间。
它不得写回 motor、`AlsFrameInput.CharacterTransform`、`CharacterYaw`、view yaw 或 BlendCoordinates。

### 2.3 Profile 数据

`p3_locomotion_profile.json` 增加严格的 `presentation` 对象，至少包含：

```json
{
  "translationMeters": [0.0, -0.92, 0.0],
  "yawRadians": 1.5707963267948966
}
```

`AlsLocomotionProfileCompiler` 将它编译为纯 C#、不可变、有限值的 presentation 定义。缺失字段、
额外字段、非有限数值或非三分量 translation 都必须失败。运行时不得按 asset name 猜测这组变换，
也不得在 Worker steady state 创建矩阵、字符串或集合。

## 三、运行时数据流

初始化期的数据流为：

```text
tracked profile JSON
  -> strict profile compiler
  -> immutable presentation definition
  -> P3 runtime context precomputed Godot Transform3D
  -> every prebuilt character visual worker
```

Worker 每帧继续消费同一份 `AlsFrameInput`，先计算逻辑 locomotion 结果，再把预计算的 presentation
局部变换与逻辑角色世界变换组合，最后推进 AnimationTree。该组合属于 Worker 已拥有的 visual subtree，
不引入跨线程 Node 读取。

角色替换时 active 和 spare 必须使用完全相同的 presentation 定义；generation 切换不能产生一帧
未校正姿态或根变换跳变。现有 pose capture/rollback 继续捕获校正后的 visual root 世界变换。

## 四、相机与输入行为

### 4.1 相机目标

当前 motor 节点位于站立胶囊中心，即地面以上 `standingHalfHeight=0.90 m`。presentation 把
Mannequin 根移到 motor 下方 `0.92 m`。P3 最小 orbit camera 的目标仍定义为 Mannequin 根以上
`1.45 m`，因此相对 motor 的 follow offset 为：

```text
0.90 - 0.92 + 1.45 = 1.43 m above floor
followOffsetFromMotor = (0, 0.53, 0)
```

相机只跟随主线程 motor 和不可变数值 offset，不读取 Worker 所拥有的 visual root。水平 yaw 和 pitch
输入符号保持现状。完整 ALS Camera 行为仍属于后续阶段，本修复只保证 P3 demo 的初始背后视角、
目标高度和屏幕相对移动正确。

### 4.2 WASD 与 rotation mode

任意相机水平 yaw 下都必须满足：

```text
W = camera forward
A = camera left
S = camera backward
D = camera right
```

各 rotation mode 的视觉行为按固定 ALS 语义区分：

- `LookingDirection`：角色逻辑前向主要保持 view yaw；A/D 可以是侧向 locomotion；
- `VelocityDirection`：有可靠速度时角色逻辑前向朝实际移动方向；
- `Aiming`：角色逻辑前向朝 aim yaw，移动仍是相机相对方向。

presentation 的 90 度校正叠加在上述逻辑 yaw 之后，只对齐资产，不改变 mode 语义。

## 五、动画方向

二维动画坐标继续固定为：

```text
blend.x = dot(actualVelocity, characterRight)
blend.y = dot(actualVelocity, characterForward)
```

其中 `characterForward=-logicalBasis.Z`，`characterRight=logicalBasis.X`。Standing profile 中没有纯
L/R sample，纯侧移可以由现有斜向 sample 边界插值，但其坐标符号和参与的真实 clip 必须正确。
本修复不增加、替换或重新导出动画，也不通过提高 play rate 掩盖错误 sample。

presentation 校正后，F/B/L/R、Jump、Fall 和 Land 都在同一个已校正模型空间播放。动画图参数仍由
逻辑角色空间速度产生，禁止使用校正后的 visual yaw 再计算一次局部速度。

## 六、测试设计

### 6.1 Pure 与 profile 测试

- profile 编译得到精确的 `(0, -0.92, 0)` 和 `PI/2`；
- presentation schema 拒绝缺失、额外、非有限和错误长度字段；
- profile generator 输出确定的 presentation 数据；
- F/B/L/R stable ID 与 sample 坐标符号继续锁定。

### 6.2 Godot 方向合同 smoke

扩展真实 Godot camera/input smoke，至少覆盖 yaw `0`、`+PI/2`、`-PI/2` 下的 W/A/S/D：

```text
resolved W == flattened camera forward
resolved D == flattened camera right
resolved S == -forward
resolved A == -right
```

测试必须读取真实 `Camera3D.GlobalBasis`，不能只比较两个由同一公式合成的 yaw 数值。

### 6.3 真实 visual smoke

使用正式 Mannequin 和正式 profile 验证：

- logical identity 时，presentation 把声明的 raw `+X` forward 映射到 world `-Z`；
- visual root world transform 等于 `logical * presentation`；
- motor 位于 `Y=0.90` 时，visual root 位于 `Y=-0.02`；
- logical yaw 为 `0`、`+PI/2`、`-PI/2` 时相对校正保持不变；
- active/spare replacement 前后 presentation 完全一致；
- single/parallel 的 pose、root digest 和错误计数继续一致。

### 6.4 动画方向矩阵

真实 AnimationTree 测试至少覆盖：

- LookingDirection 稳态 W/S 对应正/负 forward blend；
- LookingDirection 稳态 A/D 对应负/正 right blend；
- VelocityDirection 的四个移动方向最终都收敛到角色局部 forward；
- F/B/L/R 代表性 sample 的真实 pose digest 互不混淆；
- Jump/Fall/Land 在 presentation 校正后仍保持既有状态转换和 pose advance。

### 6.5 实际窗口验收

自动门禁通过后必须启动真实 demo 并保存截图，人工检查：

1. 初始镜头位于角色背后，角色脚部接触地面；
2. 不转镜头时 W 向屏幕深处移动，S 向镜头方向移动；
3. A/D 与屏幕左右一致；
4. 相机旋转约 90 和 180 度后重复检查四键；
5. Walking、Running、Sprinting、Crouching、Jump、Fall、Land 的动作方向与位移一致；
6. HUD 无 runtime、thread、missing、stale 或 generation 错误。

## 七、错误处理与诊断

- presentation 编译失败时拒绝启动，不使用 identity 作为静默回退；
- 组合后的 `Transform3D` 任一分量非有限时按现有 worker failure 合同冻结最后有效姿态；
- headless/debug 中 presentation 错误立即使场景失败；
- root digest 继续包含校正后的完整 visual transform，以便 single/parallel 比较发现偏移丢失；
- smoke 失败信息必须区分 camera basis、resolved movement、logical character 和 visual presentation。

## 八、明确不修改

- 不修改 UE 导出资产、FBX 或 Godot 导入缓存；
- 不翻转 `Input.GetVector()` 的现有 Y 转换；
- 不给 orbit yaw、view yaw、aim yaw 或 motor yaw 增加 90 度补偿；
- 不把 visual root 作为 camera 或 movement 的逻辑方向来源；
- 不在本修复中实现 AimOffset、Foot IK、Turn in Place 或完整 ALS Camera；
- 不借机重构无关的 P0/P1/P2/P3 生命周期和性能测量代码。

## 九、完成定义

本修复只有同时满足以下条件才完成：

1. presentation 数据进入严格 profile 合同并通过纯测试；
2. visual root 每帧使用 `logical * presentation`，motor 与 Core 合同保持不变；
3. 相机目标高度按校正后的模型根重新对齐；
4. 三个 yaw 下的 W/A/S/D 与真实 Camera3D basis 一致；
5. 真实 Mannequin 初始面向 gameplay `-Z`，脚部落地；
6. LookingDirection、VelocityDirection、Aiming 的移动/朝向语义未混淆；
7. 真实 F/B/L/R 和空中动作测试通过；
8. P3B 全矩阵、P3A、P2B、P1、P0 和全部 .NET tests 通过；
9. single/parallel 摘要一致，关键路径继续零托管分配；
10. 实际窗口截图和键鼠验收确认原始问题不再出现。

## 十、参考

- `docs/superpowers/specs/2026-08-26-p3-basic-locomotion-design.md`
- `docs/architecture/p3-basic-locomotion.md`
- `D:\GodotALS-References\ALS-Refactored\Source\ALS\Private\AlsCharacter.cpp`
- `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- `src/Als.Godot/Locomotion/AlsOrbitCamera.cs`
- `src/Als.Core/Locomotion/AlsLocomotionCommandResolver.cs`
