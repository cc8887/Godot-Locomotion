# Refactored 脚部生产分发与图内锁曲线补全

日期：2026-09-13。第一百七十九批。承接移植完整性路线的 P3/P4 补完。

## 本批落地

第 178 批两段 Layered 接口已进入真实 Character/Worker 分发，不再只由
独立切片场景调用。显式启用参数为：

```text
--als-cycle --layered-frame --foot-ik-frame --based-foot-lock
--refactored-movement-curves --refactored-pose-curves --refactored-foot-frame
```

`--refactored-foot-frame` 要求完整分层、脚部、锁脚和曲线输入。默认 Demo
仍未切换。启用时的物理阶段为：

| 顺序 | 所有者 | 工作 |
| --- | --- | --- |
| 0 | 主线程 Motor | 移动、输入、上一帧最终反馈和场景快照 |
| 1 | 动画前半段 | 模型、状态图、分层、脊柱、当前脚部查询请求 |
| 2 | 主线程物理查询 | 对当前请求做真实射线，回传原请求身份 |
| 3 | VisualWorker | 脚部 Rig、手部、最终根、骨骼写回和候选发布 |
| 4 | 主线程 Commit | 验证与提交结果、反馈、通知 |
| 5 | 主线程 Slot | 角色替换、停用、销毁 |

动画两段在 single 下同为主线程，在 parallel 下为不同顺序的工作线程组。
第一段只访问托管动画所有者和快照；它不访问 Skeleton/Node 的引擎属性。
VisualWorker 仍是骨骼唯一写入者。Component 变换由 Motor 输入和展示变换
计算为值快照，附着父身份在构造时缓存；应用姿势前才在骨骼所有者中捕获
回滚姿势。候选跨阶段使用每角色预分配邮箱，不新增每帧请求对象。

三阶段复用角色 admission；停用或销毁先关闭 admission，再取消未完成
候选。查询和动画响应检查当前帧及角色代次；候选交给后半段后由已有
完整事务负责回滚。失败不提交锁脚目标、弹簧、PoseState、源时钟或通知。

旧计时器只覆盖原来的一个 Worker 阶段。新入口遇到旧性能测量适配器会
明确拒绝，避免漏计前半段和物理查询后宣称性能通过。P7 仍需补全测量。

## 实际发现的移植遗漏

第一次旋转平台回放中，旧路径有 119 个连续双脚锁定样本；新路径为 0，
停止后最终 `FootLeftLock/FootRightLock` 仍为 0。

原因是第 177 批只映射了动画资产的原始曲线。V4 动画图内的 ModifyCurve
和固定 Plant 采样仍只生产 `FootLock_L/R`；新 Rig 消费的名称没有同步
经历这些写入。这不是输入等待时间或脚锁强度的问题。

补全位置包括 Standing Idle、Main 状态覆盖、Crouching Idle/Stop、
Stop/Plant Left/Right、Plant 固定样本和 Landing。映射发生在对应的
生产点，之后继续原来的状态/Slot/加法混合；没有在最终输出上强制复制
或补成 1。资产中缺失的曲线仍然缺失，负加法值仍然保留。

生产回放现在逐帧检查两个新名称与原 V4 锁曲线的值和存在性完全一致。
Main Movement 专项也改为与原 V4 通道对照，纠正了“Land 之外锁曲线
一律为 0”的旧测试前提。独立 `--refactored-state-curves` 测试入口同时
保留源曲线和缓存布局中的适配名称。

这属于 V4 资产/动画图接入 Refactored 消费者的明确版本适配，不等同于
两版所有资产和算法已完全一致。

## 验证结果

最终有效日志以前缀 `artifacts/refactored-foot-dispatch-179-writers-` 保存：

- `single.log`、`parallel.log`：真实分发各 960 帧通过，含跳落、蹲姿、
  停止通知、原地转身、角色代次替换、旧结果拒绝及恢复。321 帧存在锁量，
  854 帧有脚部环境偏移；替换后活跃代次实际射线 1612 次。
