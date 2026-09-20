# 第七批：Stop Plant 姿势组件

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
范围：完整性补完 A 的固定落脚采样与姿势运算。**尚未接入可玩 Demo 的外层 Stop 状态机**。

## 实现

- `AlsMeshSpacePoseBlend`：单个非加法层，旋转在 Mesh Space 混合，位置和缩放仍在
  Local Space。先分别累积源/目标旋转，再通过混合后父骨骼旋转还原局部旋转。
  零权重子骨骼也必须完成还原，不能直接复制局部旋转。支持原地输出、严格阈值，
  拒绝无效父链、非有限权重及错位重叠的输入/输出。
- `AlsStopPlantComposer`：六个固定源按 F/B/LF/LB/RF/RB 排列。左右 BlendList 先各自
  归一化姿势，再由 F/B/L/R MultiWayBlend 混合，最后覆盖源配置的单侧骨骼分支。
  不能将两级姿势混合平铺为六个叶子的一次加权。
- `AlsLocalPoseClip`：从 Cycle 原采样器抽出实际骨骼轨道绑定和局部采样，Cycle 与
  Plant 共用，不改变导入动画或其关键帧。初始化时验证轨道和骨骼，采样用秒为单位。
- `AlsStopPoseSampler`：消费此前编译的 Stop profile，缓存左右各六个固定采样，
  保留不同 SourceNode 的配置身份。按物理骨骼名称与父链映射到 Godot 骨架。
  初次相关选择器直接选择目标，之后右 RB 使用 .1 秒 Linear，返回默认侧为 0 秒；
  左侧选择器双向均为 0 秒。零贡献选择器不更新，其状态由调用者显式携带。
- 曲线用当前实际贡献的固定源求值，Override 保留仅在 Base 中存在的曲线，最后写
  对应 `FootLock_L/R=1`。该写入尚未进入可玩 Demo 的 P4 Foot Lock 消费路径。

组件没有播放时钟、Notify 分发或 Root Motion 抽取；固定采样不推进源动画。
准备状态为值类型，姿势输出由调用者提供，每个实例独占临时缓冲。
两个 Plant 实例可分别保留选择器状态；尚未建立 P5 的正式槽位/绑定身份。
禁止后续将这些固定源统一视作 `als_cycle/pose` 的单个播放身份。

## 原生依据与探针

本地 UE 5.9 源码：

- `AnimationRuntime.cpp::BlendPosesPerBoneFilter` 的 MeshSpaceRotation 分支及 BlendCurves。
- `AnimNode_BlendListBase.cpp`：首次有效子项零时长；重选时按剩余权重缩短时长；
  当帧推进；只更新相关子项；两个姿势先混合再交给下游。
- `AnimTypes.h::FAnimWeight`：相关权重严格大于 0.00001，满权重大于等于 0.99999。
- `assets/config/v4_stop_graph.json`：真实引脚时间、两个分支、枚举值及 FootLock 写入。

按 `ue-diagnosing-plugin-build-load` 的约束执行完整项目 Editor target 构建与插件
审计，再冷启动只读 commandlet。前两次编译发现并修正枚举作用域、FQuat 模板整数字面量
问题，没有在失败产物上执行 commandlet，也没有复制 DLL 或保存 UE 资产。

最终构建 fingerprint：`9E8532CB677487E6D69DEC93ADDE27A3F7B429B537E729D913249DC046A2301F`。
最终 commandlet 日志：`artifacts/mesh-space-blend-native-20260910.log`。
退出码 0，汇总 0 errors / 0 warnings：

```text
ALS_MESH_SPACE_BLEND_OK cases=80 bones=79 assets_saved=0
```

