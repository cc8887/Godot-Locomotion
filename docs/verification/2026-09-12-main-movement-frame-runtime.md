# 完整主移动来源、缓存与原始姿势事务

日期：2026-09-12。完整性修复第八十批。

## 本批实现

新增 `AlsMainMovementFrameRuntime`，将前批 Grounded 帧组件、Air 收集器、Landing
收集器与正式 Main Movement 状态机接在一起。此处的“完整主移动”指 Grounded、
Fall、Jump、Land、Land Movement 五个姿势状态及三个 conduit，不指完整 AnimBP
或 Demo 已经完成。

- 外层状态使用正式编译的 17 条优先转换；Land 剩余时间和 Land Movement
  自动转换时间读取上一份实际共享来源历史，不在测试中指定动画结束帧。
- 清除目的状态缓存权重后，按状态机给出的实际初始化顺序执行，未用位掩码
  替代顺序。Grounded 初始化入口；空中保留独立预测/Lean 与嵌套 Jump；移动
  落地先初始化 Grounded 缓存，再初始化落地来源。
- 外层状态按实际 Update 顺序收集普通来源。每个状态贡献立即追加到共同缓冲，
  保留状态间顺序、样本范围、父状态上下文和惯性化同步标志。
- Grounded 与 Land Movement 的缓存读取先登记，等外层状态访问结束后统一
  更新地面六缓存。地面来源追加到相同批次，整个角色只调用一次共享 Sync。
- 外层与地面、空中、落地组件之间显式交换候选来源；不会拿各自 Begin 时的
  旧副本覆盖其他分支的新初始化或缓存权重。
- 来源通知由同一次同步结果准备。外层状态通知和嵌套 Jump/地面更新记录保留
  给上层消费；未在 Update/Evaluate 内分发 gameplay。
- 原始主状态姿势已组合：状态过渡栈、QuickFeet 逐骨骼权重和普通曲线权重
  分别使用已有原生语义组件。曲线按名称把地面局部布局映射到主移动布局。
- Land Movement 实际读取 Grounded 缓存再叠加移动落地动画，替换参考姿势
  测试依赖。与 Grounded 同帧读取时共享同一个缓存求值结果。
- Commit/Discard 共同管理外层状态、空中/落地输入、来源同步、来源事件状态
  和地面内部缓存；发生晚期姿势失败后可取消并从同一已提交帧重试。

`EvaluateRaw` 明确输出惯性化之前的原始姿势。Main Movement、嵌套 Jump 和
地面的惯性化请求已转交外层候选 sink；还没有实现本层之外的 BaseLayer Slot/
Montage 与最终惯性化节点，也没有接入 Worker/Demo。调用者必须先完成后续
姿势/事件阶段，再提交此组件。

地面子图内部仍有把缺失曲线变成存在零值的旧分支；外层名称映射和 presence
混合不能替代修复这些局部消费者，因此完整曲线语义尚未关闭。

## 验证

新增 `scenes/tests/main_movement_runtime_smoke.tscn`，使用完整 75/109 绑定与真实
动画资源。测试提供角色运动/输入轨迹，状态转换、来源初始化、播放时间和
落地退出由正式组件计算。

30/60/120 Hz，各运行两组起跳脚/速率与落地速度；其中三组从空中直接开始，
另外三组先地面移动再起跳。每帧先求值并取消，再重试并提交。

| 检查 | 最终结果 |
| --- | --- |
| 主移动回放 | 3360 帧，五个姿势状态均覆盖（状态位图 79） |
| 来源事件 | 53 个，重试载荷、身份和顺序一致 |
| 外层状态事件 | 18 个，全部解析到正式 notify 名称 |
| 惯性化请求 | 51 个，重试时持续时间、权重、父状态、活动标志和 requester 一致 |
| Grounded/Land Movement 缓存别名 | 119 帧，共享求值，不重复执行六个生产者 |
| 实际移动落地姿势 | 210 帧，与真实 Grounded 基础组合一致，且不同于参考姿势基础 |
| 动画实际时间驱动的落地退出 | 12 次，包含 Land 与 Land Movement |
| 完全未访问地面的空中帧 | 1332 帧，地面时间与 epoch 冻结 |
| 惯性过渡清理窗口中的旧地面访问 | 6 帧，保留状态机原权重并标为非活动上下文 |
| 从空中直接启动 | 3 组，Grounded Entry 已初始化，但隐藏的地面资产 epoch 仍为零 |
| 晚期 Slot 姿势故障 | 6 次，来源不泄漏，取消后重试通过 |
| 求姿势前提交 | 6 次拒绝 |

