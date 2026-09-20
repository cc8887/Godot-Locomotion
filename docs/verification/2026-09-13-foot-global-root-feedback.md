# Foot IK 全局更新与最终根反馈

第一百四十四批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 实现与来源

AlsFootIkFrameRuntime 分开实例全局属性、姿势分支访问和最终输出反馈。
PrepareGlobal 消费已提交的最终曲线及对应骨骼/场景观测，按正式
UpdateFootIK 函数更新 Left/Right Lock、Offset 和 Pelvis；不准备或求值
骨骼控制。PrepareGraph 决定普通分支是否访问控制节点；隐藏时不更新
控制输入、不求值，也不允许读取上一帧姿势冒充当前输出。

CompleteFinalOutput 要求当前根身份和已经完成的分支阶段，保存最终根
曲线；普通脚部 Evaluate 不再自动形成下一帧反馈。全局候选只有在最终
反馈完成后才能提交。非法曲线取消候选，错误阶段、外来身份和重复反馈
均被拒绝。隐藏期间全局属性与最终反馈可以提交，CommittedPoseIdentity
继续指向最后实际求值的脚部控制帧，不能与全局身份混为一谈。

原有 Prepare 是正常访问的组合调用，仍必须显式完成最终根反馈。所有
生产调用已改为拆分路径：脚部全局更新发生于根选择前，普通图访问时
准备控制，最终根求值后才保存反馈，所有银行校验后共同提交。最终根
现在仍只启用普通生产分支，尚未连接 Ragdoll 混合和物理恢复。

保持正式 V4 属性函数语义：Ragdoll 仍执行脚锁，但保持偏移/骨盆属性；
Grounded/None/Mantling 执行偏移和骨盆函数；InAir 执行原版复位分支。
脚锁使用实际 movement velocity 和 world delta，姿势观测仍要求来自
上个已提交最终帧。隐藏访问不会把全局属性重置成初值。固定骨架与
直接 Float/Curve alpha 不需要隐藏期间伪造控制更新；动态骨架/LOD
仍未作为支持范围完成验收。

同时修正 AlsRootPoseRuntime 的生命周期判定：Initialization 和 Bones
应比较实际计数，不以同一计数的 GlobalFrame 变化触发重建。新增用例
验证正在进行的根混合不会因此跳到目标，取消重试也保持混合历史。

本机只读核对：AlsAnimationUpdateGraphCompiler 验证的原始 UpdateGraph
将 UpdateFootIK 放在实例级 MovementState 分发之前，与姿势相关性无关；
AlsFootIkInputCompiler/AlsFootIkCompiler 验证正式属性函数与九控制链；
UE 5.9 AnimNode_SkeletalControlBase::Update_AnyThread 在节点访问时
读取属性或实例历史曲线并计算 alpha。沿用已核对的遍历计数语义。
本批没有启动 UE、重新导出资源或添加新的 UE 最终姿势 oracle。

## 验证

优化 Debug 构建通过，0 错误、0 警告。

- Import Foot IK 专项 60 项全部通过，含 30/60/120 Hz 的全局隐藏、
  Ragdoll/Mantling/InAir 属性门控、最终曲线与普通分支曲线不同、错误
  阶段/身份、非法反馈取消重试，以及既有零分配热循环。
  `artifacts/test-results/foot-global-import.trx`。
- Core 根节点专项 15 项全部通过，含同计数不同 GlobalFrame 的活跃混合
  保持和取消重试。`artifacts/test-results/foot-global-root.trx`。
- `layered_frame_input_smoke.tscn -- --unvisited-feet` 共 630 帧：210
  隐藏、264 混合、420 有效控制、11 最终反馈故障、474 次最终曲线历史
  检查和 3 次同计数 stamp 检查，每帧取消重试。
  `artifacts/foot-global-root-composition.log`。

根/脚部组合使用正式根混合配置、正式脚部模型与 79 骨骼布局。两条输入
姿势/曲线及物理观测受控，最终姿势重新组合出下一帧脚部观测；没有绑定
真实物理 Ragdoll，不能把这一专项记作根混合已进入生产。

真实原生脚部生产单线程/并行各 960 帧通过：
`artifacts/foot-global-production-single.log`、
`artifacts/foot-global-production-parallel.log`。共同摘要保持：
result=B289A6FB5130DBB7、fullPose=7B82A91E8A09C723、
sampledPose=6804D603D2523040、root=DB5B813964D3479C；224/258，
37 来源事件，lag/stale=0，316 脚锁帧、910 偏移帧，旧脚部写入为零。

真实分层输入 1,260 帧和每帧重试、10 次失败通过：
`artifacts/foot-global-layered-input.log`。
晚期姿势/来源事件失败回滚通过，来源回调泄漏零：
`artifacts/foot-global-late-transaction.log`、
`artifacts/foot-global-late-events.log`。本批修改文件空白检查通过。

## 尚未完成

下一项是手部未访问提交和最终根对子图的访问调度，将 BaseLayer、
Overlay、Post Layering、Aim、手脚与 Ragdoll 来源的候选统一衔接。
组件隐藏 API 和上述受控组合不能代替真实根双分支生产入口。
当前脚部物理采集仍沿用已有场景路径；全状态、移动平台和实际根姿势
观测的绑定随整链继续验证，不以本批受控命中输入作已完成证明。

随后进行 P3/P4 起步、左右换髋、上身、平台与支撑脚的 UE 同输入多帧
和人工对照，再切换默认入口；当前默认仍是 BaseLayer。本批没有关闭
原始视觉问题，也没有将完整 P6 物理玩法设为基础视觉修复前置。

P5A 其余通用事件/动作、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 物理恢复/完整 Camera、P7 十分钟性能验收仍在目标内。
既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms 超过 2.5ms
未在本批关闭，未运行全套测试或十分钟预算，音频暂缓。
未 commit、revert 或 merge，保留已有工作区改动。
