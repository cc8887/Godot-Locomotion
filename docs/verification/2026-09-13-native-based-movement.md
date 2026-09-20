# 原生基座旋转核对与实际运输差异

第一百五十七批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。
上一批为生产速度修复与验证进展。本批进一步核实原生行为，新增可复用
UE/Godot 对照场景；没有宣称平台脚锁已修复，也没有改变 Demo 的朝向规则。

## 修正上一批的归因

“平台旋转而角色 yaw 保持 0”不是足够的漏移植证据。实际 V4 Character
CDO 的三个 UseControllerRotation 标志、CMC OrientRotationToMovement 和
UseControllerDesiredRotation 都为 false；IgnoreBaseRotation 为 false。

UE UpdateBasedMovement 调用 Character.FaceRotation。APawn.FaceRotation
只应用启用的控制器旋转轴；如果没有生效，CMC 还检查 OrientRotationToMovement
或 UseControllerDesiredRotation。本资产两个条件也未启用，故角色保持原
朝向，UpdateBasedRotation 得到的角色旋转差为零，控制器也保持原朝向。

这已经由实际函数验证，而不只是阅读默认值推断：

| 场景 | 帧数 | 末帧平台 yaw | 角色 / 控制器 yaw |
| --- | --- | --- | --- |
| 原资产，30 Hz，有控制器 | 180 | -120.321136° | 0° / 0° |
| 原资产，60 Hz，有控制器 | 360 | -120.321136° | 0° / 0° |
| 原资产，120 Hz，有控制器 | 720 | -120.321136° | 0° / 0° |
| 原资产，60 Hz，无控制器 | 360 | -120.321136° | 0° / 0° |
| 正对照，60 Hz，启用 UseControllerRotationYaw | 360 | -120.321136° | -120.321136° / -120.321136° |

各场景基座确实携带角色沿圆周移动，自身 Velocity 保持 0。正对照保证
探针并非遗漏基座旋转更新。native yaw 与 Godot yaw 的符号转换保持原约定。

`tools/unreal/export_based_movement_trace.py` 创建原 Character Blueprint、
真正可移动 StaticMesh 基座，以及需要时创建并 Possess 的控制器。
`AdvanceMovementProbeBase` 仅用于同一 Editor 世界的临时对象，通过真实
MovementBaseInterfaceData 初始化，再调用实际 UpdateBasedMovement 和
SaveBaseLocation。没有替代 CMC 类或重写上述引擎算法。

范围明确为受控基座变换和实际 CMC 基座函数，不包括自动 World Tick、
Character Blueprint Tick、动画 Tick 或整角色脚锁原生配对。

## 冷 / 普通 Editor 与工具问题

冷输出 `artifacts/based-movement-157-final.json`，普通 Editor 输出
`artifacts/based-movement-editor-157.json`。defaults 和全部 5 条轨迹共
1980 帧逐字段完全一致。两次进程均退出 0。

首次用 EditorActorSubsystem 放置原生 Actor，在无界面命令行中触发
PlacementSubsystem/AssetFactory 空指针崩溃，未进入基座运算；失败日志
`artifacts/unreal/based-movement-157.log` 保留。改用只在 Editor 世界创建
RF_Transient 对象的 World::SpawnActor，绕开资产放置 UI 工厂。
下一次 Python 的 get_pawn 方法名不匹配，并且 EditorActorSubsystem 不接受
原生创建的 Controller 清理路径；改为原 Pawn.GetController 验证 Possess、
Actor.DestroyActor 清理。旧日志 `based-movement-157-native-spawn.log` 保留。
没有因为导出失败修改项目资产或放宽原生结果要求。

## Godot 实际运输仍不等价

新增 `MovementBaseOracleSmoke`，使用同一 UE 导出的逐帧基座 yaw，驱动真正
的 AnimatableBody 和生产原移动 Runtime Motor。先做静止接触帧，与 UE 的
SetBase/SaveBaseLocation 初始化对齐，再进行六秒空输入配对。
对照忽略垂直初始摆放高度，仅比较相同半径 2 m 的水平角色位置。

| Hz | 配对帧 | 最大水平位置误差 | 最大半径偏差 |
| --- | --- | --- | --- |
| 30 | 180 | 3.505208 cm | 2.449489 cm |
| 60 | 360 | 2.233718 cm | 1.221275 cm |
| 120 | 720 | 0.775651 cm | 0.612855 cm |

各场景均保持物理支撑、自身速度为零，角色朝向与 UE 一致；位置对照未通过。
补上初始站稳步骤后与首次结果相同，排除未建立初始平台的解释。这里只能
确认当前 Godot 平台运输路径与原生不等价；尚未把误差全部归因于某一行
Godot 引擎实现或宣称已验证完整碰撞语义。

