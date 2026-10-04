# Lyra 最终 Rig：ALS 参考骨架连续原生对照

2026-10-02。在主目录直接推进，未创建 worktree、提交或推送。本批完成显式 `AlsCompactReference` 配置下 Main73 的连续 VM、完整输出和初始化生命周期对照。整个 Lyra 移植仍进行中。

## 问题与修复

上一批已证明 ALS 参考绑定及完整 Construction 层级，但没有目标配置连续 Forward Solve 的原生证据。新采集的首帧暴露实际差异：Godot 骨盆位置与 UE 最大分量差 2.026711826359417cm，超过原 1e-8cm 门槛。提前比较纯工作寄存器进一步定位到足部法线弹簧；没有通过放宽阈值修复。

安装版 UE 5.8 的实际生命周期为：

1. `FAnimNode_ControlRig::HandleOnInitialized_AnyThread` 清除 `RefPoseSetterHash`。
2. 随后的已访问 Update 调用 `UpdateControlRigRefPoseIfNeeded`，再次按 ALS compact reference 设置 InitialLocal，并请求 Construction。
3. Evaluate 在 alpha 判断之前调用 `ExecuteConstructionIfNeeded`，即使 alpha 为零也会执行。
4. Construction 保存 CurrentLocal/Global、解析初始缓存、重置到初始姿态、执行 VM，再恢复 CurrentLocal；其执行会消耗 Rig delta。
5. Forward Solve 在本帧使用零 delta；下一次 Update 恢复正常物理 delta。

此前 Godot 只在 Initialize 执行一次 Construction，遗漏第二次请求。第一次修订仅补零 delta 和 Reset，仍在 PelvisCtrl 工作变换出现 2.249954111613178cm 差异：控制 offset 的局部/全局缓存必须按原 GetPose 顺序解析，不能用简单 Reset 代替保存/恢复。

现在 `LyraFootPlantRigPoseHost` 在同一角色候选中保存 reference setter invalid 与 pending Construction，Update 执行参考重绑，Evaluate 执行保存/Construction/恢复并消耗 delta，Commit 才发布这些历史。仅更新帧保留尚未执行的 Construction；取消重试和重复 Evaluate 均从同一 prepared 状态开始。alpha-zero Construction 同样消耗 delta。此行为只作用于显式目标参考配置，原 authored 路径保持原生回归。

`LyraFootPlantRigHierarchy.CaptureConstructionPose` 按元素顺序读取 local 后 global，`RestoreConstructionPose` 通过原局部写入语义恢复姿态。生产宿主执行原 VM；不会读取原生输出答案。

## 独立 UE 采集

新增外部 `AlsLyraRigTargetLibrary`，沿用原探针的观察位置与字段顺序，只对 transient Main73/独立 OperatorBool 节点设置 `bSetRefPoseFromSkeleton=true`。保留真实 Main 编译属性处理器；没有增加求解前全层级 observer，避免重现此前 observer 影响 PoseAdapter 缓存的问题。原探针源和原 JSON 不覆盖。

新的 BuildPlugin 使用短路径 `../GLRigSolve`，77 动作成功；外部产物位于 `artifacts/unreal/gasp58-lyra-rig-target`。两次独立 UE 进程均实际退出0、项目描述哈希不变，669 个源包与815份已有 JSON 保持 SHA256，保存资产数为0。未编辑引擎源码或 GASP58 资产；UBT 执行目标 metadata 写入，不能称引擎目录没有任何写入。UE 日志保留已有工具/条件/tag 等警告，不称 UE 零 warning。

两次全部字段、帧、输入/输出和工作值一致。仅 `FCachedRigElement.ContainerVersion` 按实际地址哈希语义进行每条轨迹的双向身份映射，保留 invalid token；没有容差比较其他数值。第二次不重写第一次捕获。

六条轨迹为 OriginalMain / OperatorBool ×30/60/120Hz，共2520帧、2154输出、2001次求解、683343条可观测指令访问、16008次独立扫掠；双脚命中2313/2303。所有求解使用 ALS 原生目标腿长42.57203674316406 /40.19668960571289cm。首帧及三秒时的六组初始化、部分 alpha、关闭、隐藏和仅更新序列来自原请求，未修改触发时间。

## Godot 验证

实际 Godot 4.7.2 Mono；Debug 与 ExportRelease/Optimize 构建均0警告/0错误。

