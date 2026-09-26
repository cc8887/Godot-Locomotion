# Grounded 实际姿态宿主与普通 Demo 接入

日期：2026-09-26。基线 `002bece`，直接在主目录实现，无新 worktree。用户 README、项目配置、场景、LayerBlending、HUD、漫游角色及 UID 改动保留，未纳入本批。

## 实际变化

- 新 `AlsRefactoredGroundedHostProfile` 校验原 Grounded 编译/编辑姿态链接、五个 CallFunction 内图、原状态回调函数连线、两条非循环站蹲播放器的可见 rate 引脚与编译值、固定 Roll evaluator、缓存顺序、惯性与曲线策略。
- `AlsRefactoredGroundedHost` 消费上一批六状态/二十边的真实状态更新，驱动两条独立 1.2f SequencePlayer、Roll 第45帧、Standing/Crouching deferred cache，再按原过渡栈合成。node43 不排除任何曲线；node42 最后写 PoseGrounded/FootLeftIk/FootRightIk=1；Roll 基底写两个 FootLock=1。
- 在 Grounded 实际遍历时执行 InitializeGrounded→RefreshGrounded。共享 Parent 的全局准备不再提前刷新 Grounded；隐藏期间保持历史，重新相关时重置速度混合。Standing/Crouching/Roll 的 OnBecomeRelevant 清入口标记；专用站蹲入口停止过渡/转身，Roll 退出按当前 stance 使用原右侧过渡（.2/.2、rate1.5、start.2）。均修改候选队列。
- 角色 owner 统一预校验及提交 Grounded 状态、播放器、惯性、stance、Parent、动作 bank/queue、Transition。更新但不求值、隐藏、取消、后处理后取消、晚期故障、同帧重试与晚期求值拒绝保留。
- 普通 Demo 的 Grounded 输出直接取新宿主，再进入共享 Transition。旧 V4 Grounded 不再合成普通新路径的地面姿态。诊断新增实际提交帧数和状态覆盖位图；入口重置使用新宿主候选。物理骨/虚拟骨及新旧曲线适配沿用前批。

这是完整角色迁移中的 Grounded 输出接入：旧 Grounded 更新、源时钟/通知仍为旧 Locomotion 外层兼容运行；空中、上身和最终脚部尚未全部换成原完整图。Demo 的组件惯性输入仍沿用固定 component 的桥接边界，尚非全链原生验证。

## 验证

Import 使用 Release 与 `DOTNET_TieredCompilation=0`；TRX 保存在忽略目录 `artifacts/tests/refactored-grounded-host/`。

- `grounded-host-first.trx`：4 失败。误把编辑器播放器 Node 模板 rate=1 当实际 rate；实际可见引脚为1.2，编译值为完整 float。改为分别校验模板、引脚、运行值。
- `grounded-host-v2.trx`：1通过/3失败。Standing 最终输出比内部 Pose 布局多 PoseStanding；统一按实际子宿主 CurveNames 映射，未裁掉曲线。
- `grounded-host-v3.trx`：4通过。30/60/120 Hz 各8秒、共1680帧真实 source/stance/动作/惯性求值；每帧后处理后取消重试，与独立干净角色比较。覆盖实际state1–5。
- `grounded-host-regression.trx`：35通过、0失败、0跳过，含本批5项、Grounded机器、共享动作及已有Standing独立/共享原生六组。新增100帧隐藏/稀疏求值与全求值时钟对比，NaN component 故障导致整帧丢弃后可重试。
- 最终补验 `grounded-host-final.trx`：5通过、0失败/跳过，增加后处理后求值拒绝；最终 Optimize 再次0警告/0错误。
- Godot Optimize 构建0警告/0错误。没有运行全仓所有测试、Grounded/Crouching新UE原生连续轨迹或十分钟性能预算。

Godot 日志位于 `artifacts/`：

| 检查 | 结果 |
|---|---|
| `grounded-host-demo-30.log` | Standing143/Crouching75/Transition269/Grounded269；state位图30，air34/sprint51 |
| `grounded-host-demo-60-render.log` | Standing286/Crouching149/Transition538/Grounded538；state位图30，air69/sprint103 |
| `grounded-host-demo-single-final.log` | 最终门禁和提交计数更新后的60Hz single复测，相同计数与state覆盖，退出0 |
| `grounded-host-demo-120.log` | Standing575/Crouching299/Transition1077/Grounded1077；state位图30，air138/sprint209 |
| `grounded-host-dispatch-{single,parallel}-10.log` | 各3621角色帧、取消2/hold1，air520/crouch600/lock961/events120/rays6544；三种摘要同 |
| `grounded-host-rolling.log` | 普通翻滚420帧，接受2/忙拒绝2/完成2，rolling114/turn57 |
| `grounded-host-camera-ragdoll.log` | 普通相机480帧，第一/第三人称、换肩、Ragdoll与起身交接通过 |
| `grounded-host-camera-recovery.log` | 人工注入70个重叠碰撞体，2轮容量失败/恢复，共10失败候选、52成功帧；两条WARNING为预期注入，退出0 |

上述正常场景（容量注入除外）日志无ERROR/WARNING。十角色 pose摘要 `FA21A30CEF695ABE`、root `8B8AAD0E466EA3B5`、result `9DFF6BEE3D38E18E`，只证明同输入调度一致，不是原生全图等价。

首轮普通60Hz测试 `grounded-host-demo-60.log` 失败：state位图22缺专用StandToCrouch。测试一直转镜头，原规则正确走移动/Rotate混合路径；补停稳且不转镜头的双向站蹲段后，三频率均覆盖state1–4。未修改状态规则或降低覆盖门槛。

渲染输出13帧位于 `artifacts/grounded-host-captures/`，检查了485→495蹲下、545→555起立前段，以及210横移、350跳跃。可见连续姿态变化，无这些帧中的骨骼整体飞离；不能据此关闭滑步、换髋、脚部或头颈缺陷，也不能代替人工签收。

## 后续完整目标

继续原 Locomotion（六状态/十六边）及 Jump（五状态/五边），连接 Fall/Jump/Land、源Notify/Sync/Parent反馈，去掉旧外层兼容更新；补 Crouching/Grounded/Locomotion 原生轨迹。上身/Overlay/最终脚部、Mantle/Roll/RootMotion、物理恢复/相机整链回归、人工矩阵、可复现资源交付及十分钟预算均保留在 ROADMAP。音频、道具物理、头颈专项继续暂缓，目标未宣告完成。
