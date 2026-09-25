# Refactored 起步／Pivot 内部姿态图

## 实现范围

新增 `AlsRefactoredMovementDetailsPoseGraph`，读取现有 catalog 绑定的原 AB_Als_Standing 完整图。六个状态的全部内部编译节点必须被消费，runtime 连线及 sourceLinkId 与编辑图双向引脚逐一交叉校验；不按动画名字推断接线。

| 状态 | StateResult | Movement 读取节点 | ApplyAdditive | MultiWayBlend | ResetPivot |
| --- | ---: | ---: | ---: | ---: | ---: |
| Walk | 108 | 107 | — | — | — |
| Run Start From Walk | 116 | 110 | 115 | 109 | — |
| Run | 106 | 105 | — | — | — |
| First Pivot | 104 | 98 | 103 | 96 | 97 |
| Second Pivot | 95 | 89 | 94 | 87 | 88 |
| Run Start | 86 | 80 | 85 | 79 | — |

总计 38 个状态内部节点：六个 StateResult、六个 UseCachedPose、四个 ApplyAdditive、四个 MultiWayBlend、十六个 SequencePlayer、两个 CallFunction。四路顺序为 Forward/Backward/Left/Right，输入来自 GroundedState.VelocityBlend，归一化政策不变。十六播放器身份及各自组、起点、速率等复用原 MovementPlayers compiler，不合并同资产的不同播放器。

四个加法层均为固定 Alpha=1，Float 输入、无 alpha 映射/夹取/插值、无 LOD 限制。对应四个真实 acceleration 资产验证为 LocalSpaceBase，实际 additive 编译均得到 79 骨布局。两个 Pivot 回调必须是原 OnBecomeRelevant 的 ResetPivot，Source 指向本状态 ApplyAdditive，调用身份保留。

全部状态共享 SaveCachedPose 67，名称 Movement，原源节点 122 为 ModifyCurve。此处保存边界和真实来源，不把它改连到方向状态机 201。原缓存源还包含 PoseMoving 曲线和 Lean 加法，尚待整合。

## 对后续求值的约束

原四个 MultiWayBlend 的 `bAdditiveNode=false`，不能擅自改成 true。但 ApplyAdditive 在求值加法支路时构造 `bExpectsAdditivePose=true` 的 FPoseContext；本地 UE `Engine/Classes/Animation/AnimNodeBase.h` 的 `ResetToRefPose` 会根据该上下文重置为 additive identity。因此全零速度通道时，未来求值器应保留这个上下文含义，不能仅按 MultiWayBlend 字段恢复绝对参考姿态。当前批次尚未实现该求值器。

本图只编译结构与来源；没有新增独立时钟、缓存调度器或 Parent 修改。初始化、状态更新顺序、source requests、ResetPivot 消费、真实 Movement cache、加法 pose/curve 求值及惯性化请求统一消费仍需后续接入。

## 验证

- `artifacts/refactored-movement-details-pose-graph/validated.trx`：新增 2 项通过，含 15 种资源变异拒绝；验证实际四个 additive 资产、十六独立身份、六共享读者和两个回调。
- `related.trx`：15 项通过（回调 4、移动播放器 5、Movement Details 连续更新 6）。
- Godot Optimize 构建成功，0 warnings / 0 errors。

前三轮失败记录保留：`initial.trx` 错误要求 authored Node.cachePoseName 与 runtime 都为 Movement，实际 authored 模板为 None、名字在编辑器外层；`graph.trx` 对 alphaScaleBiasClamp 使用了要求整个对象完全相等的比较器，改用既有字段级 Clamp 校验；`graph-final.trx` 错误要求所有 ApplyAdditive 都暴露 Alpha 引脚，改为未暴露时检查 authored/runtime 的 Node.alpha，暴露时额外检查字面值。没有修改原资源来规避失败。

没有新 UE 导出、原生整图对照、Godot 场景、全量或十分钟性能测试。普通 Demo 尚未切换完整 Refactored 链；本批不能证明起步滑步或 Pivot 画面已经修好。

## 下一步

根据这些真实节点接初始状态递归、ResetPivot 相关性、加法源更新与共享缓存读者上下文；原 Movement 缓存补齐 Lean/PoseMoving 后合入全 stance 遍历，再做实际播放器时间和姿态曲线的 UE 连续对照。其余 Standing/Crouching/Stop 状态机、真实 Parent/Notify、完整宿主及所有既有 Ragdoll/Mantle/Camera/性能缺口继续保留。

主目录仍为 `D:/GodotALS`、分支 main；用户未提交修改保留，音频、道具物理和头颈诊断继续暂缓。