| 门禁 | 每个构建的结果 |
| --- | --- |
| ALS 目标连续对照 | 2520帧，2154完整姿态，174474骨输出 |
| 部分 alpha / 关闭 | 156 /153输出 |
| 取消后重试 | 2520帧 |
| 工作/变量/层级/输出比较 | 14449584项；43080曲线/属性通道检查 |
| 最大 vector / quaternion 分量差 | 2.842170943040401e-14cm /2.220446049250313e-16 |
| 原 authored 完整输出回归 | 2520帧，2154姿态，10279440比较，原精度不变 |
| 两参考配置完整 Construction 回归 | 98元素、4200组TRS，全0差 |
| ALS Main 实际 Jolt 碰撞 | 三Provider×三Hz，共2520帧、2484姿态、18初始化 |
| 真实命中 /未命中 | 30744 /2976 |
| 物理候选取消重试 /晚期失效恢复 | 2520 /21 |
| 非法调用拒绝 | 2716 |
| 平面差 /法线差 | 3.762543201446533e-5cm /6.66742780985885e-8 |

原生输出门禁在 Godot 算出请求位置、半径、通道并检查一致后，才注入 UE 记录的碰撞命中；不注入数学、骨骼或 Euler 答案。完整81骨 pose、曲线 presence/flags、typed属性与 RootMotion 输出保留原门槛。求解后工作比较采用原 observer 顺序；新增输出前检查为纯寄存器读取，不解析层级缓存。

真实物理回归执行修正后的 Main 宿主，包含地面升降/斜坡、组件非均匀缩放、自体排除、Box 角点法线顺序、球边缘、三角面和初始重叠。其 scope 仍不包含 Chaos/Jolt 通用逐值物理等价。Optimize 使用实际三个发布 DLL，结束后六个 Debug DLL/PDB 恢复并逐文件哈希复核。

## 证据与复核

新不可变捕获 `assets/generated/lyra_als/rig_target_v1_native.json` 为517027586字节。派生 solver100897637字节，SHA256 `17e0561a6975eece54b179212d77271dc3342c4fb1dc87c918403c8141ad8bc5`；完整输出32495143字节，SHA256 `961ee780e5e1d5b652d24885829922d6f6c4d7c21157a9524832d20f2f082f1c`。派生文件依赖原 native 的字节哈希、runtime program、原请求、参考 policy 与映射描述，不统一格式化。

日志位于 `artifacts/lyra-analysis/`：

- `rig-target-plugin-build.log`、`rig-target-ue-first.log`、`rig-target-ue-repeat.log`。
- `rig-target-debug-build-final.log`、`rig-target-optimize-build-final.log`。
- `rig-target-godot-debug-final.log`、`rig-target-godot-optimize.log`。
- `rig-target-scene-debug-final.{log,json}`、`rig-target-scene-optimize.json`。
- `lyra-rig-target-verification.json` 包含 native SHA256、各配置结果、物理 scope 与产物恢复状态。

首次骨盆失败、纯工作值失败、仅补 Reset 的失败及独立层级诊断保留在 `rig-target-godot-debug-{first,work-first,lifecycle-first}.log`、`rig-target-godot-hierarchy-diagnostic.log` 与 `rig-target-first-hierarchy-diagnostic.json`；正确保存/恢复后的首次通过为 `rig-target-godot-debug-preserve-first.log`。一次诊断构建项目路径填错，MSBuild退出1，日志同样保留；随后使用根目录实际 GodotALS.csproj 构建。诊断 observer 已从最终代码移除。

复核入口为 `tools/verify_lyra_rig_target.py`；发布构建运行/恢复脚本为 `scripts/verify-lyra-rig-target-optimize.ps1`。Python AST、PowerShell解析及 `git diff --check` 通过。

## 与 Interface / Layer 方案的关系及剩余范围

继续使用原 ALS skin68 /raw69 /logical81。Main73 目标参考配置现已具备连续原生输出证据；ALS 模型无需重新蒙皮。Manny 缺失脊柱/扭转骨仍保留原 Rig 内部参考，尚需完整主图及握持/坡地画面评估。

Animation Layer Interface 继续映射不可变 typed 签名/调用合同，14入口在同角色 `ItemAnimLayers` 组共享一个执行实例。实例保存自己的机器、播放器、缓存和反馈，参与角色统一 Source Sync 与候选提交；Layer Group、Sync Group、BlendMask 各按自身职责处理。完整方案见 [ALS 与 Layer 实施方案](2026-10-02-lyra-als-layer-design.md)。本批没有改变 interface/换装合同，也没有关闭任意UE接口、多group、self-layer、Unlink和部分覆盖语义。

关闭范围仅为**显式 ALS 参考配置的此连续最终 Rig 原生门禁与修正后真实 Main 物理回归**。输入 pose 为受控边界，不是完整Main所有图的联合 UE oracle；记录UE命中对照也不等于跨引擎真实物理一致。普通 Demo、生产模型最终输出、装备换类/多角色、统一 Notify 与 RootMotion 碰撞消费、完整Main原生轨迹、人工视觉与性能继续按 ROADMAP 推进。生产应显式选择 ALS reference，默认 authored 行为仍保留。