日志 `artifacts/movement-base-oracle-157-{30,60,120}-based.log` 明确写出
position_match=False。本批用 `--diagnostic` 保留测量并正常退出；它不是
通过记录。去掉该选项即按最大 0.1 cm 门槛失败，门槛没有根据结果放宽。
场景参数为 `--als-hz=60 --native-base-trace=<本地原生 JSON 绝对路径>`。

## 下一段实施范围

先完善基座运输，再补基座脚锁，两层分别做配对：

1. 原 CMC 保存旧基座位置/旋转，从角色胶囊底部的基座相对位置计算新世界
   位置，执行带碰撞的位移；基座缩放不参与原函数的旋转/平移矩阵。
   Godot 适配需避免与 MoveAndSlide 的现有平台携带重复应用，并纳入实际
   支撑身份、基座切换/移除、阻挡、坡面、离台继承速度及生命周期快照。
   原 CDO 四个 ImpartBaseVelocity/AngularVelocity 标志全为 true。
2. 保持上文已核实的原角色/控制器朝向规则。不能为解决脚漂移强制旋转相机。
3. 当前脚部属性更新是 V4 SetFootLocking/SetFootLockOffsets，只保存组件空间
   位置/旋转并按自身速度和角色旋转差补偿，没有 ALS-Refactored 的基座空间
   锚点。原详细规划中的稳定平台支持需要补后者，必须明确记录版本边界。
4. 对照锁定版本的 ALS-Refactored RefreshMovementBaseOnGameThread、
   ProcessFootLockBaseChange、ProcessFootLockTeleport、RefreshFootLock 与
   ConstrainFootLock，一起实现基座空间锚点、切换/传送、重新锁定、失效/
   移动/空中释放，以及大腿/脚旋转约束。不可只加无限保持的锚点，否则
   旋转平台长时间运行会把腿扭成螺旋。
5. 上述 C++ 的源默认 ThighAngleLimit=90°、FootAngleLimit=40°，大腿轴从
   参考骨骼的 pelvis 子树求得；这是有来源的约束，不能替换成任意外观夹角。
   新运行时须明确与 V4 曲线协议、骨骼轴及原静态地面路径的适配。
6. 用 30/60/120 Hz、单/并行、平台切换/移除/传送、长时间旋转和失败重试
   验证之后，再推进完整默认入口与人工验收。此前 14.23 cm 脚漂移不能仅用
   本批胶囊位置误差解释，也尚未关闭。

本地 C++ 参考在 `../AdvancedLocomotionSystemV/Plugins/ALS/Source/ALS`；
仓库锁定提交 `b754d6f0f2bb03741d301f8fb88077ebfe561e17`，有 UE 5.9 兼容补丁。
该版 CMC 默认 IgnoreBaseRotation=true，角色和相机还有独立基座处理，不能
与 V4 原图混称为相同算法。平台修复继续归 P3/P4，P5A 剩余项至 P7 不缩减。

## 构建与审计

使用 `ue-diagnosing-plugin-build-load` 技能。两次原生源码改动后均完成整
Editor 目标构建与三插件审计，并在最终退出 0/AUDIT_PASS 后启动 UE。
最终日志前缀
`../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T071044207Z-9086e07f0f9741e58fdb39e2d2118b35`。
BuildId `a9864da3-504b-48ca-b30c-39953869f477`，fingerprint
`4C93A2BDC9F5431EBD8B1420A991DF0E1E86283A80C695422EBA6A1138ACB28B`。

项目/仓库两份探针 SHA256 相同：
`D67F53A66175C954CC0B504D3A3B624D155D874608681034845A8FE3E4E3AA06`；
两份头文件相同：
`D01A40A96A4E0151CD730280B507E945A4D2E2F256C162077FD9426E0F7295E2`。

冷运行 0 error/1 既有 PawnActionsComponent warning；普通 Editor 仍有两条
既有 AutomationTest Condition failed。DataValidation 退出 0，0 error/3
warning，含旧导航网格版本和 PawnActionsComponent 警告，未称全项目无警告。
隔离包 `artifacts/unreal/AlsBasedMovementPluginValidation-20260913-157`
成功，日志 `artifacts/unreal/based-movement-package-157.log`。未部署/复制
打包 DLL，打包后项目插件审计再次通过。Godot 优化 Debug 构建成功，未把
第 156 批完整动画/渲染结果宣称为本批重新运行。

技能引用的 superpowers 调试/完成验证技能不在当前可用目录；以完整构建、
原始失败日志、实际原生正对照、冷/普通运行和独立数值测量落实验证。