探针直接调用 `FAnimationRuntime::BlendPosesPerBoneFilter`。真实 ALS Skeleton 共 79 个
骨骼项（包含虚拟骨骼），合成输入局部姿势覆盖变化旋转、四元数反号和非均匀缩放。
四类掩码：左腿、右腿、全身、带空洞的逐骨骼掩码；十个 alpha 含阈值两侧；
分别请求 ISPC 开关 0/1。共 80 x 79 = 6320 个输出变换对照，比较位置、缩放和旋转。
旋转对照允许四元数同旋转反号，误差限 0.000006；位置/缩放误差限 0.000003。

fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_mesh_space_blend_native.json`。
SHA256：`22F7E22E7B93D0E57466F65196A072C2D58D64ABA3BBB360BD0D2A885D9008A3`。

这是原生骨骼混合运算对照，不是完整 AnimBP 或实际 Stop 的 UE 多帧回放。
没有执行普通 GUI Editor 重启、项目数据验证或打包，不作为插件发布验收。

## 验证结果

- Core Locomotion：319/319，包括原生对照、零权重子骨骼补偿、原地输出、无效输入、
  两级混合与错误平铺的反例、六方向固定源、阈值及 0 B 热路径检查。
- Godot 构建：0 warnings / 0 errors。
- `stop_plant_smoke.tscn`：30/60/120 Hz，12 固定源，4608 个实际轨道分量采样检查，
  2448 个骨骼位置/缩放/Mesh Space 旋转检查，246 个选择器/重试检查，72 个曲线检查。
  三次各 1000 次活动采样/曲线求值均为 0 B 托管分配。用的是导入资源的实际采样，
  不证明导入前后的动画压缩/坐标转换已经与 UE 逐帧一致。
- 初次 Godot smoke 发现骨名大小写差异。修正为项目已有的 OrdinalIgnoreCase 约定，
  同时仍验证唯一映射、骨骼数量和精确父链，重新运行通过。
- `standing_cycle_smoke.tscn`：9 组频率/相位、63 次全骨骼检查，9 次普通及 9 次中断
  回滚、首次候选失败恢复，36 个方向组合，空闲及活动路径 0 B，结果与第六批相同。
- Cycle 单/多线程各 180 帧：result `BE7BC81EDF4F1B91`，full_pose `B684F207D0AC5DA1`，
  root `309E8D0E0BEEB2CB`；lag/stale 为 0。共享采样器提取未改变第六批输出。
- `git -c core.safecrlf=false diff --check` 通过。

```text
STOP_PLANT_OK rates=30,60,120 fixed_sources=12 sample_components=4608 bone_checks=2448 selectors=246 curves=72 alloc=0B outer_state_machine=not_connected
```

没有重新录制移动截图或发布滑移改善数据，因为可玩 Demo 尚未进入本批 Plant 组件。
组件的确定性重试测试不等同于完整 P5 事务回滚或事件验收。没有执行全量 Core/P5A/P7。

## 新确认的缺口与下一步

已有只读 `artifacts/locomotion-port-audit/graphs.json` 显示 Stop 使用的 `(N) Locomotion
Detail` 并非仅有 Cycle：Walking/Running 引用 Cycles，Run Start、Walk->Run、First
Pivot、Second Pivot 在 Cycles 之上混合四向 `ALS_N_LocoDetail_Accel_F/B/L/R` 加法动画。
这些分支未进入当前 Cycle 实现。旧 K2 导出中的 Detail 状态机 nodes 为空，只能证明
子图和姿势连接存在，不能从中猜测边、默认时长、事件或重置规则。

下一步需用原生导出补全 Detail 的状态/播放器合同，并接 ShouldMove 的实际移动输入、
外层 NotMoving/Moving/Stop、Pre-Stop/Lock/Plant 与初始化/失去相关性重置。
ShouldMove 不能用 `speed > 0` 冒充 `(IsMoving && HasMovementInput) || Speed > 150 cm/s`。
最终仍需统一 P5A 的播放身份、实际 Sync 时间、曲线与源事件提交，完成动态分层，
再继续原定 P5B/P5C/P6/P7。音频暂缓，人工确认的镜头/输入不改。