- 两种模式结果 `B6345BBBADCB7487`、姿势 `38B69148EC9CCD06`、
  完整姿势 `66E084F8FF0938FE`、根 `3C8B520C47ECA5C7` 一致。
  PoseMoving `AAB0325E448AAC0C` 和预测/PoseState `EFC740AA927A17E5`
  保持。新 Rig 改变最终脚部输出，未用旧 Rig 的姿势摘要替换新基准。
- `late-source.log`：第 25 帧真实通知触发后的晚期失败通过，Rig、锁脚、
  最终姿势历史、动画/运行时/骨骼和事件回滚，回调泄漏 0，待处理查询 0。
- `old-regression.log`：旧生产入口并行 960 帧通过，原结果
  `DB9FEFC95ADA4B15`、完整姿势 `765E1669B4501131` 保持，原基准未改。
- `main-final.log`、`main-state-entry.log`：主移动图各 3360 帧，float 与
  precise 均通过，含 297 个状态混合帧、18 个输入拒绝、Slot 晚期失败
  和同帧重试；覆盖脚锁图内写入与缓存/Slot/父级混合。
- Import 相关 28 项通过：`artifacts/tests/refactored-foot-dispatch-179-final-import.trx`。
  优化 Debug Godot 构建 0 警告/0 错误，`git diff --check` 通过。

### 渲染检查

最终 `writers-visual.log` 与目录 `artifacts/refactored-foot-dispatch-179-writers-visual/`
记录真实 Demo 横移/起停/转身 720 帧、12 张截图、51 帧转身、546 帧移动。
脚部单帧最大旋转 13.095°，`violations.json` 为空。人工检查了左右横移
和停止姿势截图；这不是用户的人工验收或 UE 完整角色配对。

相同路线旧 Rig 对照 `refactored-foot-dispatch-179-old-visual.log` 也通过，
最大角度相同，因此不能把旧第 616 帧 41.10° 的消失归因于本次新 Rig。
当前路线不再复现它；原始失败配置的归因、起步接触位移与换髋过渡仍需
继续对照。截图 HUD 的 FPS/WorkerMs 不作为性能证据。

### 平台尚未通过

`writers-platform.log`：补曲线后双脚锁定恢复为 119 个连续样本，然而
目标水平误差 `0.0001755933 m`、最大平台相对漂移 `0.0015091707 m`，
超过原测试 `0.0001 m` 门槛。测试仍报告失败，没有扩大容差。

配对旧 Rig 日志 `refactored-foot-dispatch-179-platform-old.log` 通过：
119 个样本，目标误差 `0.0000015078915 m`、漂移 `0.000014789761 m`。
因此剩余差异需定位到新 Rig 的原生目标、弹簧/IK 与坐标/骨骼写回，不能
把它归咎于平台 Motor 或称为已经达到旧路径精度。毫米级漂移也不能
未经同输入 UE 对照就判断为原版预期效果。

## 后续顺序与验收边界

1. 为旋转/平移平台保存逐帧锁目标、Rig 前后组件骨姿态和真实骨骼，区分
   水平目标误差、垂直偏移、平台相对漂移及有效支撑接触；配对 UE 原生。
2. 完成新分发 30/60/120 Hz、多角色及阶段之间取消/恢复的专项验证，再做
   起步、换髋、上身和原失败路线逐帧/人工验收，随后启用默认完整入口。
3. 继续原 P5A 通用动作与通知、P5B 全 Overlay/道具、P5C Mantle/Roll/
   Root Motion、P6 Ragdoll/Get-up/恢复/完整 Camera、P7 十分钟预算。

第 178 批全量回归债务仍开放：Core 23 项失败、Import public surface
失败及 MainGrounded 嵌套遍历栈溢出。本批没有重新运行这些全量失败套件，
上述专项结果不能代替其修复。未改 UE 插件或资源、未调整已确认的键鼠
输入、未提交/合并/回滚用户修改。总目标继续保持进行中。
