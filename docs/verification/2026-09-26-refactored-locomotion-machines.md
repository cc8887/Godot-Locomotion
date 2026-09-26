# 原 Locomotion / Jump 状态机组件

日期：2026-09-26。基线 `main / 9bde6d5`，直接在主目录实现；普通 Demo 保持该基线的新 Grounded→Transition，空中外层尚未替换。

## 实现边界

新增 `AlsRefactoredLocomotionResources`、类型化规则编译器和候选运行时，复用共享状态机引擎，增加隔离的 RefactoredLocomotion / RefactoredJump 输入域。原 V4 入口不能驱动新图；原 Grounded 等其他规则域不变。

以现有导出 `refactored_locomotion_machines.json`、有哈希绑定的原 AB_Als_Locomotion 编辑图/编译节点为来源，不重写或重新格式化资源。未修改、编译、运行 UE，也没有新的 UE 导出或连续 oracle。

- 外层 node83：六状态/十六边；嵌套 node64：五状态/五边。保留原 state/player 顺序、两个 alias 的完整展开、delegate 与编辑 property 的不同身份、每帧最多三次转换及首帧跳过混合。
- Land Conduct（原名称如此）只有 Grounded tag 才可进入；HasInput 或移动平台的相对位置标记选择 Land Movement，否则 Land。原 Fall/Jump→conduit 的 .2 秒不是实际落地混合：UE `FindValidTransition()` 使用 conduit 最终边的 .1 秒惯性策略。
- Jump 根据上一帧 FootPlanted `>0` 选 Right Foot，否则 `<=0` 选 Left Foot；零值归左，不能按速度或此次输入重选。
- 左右脚起跳自动退出的 trigger 为0，以前一更新严格最大 cached weight、先到 tie 的原源原始累计时间判断；微小正权重也有效。目标状态 reentry 时清除缓存权重，不能使用已清除的旧观察立刻再退出。
- 起跳结束后可在同一更新进入 Loop 再向 Flail 开始1秒标准混合；Loop→Flail 是无条件规则，不是速度/下落阈值。初始 conduit 本身不初始化或求值姿态。
- Land→Grounded 自动退出 trigger0、.8秒 Cubic、QuickFeet、LandToGrounded 通知0；Land Movement→Grounded 则使用 .3 秒剩余时间触发。保留非站立 stance、输入/原地旋转/650 cm/s 阈值的原优先级。
- QuickFeet 复用原79骨/18项及33组原生权重检查。四条起跳和四条落地计时源有明确身份、长度、Sync组、起点及原rate；Walk起跳从 .12f 开始，Land Movement 两源rate为1.75/1.5。
- 原 Grounded 退出停止回调、转换通知和惯性请求只生成候选；Prepare/Commit/Cancel 保证取消不改变提交状态。未来姿态宿主负责消费，不在组件阶段直接触发动作。

只读核对本机 UE `AnimNode_StateMachine.cpp` 的自动剩余时间、conduit 最终边和 SetState 清播放器权重行为。本批没有更改共享引擎的既有混合算法或放宽原生精度阈值。

## 验证与失败保留

测试结果保存在忽略目录 `artifacts/tests/refactored-locomotion-machines/`。

1. 最初测试编译引用了不存在的惯性请求 `Seconds` 字段，改为实际 `Duration`，生产代码构建未受影响。
2. `first.trx`：11项中10失败；资源对象校验漏写嵌套函数对象的 `className=None`，补齐完整绑定。另一负例通过不能证明基线可用，已补强负例必须先通过正常构造。
3. `v2.trx`：11项全通过。包括完整规则真值表、精确tag和650边界、FootPlanted零点、conduit和最终惯性、自动退出严格权重/时间/清除、通知优先级和所有权门禁。
4. `v3-real-clocks.trx`：14项中3个三频率覆盖失败，原因是受控腾空只持续1秒，短于原起跳1.6667秒，未走到Flail；没有修改生产算法或删除覆盖断言。
5. `v4-real-clocks.trx`：14项通过。测试延长为每频率16秒，30/60/120 Hz 合计3360提交帧，每帧全部候选取消后同身份重试；真实8源共享Sync时钟，保留原组/起点/rate，周期性79骨源采样，覆盖主图五个内容状态、左右起跳/Flail、两种自动落地退出、零外层权重和隐藏重入。**混合通道是受控测试输入，未声称原 Fall/Jump/Land 内部图遍历或最终姿态完成。**
6. Core 相关57项全部通过，`core-regression.trx`。最终 `regression.trx` 共59项全部通过、无跳过：新增16项（含7种重算有效哈希后的原始资源变异、规则域隔离）、Grounded组件/姿态宿主、Crouching、Movement Details及既有Standing独立/共享原生六组回归。原生门槛保持位置2e-5 cm、旋转/缩放/曲线2e-6。
7. `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 成功，0警告0错误；`git diff --check` 通过。测试命令使用 Release、`DOTNET_TieredCompilation=0`。所有首轮失败保留在上述目录，没有覆写失败记录。

## 下一交付

实现原空中/落地姿态宿主：Fall三源、预测落地两帧及Lean，Jump嵌套源和node39惯性，Land轻/重混合，Land Movement mesh additive及Grounded cache；接 Parent Initialize/Refresh/停止/落地退出处理、外node4惯性与原曲线，再统一到角色owner并切普通Demo。之后补完整新链路的原生对照和Godot运行验证。

没有进行 Godot 场景运行、多帧画面检查、人工或十分钟性能验收，也没有关闭 R2–R7。真实源Notify、脚反馈、完整上身/Overlay、Mantle和Ragdoll等原路线任务仍保留。用户暂缓的音频、道具物理、头颈问题及用户未提交修改不在本批内。
