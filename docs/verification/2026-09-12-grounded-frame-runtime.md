# 可复用地面帧运行组件

日期：2026-09-12。完整性修复第七十九批。

## 实现

新增 `src/Als.Godot/Animation/AlsGroundedFrameRuntime.cs`。此前实际 Main Grounded
更新、来源收集、六个缓存和骨骼缓存的共同所有权只存在于 smoke 私有 Replay；
现在抽成可供外层 Main Movement 使用的每角色组件。它接收独占的 Standing 图、
资源库、完整来源配置及严格编译的地面配置，不依赖测试时间线或固定帧率。

生命周期为 Begin → 初始化/CacheBones → Prepare → CollectSources → 外层共享
Sync → CompleteSources → 按需作用域求值 → 外层决定 Commit 或 Discard。

- Begin 从已提交地面状态和外层共享来源读取自动转换所需时间，然后建立候选
  缓存、骨骼缓存和蹲姿来源状态。角色身份、代次、帧序号及遍历计数受检查。
- 初始化缓存按传入入口执行；多个别名由同一个 Save 生命周期去重。Standing、
  Detail、Stop、Crouching、Cycles 的原有有序初始化和条件骨骼遍历继续保留。
- 正式输入包括动画速度、蹲姿播放速率/步幅、速度分量、Yaw、Lean、斜向修正、
  Slot 权重和遍历计数。蹲姿斜向修正同时进入 Update 与 Evaluate，未留固定 1。
- Prepare 排空同一地面缓存队列，然后收集 Main、Standing 和 Crouching 来源。
  缓冲支持在其他分支之后追加；组件不执行 Sync，不持有另一套播放时钟。
- Sources 提供阶段受限的显式交换点，外层初始化空中/落地来源后，地面不会
  用 Begin 时的旧副本覆盖它。完成同步后关闭该接口，求值只读取同步结果。
- EvaluateEntry 允许 Grounded 和 Land Movement 入口读取同一缓存结果。求值
  失败会拒绝提交，关闭作用域并 Discard 后可从同一已提交帧重试。
- Slot 更新、惯性化请求、被跳过的缓存上下文及骨骼映射刷新交给外层候选 sink；
  不将这些请求变成隐含的无操作，也不在此分发 gameplay 或发布渲染姿势。
- 可以提交已更新而未求值的分支状态。没有 Grounded 入口的帧不推进其机器
  更新时间和来源时钟；恢复相关性后由原有帧序号逻辑重入。

组件中的 Commit 只提交地面状态/内部缓存。最终姿势、通知事件状态、其他分支
和外层惯性化仍须由完整主移动事务一起验收、提交，不能单独提前调用它。

## 验证

扩展现有 `main_grounded_update_smoke.tscn`，使用 `--grounded-runtime`。新组件
拥有独立资源库、Standing 图及内部缓存；旧 Replay 作为同输入的对照实现。
二者使用完整 75/109 绑定，而不是把旧缓存对象传给新组件。

30/60/120 Hz，共 1680 个对照帧。每帧新组件先求值并取消，再同帧重试提交；
验证骨骼、曲线、状态、斜向修正、骨骼缓存计数、来源时间/epoch/权重、同步
历史、通知 tick 和事件载荷/顺序一致。蹲姿步幅和斜向修正使用随时间变化的
受控输入；事件准备使用相同的前帧事件状态，组件本身不拥有或提交事件状态。

| 检查 | 结果 |
| --- | --- |
| 精确对照与取消后重试 | 1680 帧通过 |
| Slot 实际求值抛错后恢复 | 6 次，已提交身份不变 |
| 生命周期拒绝 | 12 次：无候选提交、重复收集、作用域中提交、已提交帧再次 Begin |
| 外部分支初始化的保留 | 6 次，在 Begin 后写入空中来源，地面收集后仍保持 |
| 无入口的地面停用 | 9 帧，无来源贡献、时间/epoch 冻结、机器更新序号保留 |
| 停用后的真实重入 | 3 次，Main 重入并求出有效骨骼姿势 |
| 新组件热路径 | 6 处采样，每处热身 30 次、重复 120 次，Update/Sync/Evaluate/Discard 合计零分配 |

内存结论不包括测试对照代码、资源构建或整个进程。Slot 仍为明确的合成测试
姿势，不代表 Montage 播放器完成；停用入口由测试控制，不代表外层主移动
状态机已驱动这些帧。最终日志 `artifacts/grounded-frame-runtime-dormant.log`。

前一版常量输入的 `grounded-frame-runtime.log` 和后续可变输入的
`grounded-frame-runtime-final.log` 也已终止且通过。第七十八批旧/完整来源绑定
对照再次通过，日志 `artifacts/grounded-runtime-binding-parity.log`。

## 构建与失败记录

构建最终零警告、零错误。首次实现将 Cycles 输入属性写成 VelocityBlend，
实际契约为 Velocity；补停用测试时又尝试通过 with 给只读 FrameId 赋值。
两处均在编译阶段发现并修正，未在失败构建后启动旧程序集进行验证。

运行方法：设置 `DOTNET_TieredCompilation=0` 和 `COMPlus_TieredCompilation=0`，
成功执行 `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 后，以 Godot
控制台程序运行
`--headless --path . scenes/tests/main_grounded_update_smoke.tscn -- --grounded-runtime`。

本批仅新增 Godot 运行组件、扩展 smoke 和更新文档，没有改 Core/Import 算法，
未重跑其完整套件。旧 Worker 的前批证据不能当成新组件的并行接入证明；本批
没有将新组件连接到 Worker/Demo，也没有执行新的 UE 探针、人工截图或十分钟
性能测量。未 commit、revert 或合并，未改已确认的键鼠。

## 下一项

使用该地面组件与现有 Air/Landing 收集器建立外层 Main Movement 候选所有者：
按主状态机实际初始化/更新顺序交换来源状态、收集 Grounded 缓存请求，再执行
一次共享 tick。Land Movement 要读取这里的真实 Grounded 姿势，替换原来的
参考姿势夹具。随后补主状态混合、最终 Slot/Montage、惯性化和 Demo 提交。

完整最终曲线 presence/名称合并、YawOffset 到角色朝向、动态上身、Foot IK/
Lock/pelvis/平台，以及原 P5A–P7 全范围仍未完成。交错步、起步滑步、双臂
姿态和最终性能门禁均保持待验收；音频继续暂缓。
