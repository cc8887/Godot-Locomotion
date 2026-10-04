# Lyra FootPlacement 原求解器与明确初始化的原生对照

2026-10-01，在当前主目录完成原 FootPlacement 配置的独立数学算子和候选帧宿主。ALS 原模型、68 根蒙皮骨继续复用，运算使用既有 81 logical bone 布局。本批仅关闭明确初始化条件下的 FootPlacement 算子；共同 ItemAnimLayers 执行组仍为 13/14，完整 SkeletalControls、Main、普通 Demo 和 Godot 场景碰撞尚未关闭。

## 原生初始化缺陷及验证范围

本机安装版 UE5.8.1 的 `AnimNode_FootPlacement.cpp:685`，`ResetRuntimeData` 对腿数据调用 `SetNumUninitialized`，随后只写 `Idx` 和 `Interpolation`。初次 Gather 虽写部分姿态与地面平面，`TimeSinceFullyUnaligned` 等首次使用字段没有确定初始化。安装版 `Array.h` 确认该调用不运行成员构造器。

保留原始独立探针 `AlsLyraFootPlacementLibrary` 和 ignored `foot_placement_v1_*` 三文件。首次采集成功，但独立进程复采被不可变 fixture 门禁拒绝：`Immutable FootPlacement fixture differs: native`，UE 实际退出 -1。这是明确的重复性失败，没有当作崩溃或成功处理。

原捕获与复采比较：3 条轨迹的初始历史不同，69 帧 before、66 帧 after 历史不同，126 帧完整输出姿态数据不同。初始平面中出现零或约 1e-307 的未初始化值。证据保存在 `artifacts/lyra-analysis/foot-placement-native-diagnostic.json` 和 `foot-placement-uninitialized-comparison.json`；失败日志保留。

新增独立 `AlsLyraFootPlacementSeededLibrary`。在临时节点第一次 Initialize 后、Cache 前，将两条腿的运行存储赋为原 `FLegRuntimeData()`，再执行实际原节点 Initialize/Update/Evaluate。这只定义独立探针的初始存储；没有修改 UE 源码、原探针、已保存 fixture 或 GASP 主项目。新的 `foot_placement_v2_policy.json` 明确标注 `definedInitialStorage=true`。之后的初始化仍执行原节点逻辑。

**两次 v2 原生采集一致，只证明定义初始状态下原求解公式可对齐。原 v1 重复性门禁仍失败，不据此宣称未经调整的 Lyra Main 或完整 FootPlacement 生产链通过。**

## 配置、碰撞与实现

绑定三种 Provider 的原编译 node105，加载时比较完整 settings 和依赖字节哈希。实际配置为 Manual 速度、所有曲线名 None，未绑定的速度曲线回退 60；Unlocked、两骨腿、AllLegs 骨盆求解、SuddenMotionOnly 补偿、开启地面和骨盆插值。无锁定和分离平面的非零分支不在原配置可达范围内，当前算子不声称支持那些配置。

`AlsLyraFootPlacement` 按原 UE 轴向/厘米实现：源脚及脚掌距离、组件移动与地面法线历史、球体扫掠请求、地面回退与空中回原脚平面、高度/旋转弹簧、落脚对齐与扭转校正、水平骨盆再平衡、双腿伸展范围、骨盆弹簧、抬跟/超伸/穿透/过度压缩修正，最后用原 FCSPose LocalBlend 应用骨盆和两个 IK foot。原 Update 的 delta 累积、counter 间断、Initialize 保留时间/counter及成功求值清零保持。

`AlsFootPlacementMath` 保留 float/double 边界、Plane/Transform、SphereDistToLine 与原临界阻尼运算顺序。首次 Godot 对照在第119帧失败，原因是高度补偿将 double 增量先转 float；原 UE 为相加后转换。已修计算次序，原误差门槛不变，失败日志 `foot-placement-godot.log`、`foot-placement-godot-2.log` 保留。

`LyraFootPlacementNodeHost` 持有独立骨骼和数据通道缓冲，准备/求值/取消/提交完整历史候选，拒绝外来、旧代、重复与失败候选；隐藏、仅更新、重复求值、每帧取消重试以及查询异常后恢复均覆盖。最终输出 view 随候选结束失效。

原生探针使用带真实物理场景的临时 GamePreview World、Character 和可旋转 Box，实际调用 `SweepSingleByChannel` 球体扫掠。Character 的 walking/falling、floor、速度和组件变换为显式受控观察，不是 UE CharacterMovement 自主移动。三组原六条 Sequence 在 ALS81 上独立求值。

Godot 输入源由已有原始 sampler 独立产生，不读取预期姿态；仅碰撞查询结果来自真实 UE hit 记录。查询起点、方向、-75/100 范围、5cm 半径及 complex 标志均校验。Core 碰撞通过 `IAlsFootGroundQuery` 注入；目前尚未接 Godot 物理场景，不称坡面玩法验收。曲线、typed attributes、RootMotion 原样透传。

## 结果

三 Provider × 30/60/120Hz × 6秒：

| 项目 | 结果 |
| --- | --- |
| 更新/历史对照及逐帧取消重试 | 3780 帧 |
| 姿态/骨骼 | 3231 / 261711 |
| 原生有效 Foot 求值 | 2049 次，Godot 实际发起 4098 个查询 |
| 原生附加查询记录中 walkable hit | 6102 个，包括 alpha 不活跃的记录 |
| 在地面历史 | 2796 个姿态帧 |
| 隐藏/仅更新/Initialize | 315 / 234 / 30 |
| 输出曲线包/整数属性/RootMotion | 3231 / 12924 / 3231 |
| 无效操作拒绝/查询异常后恢复 | 18723 / 69 |
| 最大位置/quaternion/scale误差 | 2.1953104400669003e-13 cm / 4.611102534756203e-16 / 0 |

严格位置 1e-8cm、quaternion 1e-10、scale 1e-12保持；float 历史逐位比较，空间历史按同级严格误差检查。查询失败不发布历史，取消后同一帧重试与 clean 求值一致。

两个 seeded UE 进程实际退出0，各0错误、794条原资产/临时依赖警告。508个源/目标资源包及676份之前 JSON逐文件字节哈希不变，包含原失败v1三文件。原v1/v2探针源码及外置source/package镜像哈希均检查。

ignored v2 三文件：requests 2034231字节、policy 9397字节、native 138181931字节。验证入口为 `tools/verify_lyra_foot_placement.py`；原v1失败证据也是其必需条件。

Debug、Optimize ExportRelease均0错误0警告。当前新增 Foot 测试及已有 SkeletalControls Update/Root/Weapon、LegIK、固定 Provider ALS Main、共同 Aiming 回归通过。没有修改普通ALS Demo、没有提交或推送、没有新增资产保存；用户已有修改保留。

## 后续门禁

下一步在同一个 FCSPose 中按原12节点顺序运行 HandRetarget→CopyBone→Root→RightIK→LeftIK→FootPlacement→LegIK→Weapon，并把更新与完整历史纳入同一个 ItemAnimLayers 实例事务。须用新的整链原生对照验证缓存/局部空间转换，不能简单拼接独立算子后宣称第14入口关闭。

之后仍需完整 Main 的 Aiming/Slot/Additives/SkeletalControls 原位置、真实最终曲线反馈、统一通知和换 Provider、Godot collision/平台、普通 Demo、运行画面和性能验收。原未初始化生产语义还需独立处理或明确上游初始化前提，不能从待办中消失。
