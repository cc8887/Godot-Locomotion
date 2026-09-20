# MovementState 全局门控与普通子图未访问事务

第一百三十九批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 本批完成

第 138 批已拆分全局与图准备，本批补齐整个普通子图未被外层访问时的
事务入口，并将生产全局属性门控从 Floor 改为明确的 MovementState。

新增 `AlsAnimationUpdateGraphCompiler`，加载正式 `v4_layering_inputs.json`
时核对 UpdateCharacterInfo → UpdateAimingValues → UpdateLayerValues →
UpdateFootIK → MovementState Switch 的顺序、self 所有权和连接；核对
Grounded 的 ShouldMove 设置、InAir/Ragdoll 的调用分支，以及 None/
Mantling 无连接的输出。复用正式图解析器，不依据显示名称或新增延迟
常数猜测状态规则。未重新导出资产、启动 UE 或修改 UE 插件。

地面属性、地面控制和空中输入模型支持明确的动画状态。旧调用省略该
参数时保留原地面/空中 Floor 推断；生产 BaseLayer 全局入口始终传入
明确状态。Grounded 才赋值 ShouldMove 和执行地面 DoOnce/方向/Idle
控制，InAir 才更新空中属性。Ragdoll/Mantling 保留地面属性、Lean、
FallSpeed、LandPrediction 和 DoOnce 历史，但 UpdateCharacterInfo 的
Speed、全局 Aim、Jump 事件/Delay 及 Montage 候选仍按原顺序更新。
Recovering 尚无明确的原生 MovementState 映射，继续拒绝而不擅自映射。

`AlsBaseLayerFrameRuntime.PrepareUnvisitedGraph` 可在全局准备后调用：

- 不调用普通图的 Slot/惯性化准备，不更新普通状态机或采样姿势。
- 经现有空来源批次结束上一帧的同步参与和来源通知状态；共享贡献者
  仍可加入其他分支的来源，不能将整个动画实例视为暂停。
- 将本帧普通 Slot 相关性作为零交给既有 Montage 通知队列完成器。
- 允许全局属性、Montage、通知及共享来源状态统一提交；普通 Slot/
  惯性化历史保持最后一次实际访问的状态。
- 拒绝重复完成、对未访问分支求值/读取姿势。候选支持取消、同帧重试。

BaseLayer 的全局提交身份与普通姿势子图最后访问身份现已分离。普通
帧仍一起推进；未访问帧只更新全局提交身份。最终外层所有者仍需先验证
全部分支，才能提交本组件；这个入口不代表未完成的最终帧可以提前发布。

## 验证

优化 Debug 构建 0 错误、0 警告。Import 专项 46/46，结果文件
`artifacts/test-results/unvisited-global-state-import.trx`。包含正式图、
6 类来源变更拒绝、12 组动画状态/接地标志组合，以及既有地面输入和
DoOnce/方向控制回归。明确测试 None、Grounded、InAir、Ragdoll、
Mantling、未处理枚举值 255 与 Floor 0/1 的组合；Grounded/InAir 的
明确状态也不由相反的 Floor 标志覆盖。

真实资源专项通过既有 `base_layer_frame_smoke.tscn -- --unvisited-root`
运行，30/60/120 Hz 共 1,050 帧：651 帧未访问、6 次恢复访问，609 帧
Ragdoll/Mantling 属性保持，隐藏时 255 帧存在 Montage 遍历，651 帧
Aim 有变化。每帧完整准备后取消重试，比较属性、姿势、来源时钟/epoch、
通知、Montage 身份与时间；逐帧核对未访问时普通姿势身份、惯性化历史
计数和普通状态机保持。日志 `artifacts/unvisited-root.log`。

该专项故意交替输入 Floor 0/1，让物理接触与动画状态独立。真实资源
用于普通图和 Roll Montage，隐藏期间的最终曲线由受控外层输入提供，
不是实际布娃娃骨骼/快照。恢复访问测试证明现有图能继续且重试一致，
不证明完整 UE 初始化/CacheBones 重入语义已对齐。

既有两阶段/组合入口回归 3,360 帧通过，含 302 个 Slot 遮蔽来源帧和
12 次晚期失败，日志 `artifacts/unvisited-global-regression.log`。
Slot 遮蔽来源与本批整个普通子图未访问是不同路径，均分别验证。

原生脚部生产单线程和并行各 960 帧通过：
`artifacts/unvisited-production-single.log`、
`artifacts/unvisited-production-parallel.log`。两模式保持上一批
result=`B289A6FB5130DBB7`、fullPose=`7B82A91E8A09C723`、
sampledPose=`6804D603D2523040`、root=`DB5B813964D3479C`，224/258，
37 个事件，lag/stale=0；旧身份归一化摘要仍为 `D898A6B5BD5DE295`。

补充回归均正常退出：原受控路径 3,360 帧（`unvisited-controlled.log`），
真实 Roll/Slot 动作 1,050 帧及每帧重试、28 次晚期失败（`unvisited-actions.log`），
实际生产晚期姿势/来源事件失败均回滚，回调泄漏 0
（`unvisited-late-transaction.log`、`unvisited-late-events.log`）。
这些日志均位于 `artifacts/`。Godot 编辑器无界面扫描正常退出，无
ERROR/FAIL，已生成 `BaseLayerUnvisitedSmokeChecks.cs.uid`，见
`artifacts/unvisited-editor-import.log`；本批文件空白检查通过。

## 保持未完成

普通子图未访问事务已具备，但真实生产根仍只启用普通分支。接下来需
传播原生初始化/相关性/CacheBones 到普通分支，并接完整分层、手脚和
Ragdoll 的根双分支调度。真实物理观测/命名快照、动画与物理所有权和
恢复仍在 P6 范围；完整 P6 玩法不是当前 P3/P4 视觉问题修复的前置。

默认 Demo 仍为 BaseLayer；平台、同输入 UE 上身/换髋/支撑脚多帧与
人工验收未完成。本批不能关闭滑步、交错步或上身效果问题。原 P5A
剩余项至 P7、既有 Core 23 项失败、Import 分配不稳定和旧 p95=2.559ms
超过 2.5ms 保持；音频暂缓。没有 commit、revert 或 merge。
