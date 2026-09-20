# Foot IK 属性函数、原生脚锁补偿与重置行为

日期：2026-09-13。第一百三十一批，承接正式 Foot IK 原图。

## 本批实现

新增 `AlsFootLockInputModel`、`AlsFootOffsetInputModel`、`AlsFootIkResetModel`
和总调度 `AlsFootIkInputModel`；复用上一批骨盆模型，将 VInterpTo 抽入
`AlsFootIkMath`。`AlsFootIkInputCompiler` 核对五个属性函数及 UpdateFootIK
的连线、分支、执行顺序、参数绑定、函数局部变量与 CDO 初值。
`AlsMovementGraphDefinition` 已编译并持有完整属性模型。

各模型从调用方的状态和采集观察值返回候选，不查询场景、不发布事件、
不推进动画来源时钟，也不在内部保存已提交状态。保留 UE 轴、厘米、
double 向量/rotator，并在原 float 参数边界做显式转换。

### SetFootLocking / SetFootLockOffsets

- Enable_FootIK 曲线不大于零时，函数不改写脚锁权重、位置或旋转。
- 脚锁曲线达到 0.99，或者小于当前锁权重时，才把曲线写入锁权重。
  未到全锁的回升不能重新增加权重；属性更新不额外钳制。
- 更新后的权重达到 0.99 时，每次都捕获 ik_foot 的组件空间位置/旋转，
  并非只在第一次到达全锁时捕获。
- 权重为正时，先按 Mesh 世界旋转反旋转 `Velocity * GetWorldDeltaSeconds`，
  从锁定位置中减去位移，再绕组件 -Z 轴旋转角色 yaw 差。
- RotationDifference 是函数局部量；仅 CharacterMovement.IsMovingOnGround
  时计算 ActorRotation 与 LastUpdateRotation 的归一化差，空中保持零。
  空中仍执行平移补偿。锁定旋转使用完整 rotator 差，位置旋转只使用 yaw。
- 世界 delta 与动画 DeltaTimeX 是两项独立输入，不能混为一个时间值。

原生对照发现一个小但可重现的精度边界：UE5.9 Win64 FRotationMatrix 在
double rotator 上使用 float `DEG_TO_RAD` 常量再提升到 double，而
RotateAngleAxis 的标量角度转换使用 double 常量。模型按各自路径处理，
没有放宽测试容差掩盖差异。

### SetFootOffsets

射线原点取 ik_foot 的世界 X/Y 和 root 骨世界 Z；起点高于该平面 50cm，
终点低于平面 45cm，原图使用 TraceTypeQuery1、简单碰撞、忽略自身。
`Trace()` 生成这段射线；真实物理查询及 CharacterMovement.IsWalkable 的
Godot 适配仍在下一阶段接入。

命中可行走表面时，完整位置目标为：

`ImpactPoint + ImpactNormal * 13.5 - (FootFloorLocation + WorldUp * 13.5)`。

坡面旋转使用 `Pitch=-DegAtan2(Normal.X,Normal.Z)`、
`Roll=DegAtan2(Normal.Y,Normal.Z)`、Yaw=0，并保留 MakeRotator 的 float
通道边界。向下位置插值速度 30，向上/等高 15，旋转速度 30。

没有可行走命中时，保留传入的 LocationTarget 引用值，局部旋转目标仍为
零；IK 关闭时清零位置/旋转偏移，不改写传入位置目标。总 UpdateFootIK
每次调用将两脚位置目标局部变量初始化为零，所以正常无命中帧不会复用
上一帧的地形目标。

### ResetIKOffsets：保持当前资产的实际不对称行为

只看名称容易误判原图。实际 Blueprint 反射执行已确认，本地 V4 资产：

1. 以 15 平滑左脚 FootOffset_L_Location 到零；
2. 以 15 平滑右脚 **FootLock_R_Location** 到零；
3. 连续两次以 15 平滑左脚 FootOffset_L_Rotation 到零；
4. 不修改右脚 FootOffset_R_Location / Rotation、左脚锁位置、两脚锁旋转
   或脚锁 alpha。

