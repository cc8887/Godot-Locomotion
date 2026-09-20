# 完整脚部分发的取消恢复与提交等待（第一百八十二批）

## 本批结论与原规划归属

本批继续 P3/P4 完整动画链的生产接线：修复被取消的动画帧恢复时跳帧，
以及 Worker 已完成、主线程尚未提交时重复求值的问题。属于原完整性路线
明确要求的多帧率、多角色和取消恢复工作，没有另起玩法范围。

12 组实际 Godot 角色测试通过，共 27846 个角色提交帧；相同频率/角色数的
single 与 parallel 姿势、根变换、结果和覆盖计数完全一致。该结果只证明
本批分发与恢复合同，不证明完整 UE 角色视觉一致或十分钟性能达标。

## 已复现的问题与修复

1. 查询前/查询后停用角色：Motor 已发布第 N 帧，动画候选被取消，恢复时
   Motor 又推进到 N+1，脚部历史仍等待 N。原 60 Hz 单角色在发布 62、
   提交 60 时出现 `Refactored foot scene/final history identity differs`。
   证据为 `artifacts/foot-dispatch-182-before-history.log`。
   `AlsP3Character` 现在保留已经积分的输入，直到该帧提交；恢复重新准备
   同一输入并分配新查询序号。已有冻结故障后的 Motor 恢复策略保留。
2. 主线程 Commit 暂停：Worker 已提交内部动画历史并发布结果，下一物理
   tick 又准备/求值同一身份，造成分发失败。原复现为世界 tick 242、
   发布 235、主提交 234，见 `foot-dispatch-182-before-commit-hold.log`。
   Prepare/Visual 两处现在检查已发布的完整身份，等待主线程消费；检查
   包含角色和代次，不能只比帧号而错误接受替换前的旧结果。

生产变更为 `AlsP3Character.cs`、`AlsP3SplitFootFrame.cs` 和
`AlsP3WorkerRoot.cs`。新入口仍需显式启用 `--refactored-foot-frame` 及其
完整分层、脚部与 Refactored 曲线依赖，默认 Demo 入口没有切换。

## 实际角色测试

新增 `RefactoredFootDispatchSmoke.cs` 及场景
`res://scenes/tests/refactored_foot_dispatch_smoke.tscn`，直接配置真实
Character/Motor、共享不可变来源定义和独立角色交换槽。没有伪造输入或
射线响应，也没有增加生产代码专用的测试绕行入口。

每次覆盖前进、左右横移、跳跃/落地、蹲伏、停止，并在真实阶段节点上执行：

- 查询前取消：停用查询与 Visual，保留已准备候选，再停用/恢复角色。
- 查询后取消：仅停用 Visual，确认实际查询已完成，再停用/恢复角色。
- Worker 完成后暂停 Commit：连续等待，确认 Motor、动画历史、查询次数
  和通知不再推进；恢复后主线程恰好消费原输入。

停用期间检查实际 Skeleton 骨姿势、锁脚/Rig 历史、提交帧号和事件数量；
恢复检查完整输入相等、查询序号更新、逐帧身份及最终曲线生产者一致。
所有角色提交帧连续，parallel 均实际使用非主线程，线程亲和与回调失败为零。

最终日志前缀为 `artifacts/foot-dispatch-182-final-`：

| Hz | 角色数 | 每模式提交帧 | 空中帧 | 蹲伏帧 | 锁脚帧 | 通知 | 射线 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 30 | 1 | 180 | 26 | 30 | 45 | 15 | 324 |
| 30 | 10 | 1821 | 260 | 300 | 471 | 150 | 3264 |
| 60 | 1 | 360 | 52 | 60 | 94 | 15 | 652 |
| 60 | 10 | 3621 | 520 | 600 | 961 | 150 | 6544 |
| 120 | 1 | 720 | 103 | 120 | 188 | 15 | 1308 |
| 120 | 10 | 7221 | 1030 | 1200 | 1901 | 150 | 13104 |

每模式有两次取消和一次 Commit 等待。多角色测试只暂停指定角色，其他角色
继续推进，因此总提交帧高于 `6 * Hz * 角色数`。摘要与全部上述计数均做
模式间精确比较，不要求不同物理频率摘要相同。

复查命令：

```powershell
node tools/diagnostics/check_refactored_dispatch_matrix.mjs artifacts/foot-dispatch-182-final-
```

检查器要求全部 12 个日志各有一次成功标记、没有运行错误、覆盖完整，并
核对六对姿势/根/结果摘要。输出 `REFACTORED_DISPATCH_MATRIX_OK`。

早期 `before.log` 的故障来自测试只停查询而未停 Visual；
`before-resume.log` 用了停用时会清除的发布诊断摘要。这两项是测试装置
问题，修正为匹配阶段边界和读取实际 Skeleton 后才定位上述生产问题。
早期 `fixed-*` 日志未覆盖第三种等待，不用作最终 12 组结果。

## 既有生产回归

`artifacts/foot-dispatch-182-regression-{single,parallel}.log`：新入口各
960 帧通过，包括代次替换、旧结果拒绝、可见性与恢复。结果
`B6345BBBADCB7487`、完整姿势 `66E084F8FF0938FE`、根
`3C8B520C47ECA5C7` 与第 179 批保持一致。各 321 锁脚帧、854 环境偏移帧，
活跃替换代次 1612 次射线。完整身份等待没有误认旧代次结果。

`foot-dispatch-182-regression-late-source.log`：第 25 帧真实来源通知后的
晚期故障回滚通过，Rig/锁脚/最终姿势/运行时恢复，回调泄漏零、待处理查询零。

`foot-dispatch-182-regression-old.log`：旧完整脚部入口 parallel 960 帧通过，
结果 `DB9FEFC95ADA4B15`、完整姿势 `765E1669B4501131` 与原基准一致。
本批所有 12 组矩阵和四个生产回归进程均退出 0。`git diff --check` 通过。

## 仍未完成的验收与下一项

本批没有修改 UE 插件、原始动画数据或键鼠输入，没有进行新的 UE 整角色
配对、渲染截图或用户人工验收。优化 Debug 构建零警告/错误；Core/Import
全套不因本批仅 Godot 分发变更而重复运行，既有债务仍保留：第 178 批 Core
23 项失败、第 181 批 Import 1 项跳过、旧栈溢出未归因。

下一项是同输入下起步接触、左右换髋和上身/髋/脚的整角色配对，并验证完整
入口的实际渲染效果；通过后再切换默认入口。不能把本次修复等同于已解决
用户全部视觉问题。之后继续 P5A 通用通知/动作消费者、P5B 全部 Overlay/
道具、P5C Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera 和 P7 最终
人工及十分钟性能验收；音频仍按用户要求暂缓。
