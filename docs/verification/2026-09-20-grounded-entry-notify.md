# Roll GroundedEntry 通知与状态反馈

在 `${env:GODOT_ALS_ROOT}` / `main` 接续 `2597dca`。没有新建项目或 worktree；用户的
P4 规划文件保留，SHA256 仍为
`78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 原生证据和修复

UE 只读导出确认，`ALS_N_LandRoll_F` 第 2 条通知的实际属性名为带空格的
`Grounded Entry State`，值为 `NewEnumerator2`。原枚举将其映射为 `Roll`；
`NewEnumerator0` 对应 `None`。旧批量导出器只查询 `Mode`、`GroundedEntryState`
等别名，因此此通知在历史 manifest 中降级为 Generic。

新增 `export_grounded_notify_semantics.py` 和版本化原生输入，保留实例属性、
枚举文本、通知类的 Blueprint 图。编译器检查实际类、对象、源索引、枚举、
Received_Notify 的执行链及参数连接：MeshComp -> GetAnimInstance ->
BPI_SetGroundedEntryState，参数来自实例属性。事件类型及 SemanticId 从正式
运行时契约映射；不按显示名猜测，也不改变 occurrence、物理实例、时间或顺序。

生产 Main Movement 的事件准备现在补齐该瞬时通知，既覆盖 Montage 片段，
也覆盖图内相同 Sequence 的来源。Root/Overlay 共享来源布局继续使用原来的
事件身份。发布前完成转换；候选与已提交事件历史仍由原整帧事务管理。

Grounded 主状态机现在收集原生入口重置通知。准确的触发位置是 **离开 Entry
状态时**，不是离开 From Roll 时。编译器核对 baked EndNotify 和原 AnimBP
消费者确实将 GroundedEntryState 写回 None。图通知先于 asset-player 通知
加入 Proxy 队列，消费者按相应顺序计算下一帧反馈。

`AlsMovementNotifyState` 统一维护原有 SetAction Begin/End 及 GroundedEntry
反馈：非匹配动作的 End 不清除当前动作；入口选择保持到真实重置通知；隐藏
图不制造重置。Production 使用上一提交反馈决定本帧规则，最终旋转反馈、姿势
和下一帧状态仍共同提交；丢弃不更新已提交反馈。

本批通过补充原生输入完善当前生产语义，没有重写历史 manifest、冻结夹具、
schema 或数值容差，也没有修改 UE 插件 C++。旧批量导出器的字段别名仍未改；
重新提取当前生产数据时需要同时运行新增语义导出脚本。新增配置与冷导出 JSON
内容相同，配置文件使用 LF、Windows 导出文件使用 CRLF；既有字节哈希依赖未改。

## 验证

证据目录：`artifacts/grounded-notify-20260920`。

- UE 完整 Editor 目标构建和插件一致性审计通过。首次构建因系统缺 .NET 10
  失败，使用引擎自带 `DotNet/10.0/win-x64` 后通过，未安装或替换系统运行时。
  构建日志在 UE 项目 `Saved/Logs/PluginBuild/20260920T091132226Z-*`，首次失败
  保留在 `20260920T090948402Z-*`。
- 冷命令行和正常 Editor 各导出一次，均正常退出 0，均记录 `assets_saved=0`。
  实例属性和枚举文本一致。部分未连接的 EventReference PinId 在正常 Editor
  重建后变化，整文件不字节相同；正常 Editor 输出通过同一个严格编译器，
  生成的语义绑定、参数和重置编号一致。正常 Editor 启动日志另含两条
  `LogAutomationTest: Error: Condition failed`，位于引擎 UnifiedErrorTest 输出
  附近，保留原日志；本次不将正常退出宣称为整个 Editor 无错误认证。
- Import 专项 54/54 通过，包含 10 项新检查：属性名、值、枚举、类、对象、
  源索引、参数连接、接收目标和重置消费者的变异拒绝。
- Core 最终 Release 回归 2518/2518 通过、0 Skip，排除未改动的两类 P5A
  历史 Schema/Golden 长矩阵。本批 9 项新检查包括事件身份/顺序、候选原子性、
  反馈重试、不同动作 End 和枚举越界，以及热身 300 次后测量 1000 次零分配。
  首次总回归的旧 `AlsPrecisePoseCacheTests` 测量出现 6552 字节，单独 4 项
  通过；将其加入现有禁并行 Allocation 集合后整组通过。零分配阈值、测量次数
  及生产缓存实现不变，首次失败和隔离后结果分别保留。
- Godot 优化构建 0 警告、0 错误，最终日志 `godot-build-semantic.log`。
  新代码初次遇到 Span 上使用 Skip 和重命名遗漏两处编译问题，已修正，失败
  日志保留。首次语义专项也纠正了将重置错归到 From Roll 状态的假设。
- 实际 Roll 30/60/120 Hz，共 1050 帧、逐帧丢弃重试、28 次实际姿势采样后的
  故障。9 接受、3 替换、3 取消、3 完成，354 帧完整 Roll 姿势；3 条真实
  GroundedEntry 通知、9 次入口重置、3 帧当前状态为 From Roll。每档结束时
  Action 和 Entry 都回到 None。公开事件、SemanticId、反馈和物理身份重试一致。
- 未访问 Root 回放 1050 帧，651 帧隐藏、468 帧隐藏 Montage 推进，逐帧重试通过。
- 普通键鼠 360 帧通过，Alt/A/D、四次鼠标事件和 83 帧脚趾接触验证保持。
- 十角色 Single/Parallel 各 3621 帧通过，含两次取消及一次提交等待。两种模式
  pose `BF25422552331C92`、root `8B519543E987009F`、result `02BF0BAAA9542349`
  一致，和前一批无动作输入回放一致。这两个调度场景未注入 Roll 请求。

## 继续推进范围

本批完成 Roll GroundedEntry 的真实生产语义和动画状态反馈；不是完整 Roll
玩法验收。下一步接普通 Gather 的动作请求来源、稳定请求身份及主线程玩法
消费，再实现碰撞安全的 Roll/Mantle Root Motion。当前没有新增 Roll 按键。

Overlay 道具玩法、其余 Notify 消费者、完整 Ragdoll/Get-up/Pose Recovery/
Camera、完整图人工观感与地形测试，以及 P7 十分钟性能认证仍按原目标推进。
音频继续暂缓。没有将局部回放通过视为整套 ALS 完成或 1:1 视觉验收。
