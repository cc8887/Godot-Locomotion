# 第九批：Detail 加法姿势与实际资源采样

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
范围：完整性补完 A 的 Detail 求值组件。**尚未接入可玩 Demo 状态机和惯性化链**。

## 实现

- `AlsLocalAdditivePose`：按 UE 局部加法求差，旋转为 Target * Inverse(Reference)，
  位置直接相减，缩放为 TargetScale * SafeReciprocal(ReferenceScale) - 1。
  求差后归一化旋转；叠加时加法旋转左乘 Cycle，缩放乘以 1 + DeltaScale * Alpha。
  保留原生相关/满权重门限及非满权重的 Identity 短弧混合，不使用 Slerp 替代。
- `AlsDetailPoseComposer`：每个源先求差，按 F/B/L/R 固定顺序 MultiWayBlend，再
  ApplyAdditive。权重先按总和归一化，再剔除 <= 0.00001 的贡献，不二次归一化。
  原图的 bAdditiveNode=false，因此零贡献时 MultiWayBlend 返回参考姿势而非加法
  Identity；本组件保留这一源行为。此分支不代表最终状态机应该让它在静止时生效。
- `AlsDetailPoseSampler`：复用局部轨道采样，读取真实 FBX 导入资源和第 0 帧 Run
  BasePose。外部提供当前 Detail 状态和四个源时间；Walking/Running 原样返回 Cycles。
  不推进时钟、不处理 Sync/Notify，不写 Skeleton，不创建新的永久调度器。
- 四个资源和基准可共享只读动画，但 16 个 SourceNode 保持独立配置和时间身份。
  每个采样器独占临时姿势缓冲，支持原地输出、重复候选求值和实例隔离。
- 曲线按同一源时间求值，并减去该曲线的基准值后加到 Cycle；保留 Cycle 独有曲线。
  此处是标量曲线求值组件，不是 P5A 全部曲线 provenance/事件身份的正式接线。
- P4 库构建增加可选 Detail profile，校验骨架和资源闭包。不传时原有闭包不变；
  本批没有修改 Demo 调用来提前启用不完整的 Detail 状态机。

## UE 依据与原生探针

本地 UE 5.9 源码：

- `FbxAnimationExport.cpp`：FBX 骨骼轨道从 DataModel.EvaluateBoneTrackTransform 输出，
  是绝对局部姿势，不是已经求好的加法差量。
- `AnimationRuntime.cpp::ConvertTransformToAdditive` 与
  `AccumulateLocalSpaceAdditivePoseInternal`。
- `TransformVectorized.h::AccumulateWithAdditiveScale`、`BlendFromIdentityAndAccumulate`。
- `AnimNode_MultiWayBlend.cpp`：权重、顺序、归一化与零贡献参考姿势分支。
- `AnimSequence.cpp::GetBonePose_Additive`、`AnimCurveTypes.h::ConvertToAdditive/Accumulate`。

为 `AlsPoseBlendCommandlet` 增加可选 `-IncludeAdditive`，不改变默认输出。
探针直接调用原生加法求差及 FTransform 叠加，288 个 alpha/变换案例覆盖反号旋转、
非均匀/负/零/近零缩放和权重边界；另有 64 个四向组合案例，包含零权重和小贡献剔除。
误差限 0.000005，四元数同旋转反号视为等价。四向组合使用原生运算组成源图顺序，
不是实际执行整张 AnimBP，也不证明 FBX/UE 原始轨道与压缩运行时逐帧一致。

按 `ue-diagnosing-plugin-build-load` 完整构建 Editor target、审计全部适用项目插件，
随后冷启动只读探针。没有复制 DLL、修改引擎源码或保存 UE 资产。
加法探针构建 fingerprint：`AE420469497C06A00611E61C09BF4089120E6A52BD28964114E9F4E49DB3EF54`。
日志：`artifacts/local-additive-native-20260910.log`，退出码 0，0 errors / 0 warnings。

```text
ALS_LOCAL_ADDITIVE_OK cases=288 detail=64 assets_saved=0
```

fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_local_additive_native.json`。
SHA256：`B99E18DC2600EC027D3609BE986D4994DD629B9038C3C44E7EA2F5660B96C09F`。
文件还保留原探针的非加法案例；本批新增测试只把 additiveCases/detailCases 作为新证据。
未执行普通 GUI 重启、项目数据验证或打包，不作为插件发布验收。

## 实测发现并修复

1. 原生 Detail 导出含非 ASCII 文本，SaveStringToFile 的自动编码生成了 UTF-16。
   .NET ReadAllText 能读取，但 Godot FileAccess 按 UTF-8 读取失败。导出端现对
   IncludeDetail 固定 ForceUTF8WithoutBOM，重新完整构建、审计并运行原生导出。
   新旧解码文本逐字符相同，状态/规则/播放配置未改变；新增严格 UTF-8 导入测试。
2. AnimationLibrary.GetAnimation 返回共享的托管包装对象。测试读取者 Dispose 会
   使采样器保留的对象失效。Detail 明确借用库资源；局部轨道 helper 新增显式所有权
   参数，原有调用的默认策略未变；测试读取者也不释放借用对象。库必须比采样器长寿。
   验证销毁另一采样器后仍可求值，重复 Dispose 不释放别人的资源。
3. Core 分配测试并发运行受到测试宿主线程分配干扰，而单独运行通过。将四个含分配
   测量的 Locomotion 测试类纳入仓库已有 Allocation 隔离集合，未放宽 0 B 门禁。

编码修复后的构建 fingerprint：`C232291423CE7BC319D6FEFA34770F091EC62B183DF9CA0B86E4D065F3AFC147`。
重新导出日志：`artifacts/detail-graph-utf8-native-20260910.log`，退出码 0，0 errors / 0 warnings。
当前 `assets/config/v4_locomotion_detail_graph.json` SHA256：
`D048605F51BCF1E332720850137002ABD8F2B8CA9375A5EAD06CDEFF4AA6FAA2`。
原始编码版本保留为 `artifacts/detail-graph-before-utf8-20260910.json`。

## 验证结果

- Core Locomotion：332/332，新增加法相关测试 13 项；含原生对照、无分配、原地输出、
  错位/源缓冲重叠拒绝、无效权重与参考姿势重建。
- Import 全量：581/581，新增严格无 BOM UTF-8 文件检查。
- Godot 构建：0 warnings / 0 errors。
- `detail_pose_smoke.tscn` 使用实际导入资源，在 30/60/120 Hz 对四个状态、16 个源
  分别提供起始偏移及不同源时间，验证叠加到基准后重建绝对动画、曲线时间一致、
  四向混合重试、原地输出、零贡献源行为、透传状态、非法时间失败前不改输出。
  累计 117504 个骨骼检查、1728 个曲线检查、12 组重复候选；动画非静态检查通过。
  每个频率下预热后 1000 次活动姿势/曲线求值为 0 B 托管分配。

```text
DETAIL_POSE_OK rates=30,60,120 occurrences=16 bone_checks=117504 curves=1728 retries=12 motion=0.108086 alloc=0B state_machine=not_connected
```

最终日志：`artifacts/detail-pose-smoke-final-20260910.log`；首轮编码失败及资源释放失败
的日志保留，没有用成功日志覆盖失败记录。该测试不证明完整状态机播放轨迹与 UE 相同。
Stop Plant 回归通过，仍为 12 固定源、4608 轨道分量、2448 骨骼、246 选择器、72 曲线。
Standing Cycle 回归通过：9 组频率/相位、9 次普通与 9 次中断回滚、63 次全骨骼检查，
空闲/活动路径均为 0 B；结果与上一批一致。两项日志分别为
`artifacts/detail-pose-stop-regression-20260910.log`、`artifacts/detail-pose-cycle-regression-20260910.log`。
Camera/Input 文件哈希不变；未重新录制移动截图或运行全量 Core/P5A/P7。

## 剩余工作

先补 UE 惯性化的历史姿势、速度估计、请求/中断与下游消费，再接 Detail 状态时钟、
相关性/重置、真实 Sync 时间，以及 ShouldMove/外层起停与 Pivot 事件。
继续统一到 P5A 事务和播放身份，随后补动态 Layering、原定 Overlay/Root Motion 等阶段。
本批仍没有改变可玩 Demo 的姿势输出，不能据此宣称起步滑步或换髋观感已修复。
