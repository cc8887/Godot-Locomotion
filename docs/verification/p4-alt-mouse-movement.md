# Alt、鼠标转向和横移修复记录

日期：2026-09-09。工作分支：`feature/p5a-events-actions`。

## 问题与处理

1. **转身时 Demo 退出**：人工运行日志在 frame 1229 报告 `InvalidSelection`，随后 Debug 失败处理主动退出。真实 2 秒 Turn 片段在约 1 秒的相位边界上，两次浮点相位相减会放大 ULP 误差，误拒绝合法选择。校验改为比较“上一相位 + 本帧行程”的正向和，保留其他时间、速率和方向约束。没有通过忽略异常或关闭失败检查掩盖问题。
2. **转身时整个身体倾倒**：资产标记为 `ForceRootLock=true`、`RootMotionRootLock=RefPose`，原动画库只重写轨道路径，没有执行锁根。导出的根骨骼变换因此直接进入骨架。绑定阶段现在把对应根轨道的值设置为目标骨架参考姿势，保留轨道类型、关键帧时间和非根骨骼动画。当前处理限于 `ForceRootLock && !RootMotionEnabled && RefPose`，不改动作的 Root Motion 提取数据，也不宣称已经支持所有锁根模式。
3. **横移混入跑步，以及低速反向甩腿**：圆形归一化坐标会越过六方向步态采样多边形。例如纯右步行原映射为 `(0.5, 0)`，实际步行边界是 `(0.353553, 0)`。现在使用方向射线与当前步态采样边界的交点，并按已有 `Stride` 回到中心姿势，避免速度接近零时仍保持完整步幅。这是当前 Godot 图的低速混合修正，不等于完成 UE 全套 stride warping。
4. **IK 把抬脚压回地面、带低骨盆**：地形偏移现在从角色脚底平面计算；未锁定的脚保留动画抬脚高度和朝向，再叠加地形修正。原有绝对目标 API 默认行为保持不变。
5. **锁脚释放时旋转突跳**：之前在已混合的摆腿姿态上应用满权重大腿约束，即使锁定只剩约 4% 也会产生明显扭转。约束现在先作用于锁脚分量，并随锁定权重退出，再加入动画姿态。

## 对照依据

- 本地 UE `Engine/Source/Runtime/Engine/Private/Animation/AnimSequence.cpp` 的 `bForceRootLock` 分支。
- UE `Engine/Source/Runtime/Engine/Public/Animation/AnimCompressionTypes.h` 的 `ResetRootBoneForRootMotion`：RefPose 使用参考根骨骼变换。
- 本地 ALS-Refactored `Source/ALS/Private/Nodes/AlsRigUnit_FootOffsetTrace.cpp`、`AlsRigUnit_ApplyFootOffsetLocation.cpp` 和 `AlsRigUnit_ApplyFootOffsetRotation.cpp`：地形偏移叠加到动画脚部目标，而不是替换整个动画姿态。
- `AlsAnimationInstance.cpp::ConstrainFootLock`：先约束锁定状态，再混合输出。Godot 当前实现仍保留自己的物理腿链求解，不宣称逐项完全等价。

## 多帧证据

新增 `p4_movement_visual_smoke.tscn`，通过现有输入适配器和相机入口回放 Alt 步行、D 横移、A 横移、W 前进、停止转身。快速版本增加水平反向振荡和垂直鼠标转向。

每组 720 个连续提交帧，固定 60 Hz，每 6 帧在渲染完成后保存截图，共 120 张；每帧同时记录输入、Core 结果和修正前后脚部位置、旋转。截图时暂停运行，避免 HUD、姿态和帧号错位。包含纯动画/仅 Aim 的诊断模式。

最终输出位于仓库忽略的 `artifacts/alt-accepted-normal` 和 `artifacts/alt-accepted-rapid`。两组共 1440 帧、240 张原始截图。`frames.json` 是逐帧原始证据，接触图由 `scripts/analyze-p4-movement-capture.ps1` 生成。

- 快速回放的转身结束异常曾达到右脚单帧约 93°；最终同一帧约 0.44°。
- 常规回放的锁脚释放异常曾达到左脚单帧约 85°；修正后整段最大约 17°。
- 最终快速回放最大约 25.7°，对应动画自身的方向切换，没有发现 IK 额外放大的突跳。
- 新回放门禁检查连续提交、错误数、实际移动和 Turn 覆盖；该固定 60 Hz 测试路径中脚部单帧旋转不得超过 30°。这不是对任意速度或任意动作的通用阈值。
- 每张最终截图检查头部相对骨盆的向上分量；另有 8 个站立/蹲伏 Turn 加 1 个 Idle 共 909 个直接采样点的直立检查。

![转身修复前后](../../artifacts/alt-accepted-rapid/turn-before-after.png)

![横移连续帧](../../artifacts/alt-accepted-rapid/movement-contact-sheet.png)

## 回归结果

- Godot Debug 构建：0 警告、0 错误。
- Core Release：1376 项通过，过滤掉 `AlsP5aGoldenTests` 和 `TraceSchema`；Turn 时间边界用例包含 30/60/120/240 Hz、正负 90/180 度及站立/蹲伏。
- P3b 动画图：步态坐标、半速、接近零速的左右映射和方向矩阵通过。
- `verify-p4-pose.ps1`：完整通过。覆盖 P4 图、姿态写入、单线程/多线程 Foot Placement 和 late transaction rollback，以及现有零分配门禁。
- Foot Placement 保留平地、斜坡、台阶、移动/旋转平台、跳跃释放、换底座、传送释放和故障回滚验证；接触高度检查显式允许动画摆腿高度，没有删除地面下限检查。
- 新增抬脚姿态保持、非有限参考坐标拒绝且不写骨架的测试。
- P5A Runtime Binding 烟测通过；重建 Oracle 后，现有独立原生 fixture 验证通过。本次没有重新导出 UE 资产或生成新的原生 fixture。

## 复现命令

在仓库根目录运行，将 `$godot` 设置为当前 Godot Mono console 可执行文件。

```powershell
dotnet build GodotALS.csproj
& $godot --path . --fixed-fps 60 --disable-vsync res://scenes/tests/p4_movement_visual_smoke.tscn -- --capture-dir=../GodotALS-p5a-events-actions/artifacts/manual-replay --capture-step=6 --rapid
./scripts/analyze-p4-movement-capture.ps1 -CaptureDirectory artifacts/manual-replay
./scripts/verify-p4-pose.ps1 -GodotExecutable $godot
```

## 边界与待人工确认

自动回放注入的是现有适配器和相机接收的等价输入，不是操作系统层面的实体键鼠事件；仍需要人工按住 Alt 并转鼠标确认操作手感。测试不代替完整十分钟性能验收，也未扩大到所有 Overlay、道具和动作组合。

过程中曾出现一次可视进程启动早退，以及一次独立性能进程无完整堆栈退出；重试后最终可视回放及整套 P4 验证通过。现有证据能确认上面列出的确定性故障已修复，不能据此保证所有引擎级偶发退出都已解决。

相机、输入适配器文件和受保护的 P4 主规划文件哈希保持不变。保留开始时已有的 P5A 未提交修改；本次不自动 commit、合并或回退用户修改。
