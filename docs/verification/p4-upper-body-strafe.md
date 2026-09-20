# 上身、横移侧身与换髋修复记录

日期：2026-09-09。分支：`feature/p5a-events-actions`。

## 提交边界

按用户要求，先将上一轮已验证的改动提交为 `d6b45e3`，提交说明为 `fix(godot): stabilize playback identity and movement pose`。本文对应的上身修复是该提交之后的独立工作区修改，未自动再次提交或合并。

## 原因与修复

1. 双臂贴身：Lean 导出的是绝对姿势，原图直接把它送入 `AnimationNodeAdd2`。即使 Lean 很小，只要启用叠加，也会把完整基准姿势再次加到身体上。现在先用 `AnimationNodeSub2` 减去配置中已有的 `ALS_N_Run_BasePose`，再叠加差量；显式将五个分支的 `sub_amount` 设为 1，避免 Godot 默认值 0 导致没有真正相减。
2. 横移没有侧身：LF/LB、RF/RB 是不同髋部朝向的侧向循环。原方向射线在纯横移时落在两个变体的中点，把左右约 45 度的髋部朝向抵消。现在根据前后运动半区选择髋变体，再映射回当前步态多边形，不再把纯横移默认为两个变体的均值。
3. 换髋过渡：侧向 70 到 110 度区间保留进入时的前后髋选择，避免速度在侧向附近抖动时频繁切换。前后髋权重完整切换用 0.2 秒；动画采样方向按 10 rad/s 限速，并重新投影到当前步态边界。左右反向时保留已有低速 Stride 衰减，角色物理运动和输入不经过这个动画限速。
4. 新增的髋选择、过渡量和动画方向放在已有 `PreparedApply` 中，仅随提交推进。丢弃或回滚帧不会提前改变下帧的髋部选择，动画姿势、足部曲线及播放成员继续使用同一份准备后的混合坐标。

这次没有改相机、输入适配器、Core 二进制帧协议、资产清单或 P5A 播放身份布局。蹲伏的四方向样本及冲刺单样本不使用站立六方向的髋变体映射。

## UE 对照依据与边界

- 本地 ALS-Refactored `AlsAnimationInstance.cpp` 的 `RefreshMovementDirection`、`RefreshRotationYawOffsets` 和 `HipsDirectionLockAmount`，以及 `AlsCharacter.cpp` 的 LookingDirection 目标朝向处理。
- 本轮实际动画来自 ALS V4，而不是 Refactored 的另一套动画资产。读取 V4 `UpdateRotationValues` 等蓝图函数及 `YawOffset_FB`、`YawOffset_LR` 两条向量曲线，共 722 个采样点。在正负 90 度处，对应侧向分量为零，因此不能用额外强加角色 yaw 来解释或掩盖缺失的侧身。
- 直接采样 V4 四个侧向 Walk 片段，在相位 0.25，前后髋变体确实具有相反侧身。修复后的图中左右侧移的髋部横轴朝向相差 93.91 度、肩部横轴相差 50.43 度。这些是两个姿势之间的角差，不是给每帧额外旋转的角度。
- UE 读取前按插件构建与加载诊断流程完成 Editor target 构建审计；AutoTestTools 和 BlueprintLisp 通过。随后只读 commandlet 输出 `ALS_DIRECTION_AUDIT_OK curves=2 samples=722 assets_saved=0`，没有保存 UE 资产。
- 当前修复是适配已有 Godot 图和 V4 资产的运行时策略，不宣称完整复刻 Refactored 的 HipsDirectionLock 曲线、所有分层蓝图或 stride warping。蓝图导出器没有完整导出 AnimGraph 的 pose 节点。

原始只读结果位于 `artifacts/ue-direction-audit/`；读取脚本为 `tools/unreal/read_locomotion_direction.py`，输出目录由 `ALS_DIRECTION_AUDIT_OUTPUT` 指定。

## 回归与多帧证据

P3b 图烟测新增：六个躯干/手臂骨骼的近零 Lean 中性检查、同相位左右肩髋差异、侧向抖动保持、前后髋恢复、丢弃/回滚不污染，以及连续反向角速度门禁。既有步态、三种旋转模式、动作状态与资源生命周期测试通过。

`verify-p4-pose.ps1` 完整通过，包括单线程/多线程 Foot Placement、late transaction rollback、图与姿态写入。现有姿态热路径门禁仍为 0 B 分配。P5A Runtime Binding 烟测通过，包含播放成员、布局及图摘要检查。

Core Release 按既有过滤范围运行，1376 项通过：`FullyQualifiedName!~AlsP5aGoldenTests&FullyQualifiedName!~TraceSchema`。本轮没有重跑完整 P5A Golden/TraceSchema 验收；最初误包含 TraceSchema 的长运行已主动中止，不计为通过。

三组固定 60 Hz 的最终渲染回放，每组连续提交 720 帧、每 6 帧截图一次：

| 回放 | 输出目录 | 截图 | 脚部最大单帧旋转 |
| --- | --- | ---: | ---: |
| 固定镜头 D、A、D 横移及停止转身 | `artifacts/upper-final-strafe` | 120 | 18.217 度 |
| 常规转镜头并移动 | `artifacts/upper-final-alt` | 120 | 16.458 度 |
| 快速反向转镜头并移动 | `artifacts/upper-alt-rapid-smooth` | 120 | 21.165 度 |

合计 2160 帧、360 张原始截图，均无运行错误；每组覆盖 101 帧 Turn。原有 30 度脚部单帧门禁保持不变。开发中的首轮快速回放曾在 frame 426 达到 34.260 度，因而加入动画方向连续过渡；最终该帧约 14.67 度。

横移测试专用地面扩大到 30 米，避免测试过程中走出原平台，并逐帧检查仍然着地。正式 Demo 地面没有修改。`frames.json` 记录逐帧输入、结果与脚部姿势；`body-frames.json` 记录截图帧的肩、手臂、髋等世界姿势。截图等待渲染完成，并与暂停的提交帧对齐。

修复前图仅使用仍在地面上的 frame 180；早期 `upper-before` 的后续离地帧不作为有效地面验收证据。

![同一横移帧修复前后](../../artifacts/upper-final-strafe/upper-body-before-after.png)

![右移、左移、再次右移](../../artifacts/upper-final-strafe/strafe-directions.png)

![包含左右切换的连续帧](../../artifacts/upper-final-strafe/movement-contact-sheet.png)

## 复现

在当前仓库根目录运行，`$godot` 指向 Godot Mono console 可执行文件：

```powershell
dotnet build GodotALS.csproj
& $godot --headless --path . res://scenes/tests/p3b_animation_graph_smoke.tscn
./scripts/verify-p4-pose.ps1 -GodotExecutable $godot
& $godot --headless --path . res://scenes/tests/p5a_runtime_binding_smoke.tscn
& $godot --path . --fixed-fps 60 --disable-vsync res://scenes/tests/p4_movement_visual_smoke.tscn -- --capture-dir=D:/GodotALS-p5a-events-actions/artifacts/upper-replay --capture-step=6 --strafe
./scripts/analyze-p4-movement-capture.ps1 -CaptureDirectory artifacts/upper-replay -Strafe
```

常规转镜头回放去掉 `--strafe`，快速版本使用 `--rapid`。输出目录应分别指定，避免混用证据。

自动回放通过现有输入适配器与相机入口注入等价输入，不是操作系统实体键鼠操作。仍需人工检查 Alt 步行和左右反向的手感。本轮验证没有覆盖全部武器 Overlay、动作组合，也不代替十分钟性能验收。
