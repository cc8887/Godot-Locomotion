# 方向缓存更新与过渡清理的顺序

第一百五十批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。
上一批已完成 Sprint 分支接入，本批继续处理已确认的横移来源权重差。

## 原因与正式修改

本地 UE 5.9 `Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp`
的 `Update_AnyThread` 先更新全部过渡 alpha，再对尚未完成的过渡逐一调用
`UpdateTransitionStates`，随后才移除已完成过渡及更老的过渡。清理后仅在
当前状态尚未被更新时补一次全权重更新；`StatesUpdated` 防止重复。

Godot 的 `AlsStandingDirectionInputs` 已按清理前的访问集读取输入，但
`AlsCycleCacheWeights.Resolve` 仍使用清理后的过渡栈决定来源访问和权重。
因此曲线/姿势使用的状态与 Update 上下文被混为同一份状态。尤其原自定义
曲线达到端点而 alpha 尚非 1 时，清理后的单状态权重不能替换之前已访问
状态的更新权重。

新增 Resolve 重载，显式接收 beforeCleanup 和 evaluated：

- 未完成过渡按原顺序 From/To 更新，权重取清理前的完整栈。
- 每个状态只访问一次；清理后的单状态只在尚未访问时补全权重更新。
- 局部 pin 相关但全局权重为零仍保留访问；缓存继续选择最大权重，
  相同权重保留首次上下文。姿势求值仍使用清理后的栈。
- `AlsStandingCycleGraph` 正式接入两份状态，不修改时间、曲线或容差。

## 对照结果

重新捕获 30/60/120 Hz 的横移和 Sprint，全部 3780 帧先求值、丢弃、
再同帧重试通过。两份请求 SHA256 分别与上一批完全一致，因此复用原
`full-graph-ue-148-tail.json` 与 `full-graph-ue-149-sprint.json`，未重新
启动 UE，也没有原生源码/插件修改。

| 实际 Godot tick 子集 | 次数 | 时间最大差 | 缓存权重最大差 | 失败数 |
| --- | ---: | ---: | ---: | ---: |
| 横移 | 4252 | 0 | 2.980232238769531e-7 | 0 |
| Sprint | 9016 | 0 | 2.384185791015625e-7 | 0 |

横移原两个 30 Hz 轨迹第 61 帧的 4 次失败已全部消失；来源比较器
沿用 1e-6 门槛，均退出 0。证据 `artifacts/full-graph-sources-150-strafe.json`
和 `full-graph-sources-150-sprint.json`。这是实际 tick 子集的时间/权重
验收，不代表全部原生访问集合、采样、通知消费者和完整姿势均已等价。

严格姿势比较结果不变，仍退出 1：横移 1260 帧中 30 帧失败，最大
.028576905190238255 cm / .0586825146944919°；Sprint 2520 帧中
62 帧失败，最大 .04214590330499961 cm / .06842977767622564°。
所有曲线存在性与数值通过。证据 `full-graph-parity-150-strafe.json`
和 `full-graph-parity-150-sprint.json`。不能把来源问题关闭称为视觉完全修复。

## 回归

优化 Debug 构建 0 警告、0 错误。22 项专项通过，包含清理前零权重
旧状态仍被访问、重入当前状态保留首次更新权重、缓存选择、输入历史及
分配检查。TRX `artifacts/test-results/cycle-update-cleanup-150-final.trx`。

生产 single/parallel 各 960 帧通过，沿用上一批摘要无需改预期：
result=`A60FF3CFBD847474`，fullPose=`389A376BB89FBC43`，
sampledPose=`B1C0577B44AB4DDF`，root=`DB5B813964D3479C`。
各 39 事件、316 锁脚帧、910 偏移帧，lag/stale=0。日志
`cycle-update-production-single-150.log`、`cycle-update-production-parallel-150.log`。
每帧重试日志 `full-graph-godot-150-strafe.log`、`full-graph-godot-150-sprint.log`。
本批未重跑实际渲染，不能把第 149 批截图称为新截图。

## 后续边界

横移已知来源权重差关闭；下一项定位仍集中于起步的姿势分歧，核对正式
BaseLayer 混合/惯性化前后的姿势与精度边界，必须用分段 UE 对照确认原因，
不能先假定全部来自浮点误差。随后完成实际 Motor/反向时序、平台支撑和
人工验收，再切换完整默认入口。默认目前仍为 BaseLayer。

P5A 通用事件/动作剩余项、P5B Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 Ragdoll/Get-up/恢复/完整 Camera、P7 十分钟性能预算
仍未完成；既有 Core 23 项失败、Import 分配不稳定及旧 p95 2.559 ms
超 2.5 ms 未关闭，音频暂缓。本批未 commit、revert 或合并。
