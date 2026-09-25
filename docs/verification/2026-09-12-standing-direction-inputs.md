# Standing 独立方向输入与初始化后求值

日期：2026-09-12，第六十九批。工作区 `ARCHIVED_P5A_WORKTREE_PATH`。

## 实现与原生依据

此前六个方向节点统一读取当前 VelocityBlend，无法表达未访问状态自己的旧输入，
而无输入的求值还会落入 Forward。现新增候选值类型 `AlsStandingDirectionInputState`，
独立保存六组 DesiredAlphas/CachedAlphas 和初始化、更新标记。它随原有角色帧
一起提交，不引入第二套时钟或独立线程状态。

本机 `AnimNode_MultiWayBlend.cpp` 显示：合法四输入数组不会被 Initialize 清零，
初始化从旧 DesiredAlphas 重建 CachedAlphas；Update 才执行暴露输入并重算。
总权重不相关时所有 CachedAlphas 为零，Evaluate 输出参考姿势。源图编译器现在
校验当前资产六状态的默认 DesiredAlphas 都是四个零，变异默认值会被拒绝。

输入更新保留原状态机清理前快照。尚未完成的过渡更新 From/To，每状态一次；
目标即使当前贡献为零也被访问。较新的过渡完成并清理旧栈时，已发生的旧状态
输入更新不会丢失。重新初始化从自己的旧值重算，不读取新的全局值。

实际 `AlsStandingCycleGraph` 建立该快照；来源缓存权重解析、方向缓存读取、
骨骼混合与来源曲线混合消费对应状态的 CachedAlphas。没有有效方向输入时，
骨骼使用真实参考姿势，来源曲线为空；这条正式内部路径不再依赖 UnitX 回退。
旧独立直接组合测试入口保留原兼容签名，上游 VelocityBlend 计算与完整图其他
调用仍需核对，不能宣称项目中所有零速回退均已移除。

采样器允许已初始化、尚未 Update 的 Cycle：外部 BlendSpace 样本为空时返回
参考姿势和空曲线。完全未初始化却请求移动 Cycle 姿势会被拒绝，不再偷偷使用
旧直接采样。每次采样开始先丢弃上次未提交候选，失败不能留下可提交的旧缓存。

## 验证

- 新 Core 7/7：冷输入、重入保留、未访问状态、零 alpha 目标、清理前更新、
  零方向输入、各状态独立姿势/曲线/来源权重及非法调用。相关 Release 18/18。
- Core 常规 1946/1946，继续排除 P5A golden/trace schema；Import 全套
  1179/1179，包含新默认值变异测试。Godot 优化构建零警告、零错误。
- 30/60/120 Hz 真实骨架共 210 个冷初始候选案例：改变求值帧身份而不 Update，
  均得到参考姿势、空来源曲线、零外部方向输入读取；三次未初始化调用被拒绝。
  这些是候选求值/恢复案例，不是已提交整图连续运行的原生轨迹。
- 原真实资产活动专项 1260 帧、六方向、5040 次来源曲线检查、36 个分支检查、
  六次骨骼/曲线失败重试通过。活动采样的测量区间为 0 B。
- Standing/Sprint/Detail/Pivot 长回放保持通过，九次换髋、371 个等待帧；Main
  六缓存与旧入口各 1680 帧，六缓存入口 2360 次原始姿势检查与故障恢复通过。
- Worker 单/并行各 180 帧，结果均为 `21E164D829153157`，完整姿势均为
  `CF9225D4DE9B2C8B`，与前批一致；各十个来源事件，晚期事件和整体事务回滚
  通过，回调泄漏零。涉及文件空白检查通过。

TRX：`artifacts/test-results/standing-direction-inputs/`。Godot 最终日志：
`artifacts/standing-direction-inputs-cache-final.log`、`-cycle-final.log`、
`-main-final.log`、`-main-legacy.log`、`-worker-single.log`、`-parallel.log`、
`-rollback.log`，均使用 `standing-direction-inputs` 前缀。初版日志保留。

## 未关闭的完整性工作

六状态的 ModifyCurve/YawOffset 尚未实现独立输入与写入，所以“空来源曲线”
不代表完整方向状态最后也应为空。已找到旧只读审计的 `yaw-curves.json` 和
`UpdateRotationValues.bplisp`：后者指向速度方向相对控制器朝向，两个 CurveVector
分别给前后和左右输入。该 Lisp 表达没有保留明确的向量分量引脚，逐度样本也
不等于完整曲线键和插值数据；下一步应核对正式引脚/数据，不猜分量或插值。

完整初始化/骨骼计数传播、来源更新上下文与清理顺序、其他方向图的独立输入、
外层曲线 presence 和未求姿势的旧曲线查询仍需继续核对。此批没有新增完整 UE
动画图逐帧探针，也不证明整个初始化生命周期已等价。

之后继续 Main Movement、真实 Slot/Montage、最终惯性化和统一 Demo 提交，
最终 Layering/Add/LS/Lean/YawOffset、Foot IK/Lock/pelvis，以及原 P5A-P7 的
通用事件/动作、Overlay/道具、攀爬翻滚、布娃娃恢复、完整相机和最终性能验收。
音频仍暂缓；未改已确认键鼠，未提交、回滚或合并工作区。

没有新移动截图、平台路线、短矩阵或十分钟采样。起步滑步、换髋、交错步、
上身，以及原有平台脚锁/性能失败门禁继续保持未完成。