每帧检查骨骼有限且旋转归一化；重试逐项比较骨骼、曲线、同步历史、来源时间/
epoch/权重、通知、事件与惯性化请求。Slot 使用显式合成姿势夹具，惯性化请求
只验证转交与保留，不冒称其已被最终节点执行。

最终日志：`artifacts/main-movement-runtime-cold.log`。此前全部从地面启动的
回放也已完成，`artifacts/main-movement-runtime-rechecked.log` 为 3360 帧、58
来源事件；最终来源事件数减少是轨迹增加空中启动后的结果，不是通知阈值调整。

前批独立地面组件回归通过，日志 `artifacts/main-movement-grounded-regression.log`：
1680 帧对照、取消重试、可变步幅/斜向修正、停用/重入与局部热路径零分配保持。
本批没有测量完整主移动组件的分配或十分钟性能，不能沿用局部结论。

## 首错：已访问的旧分支不能当成隐藏分支

首次测试把“当前状态为空中”直接等同于“地面未被访问”，在 30 Hz 第 120 帧
失败：Fall 已成为当前状态，但仍有一个 Grounded 缓存请求，player 8 因重入而
增加 epoch。第二次测试又错误假定该请求一定为零权重。

核对本地 UE 源码 `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp`：

- `FAnimationActiveTransitionEntry::Update`（约 120 行）在 CrossfadeDuration 为零
  时保持 QueryAlpha 为零。
- 状态机 Update（约 520 行）先更新所有过渡 alpha，再更新仍未结束的旧过渡
  所引用的状态，最后才移除最新已完成过渡及之前的过渡。
- `GetStateWeight`（约 1408 行）按当时的完整过渡栈求权重。
- `UpdateTransitionStates`（约 892 行）把非当前状态的上下文标为 inactive。

因此这些旧分支可以在这一帧保留非零更新权重，尽管最终原始姿势已选择空中。
实现保留了既有状态机结果；测试现在检查有无真实缓存请求。有请求时必须与
对应原生状态 Update 权重完全相同且为非活动上下文；无请求时仍严格要求所有
地面时间和 epoch 冻结。没有强行清除旧分支、停止其时钟或放宽浮点容差。

失败日志保留：`main-movement-runtime.log`、`main-movement-runtime-diagnostic.log`、
`main-movement-runtime-final.log`。本批没有构建失败；一个空补丁调用被拒绝，
未修改文件，随后使用正确路径和有效补丁完成编辑。

## 执行与剩余范围

Godot 优化构建零警告、零错误。运行前设置 `DOTNET_TieredCompilation=0` 和
`COMPlus_TieredCompilation=0`，成功执行
`dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 后，用 Godot 控制台
程序运行 `--headless --path . scenes/tests/main_movement_runtime_smoke.tscn`。

只新增 Godot 组件/场景与文档，没有修改 Core/Import 算法或正式 UE 数据，没有
重跑其完整测试套件，也没有启动 UE、执行新的完整图原生探针或人工截图。
没有 commit、revert、合并或改变键鼠输入。

下一项转向实际 BaseLayer 后续节点：严格核对 Slot/Montage、最终惯性化配置
和求值历史，把原始 Main Movement 输出接成最终候选姿势，再接 Worker/Demo。
随后闭合最终曲线反馈、YawOffset 到角色朝向、动态上身和完整脚部约束。
起步滑步、换髋、上身、平台与最终性能门禁仍未验收。原 P5A–P7、全部 Overlay/
道具、Mantle/Roll、物理恢复、完整 Camera 的范围保持，音频暂缓。