模型明确实现此行为，编译器拒绝把右锁位置静默改成右偏移位置。后续如果
修正源资产，必须同步更新对应模型与原生对照；本批没有改 UE 资产。

### UpdateFootIK

先左脚锁，再右脚锁，之后按 MovementState 分支：

- None / Grounded / Mantling：左地形偏移、右地形偏移，再用两个局部目标
  更新骨盆。
- InAir：用零目标更新骨盆，再执行以上原始 ResetIKOffsets。
- Ragdoll：此函数不继续更新偏移/骨盆，前面的脚锁更新仍执行。

该分支调度已经编译校验并实现为纯候选更新。它尚未被生产 Worker 每帧
调用；当前完成的是完整属性函数组件，不是最终腿部节点链或物理集成。

## 原生验证与测试

按 `ue-diagnosing-plugin-build-load` 技能重新执行完整 Editor 目标构建及
插件审计，通过后才启动冷加载 commandlet。未修改原生插件源码/描述符。

`export_foot_reset_trace.py` 使用 Unreal 反射 `call_method("ResetIKOffsets")`
执行真实编译后 Blueprint。属性为 EditDefaultsOnly，不能在临时 AnimInstance
上通过 Python 编辑；最终探针在隔离 commandlet 中暂存 CDO 字段副本、设置
测试输入，finally 恢复全部字段并逐项核实，输出 `defaultsRestored=true`、
`assets_saved=0`。这不是对磁盘 UE 资产的修改。

初次临时实例写入失败、随后错误读取函数局部目标为成员的尝试，均保留
日志；最终探针仅采集真实成员字段并成功退出。这些早期失败不算通过证据。

- UE Blueprint 重置探针：30/60/120 Hz，每档连续 4 次执行，共 12 行前后
  状态，Godot/Core 模型对照通过，并检查应保持的字段。
- UE Kismet 脚锁数学探针：18 组地面/空中、Mesh 旋转、角度跨界及不同
  delta；位置误差不超过 `1e-8` cm，旋转小数点后 10 位一致。
  它验证的是实际 Kismet 数学链，不是带角色/组件的完整 SetFootLockOffsets
  Blueprint 调用。
- 首版数学探针误将 Python 构造前的 double 当作输入记录；最终版记录实际
  Unreal Rotator 值，保留 MakeRotator 的 float 参数转换。修正后原容差通过。
- Import 专项共 34 项通过，含上述原生样本、上一批 24 组骨盆插值样本、
  图规则变更拒绝、权重门控/重复捕获、地形目标/无命中/关闭、跨帧局部量、
  全局分支以及世界/动画时间分离。
- Debug 优化构建 0 警告、0 错误。
- 实际分层生产 parallel 600 帧通过，result=`946C9F387EA43F26`、
  fullPose=`6B39A65B68F3FAE3`、root=`A4F6C26CBAB8A0E7` 保持，事件 28、
  lag/stale 0。这验证新定义可加载且既有路径未回退，不代表新脚部模型已
  在生产每帧执行。

证据文件：

- `tests/Als.Import.Tests/Fixtures/FootIk/native_lock_math.json`
- `tests/Als.Import.Tests/Fixtures/FootIk/native_reset_trace.json`
- `artifacts/foot-lock-native-math-final.log`
- `artifacts/foot-reset-final-trace.log`
- `artifacts/test-results/foot-input-native.trx`
- `artifacts/foot-input-worker-parallel.log`

## 完成边界与下一项

模型和正式图定义编译已完成。下一项是最终 Foot IK 的虚拟骨控制、原生
允许伸展的 TwoBoneIK、组件空间应用顺序和 Update 时的曲线 alpha。完成
节点链后，把本批属性更新与真实 socket / 物理查询 / 上一完成曲线接入
统一候选事务，再整体替换 Worker 的旧脚部处理。

当前 Demo 视觉不因此改变：默认完整上身入口尚未切换，支撑脚、滑步、
交错步/换髋、平台以及 UE 多帧/人工验收仍未关闭。普通分支根生命周期、
原 P5A 剩余项至 P7 保持，音频暂缓。全 Core 既有 23 项失败、Import 分配
稳定性和 p95 2.559ms 超过 2.5ms 的既有问题没有在本批关闭。
