# Refactored 原始站立/蹲姿资源

在 `D:/GodotALS`、`main` 接入实际 Layering 两个 SequenceEvaluator 的原始姿态资源。普通 Demo 尚未切换完整 Refactored 图。

## 来源与实现

原生编译清单节点 37 对应 `/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose`，38 对应 `A_Als_Crouch_Pose`。两者均固定 ExplicitFrame=0，teleport=true，DoNotSync，无组、无动态输入绑定或更新回调；不是新的独立动画播放身份。

新增只读脚本 `export_refactored_base_pose_inputs.py`，按已提交清单读取这两份实际资产，导出完整原始轨道、骨架/虚拟骨/重定向及 root lock 元数据、原始富曲线集合；另行导出原生姿态参考。两份源各 2 键、时长 1/30 秒，68 实体骨、79 逻辑骨，无浮点曲线，forceRootLock=false。未人为补入 PoseStanding/PoseCrouching 等上游控制曲线。

新增 `AlsRefactoredBasePoseCompiler`，校验输入→清单→原图的哈希链、两实际源闭包、固定帧/同步/回调策略及原生骨架。资源按原生节点索引绑定，每 worker 独立 sampler，Evaluate 固定采样第 0 帧，保持原生厘米与骨局部坐标。

复用既有精确 raw sampler、retarget、逐键虚拟骨生成及曲线编译代码：在 AlsMantlingPoseCompiler 中提取内部 standalone sequence 入口，原 Mantle 的根轨道和 Montage 闭包验证仍只走原接口且保留；曲线编译增加内部 embedded 输入入口。未把这两个基础姿态塞入 V4 动画编号表。资源类暂复用 AlsMantlingPoseSource/CurveSource，名称不代表基础姿态拥有 Mantle 动作或 motion。

## 验证

- 完整 UE Editor target 构建 0 action，审计通过，fingerprint `6A36CA669CAA388864B0C247DC956056B6CD5200FB0EBDCD1191CE13D3DB27F1`。构建日志前缀 `20260924T170601442Z-ab36db4c8e9a495eaf6a7e04c1d78b27`。按 ue-diagnosing-plugin-build-load 技能先完成审计再启动导出。
- 两次冷导出都实际退出 0，0 Error/Warning，输出 `ALS_REFACTORED_BASE_POSES_OK sources=2 samples=40 assets_saved=0`；输入及参考均字节一致。
- 输入 `refactored_base_pose_inputs.json` SHA256：`61A14AEEA81197422AB3199B9E82970151211B4F2FCF9E64F3B94A75507FC502`。
- 参考 `refactored_base_pose_reference.json` SHA256：`A53B327B2785DAE7A94907F8EE8B9850F1E806EC9E991AB2CFFA485006D64807`。
- 两源×5时刻×4原生上下文（raw、retargeted、asset root lock、extract root lock）：40 姿态、3160 骨骼结果（440 虚拟骨）。最大位置差 `4.0282587826995692E-14` cm、四元数分量差 `2.2204460492503131E-16`、scale 差 0；预设容差分别 1e-4 cm/1e-6/1e-6，未调整。
- 两资源各 4 owner×120 次固定帧采样，共 960 次结果一致；错 hash、缺资产、换源、非零帧、同步策略变化和曲线源不一致均拒绝。
- Import Release `AlsRefactoredBasePose|AlsMantling`：104 通过、0 失败/跳过，含新增 7 项及既有 Mantle 精确姿态等回归。`dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning、0 error。
- 日志/TRX/重复输出在 `artifacts/refactored-base-poses/`。无本批测试失败。仅导出脚本与 C# 改动，无 UE 插件/config 变更；没有重新执行普通 Editor/DV/打包、Core 全量或新 Godot 场景验证。

## 剩余接入

真实基础姿态资源已具备；接下来将它们绑定到实际 94 节点图的 SequenceEvaluator，映射 PoseState 和分层控制属性，处理完整 Refactored VB、区域 Slot 和 Head/View，再接普通 Mantle 宿主。当前资源保持原始 Refactored 逻辑布局，不能直接当作 V4 的虚拟骨缓冲。

既有物理稳定性 9/12、Flail 0/3、复杂相机碰撞、Mantle 探测/motion 生命周期和最终十分钟性能预算未关闭。用户 project.godot/P4 规划/头颈诊断/uid 保留；头颈拉伸、道具物理、音频维持暂缓。
