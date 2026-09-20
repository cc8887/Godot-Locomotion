# 第三十二批：来源通知元数据接入主绑定

## 结果与边界

现有 `v4_locomotion_source_graph.json` 的 syncAssets 增加原生 notifies，来源编译器
严格读取并对账当前动画集，借用只读表随现有 P5 主快照进入实际 Context/Cycle。
没有新建旁路配置、时钟、通知队列或播放槽位；旧动画集的名义 timeline 不被改写。

本批范围是当前 Cycle/Detail/Stop 来源闭包的 30 个动画、44 条通知：40 条 Footstep、
4 条 CameraShake。该闭包中没有资产 Notify State，触发偏移目前均为零。新增元数据
不是当前滑步原因的证明；Pivot/状态事件仍来自图的其他路径，不与这 44 条混为一谈。
其他未绑定来源、Idle/Air/Turn/Action/Montage 等全局元数据覆盖仍需推进。

元数据已进生产快照，但 NotifyQueue 的过滤/去重、State 跨帧生命周期及游戏事件
分发尚未接入。旧 Prepare 继续拒绝 v3；完整 Standing/Detail/Stop 与上身未组装到
Demo，没有新的视觉捕获或滑移改善结论，不能关闭 P5A 或整体移植验收。

## 实现

- 原 AlsStopGraph commandlet 的 IncludeCycle 导出新增 notifySchemaVersion=1，
  每条记录原始数组索引、轨道、原始名称、Notify/State 对象路径、类路径、名义
  时间/时长、开始/结束偏移和 GetTriggerTime/GetEndTriggerTime 实际返回值。
- 同时记录权重阈值、概率、LOD 模式/级别、scope request、专用服务器、follower
  条件及 MontageTickType。原有每播放器 NotifyMode 保持原生来源，不另设默认值。
- 新 `AlsLocomotionSourceNotifyCompiler` 与既有 Sync 编译共用同一资产闭包，逐条
  对账动画集 EventId、SourceIndex、轨道、类、名义时间、时长、阈值和 tick mode。
  缺字段、非有限值、越界枚举、数量/顺序/来源失配均拒绝，保留显式不支持的错误。
- 有效结束时间按原生 State 规则核对：trigger + duration + endOffset，其中 trigger
  已含开始偏移。普通通知结束等于 trigger，不把名义结束时刻误当有效结束。
- Core 新增 unmanaged 的范围和策略记录，窗口直接使用既有 AlsAssetNotifyDefinition。
  表按 AnimationId 分段、段内保留原生 SourceIndex 顺序，与 Sync 序列表对应。
- 对象路径生成稳定数值 ID；Notify 和 State 使用不同字段，同类不同对象不合并，
  同对象跨定义不强行拆开。名字保留独立符号表，不拿展示名或类名替代对象身份。
  当前动画集导出器只支持 class-based 通知，named-only 明确拒绝，未声称已支持。
- 来源 Core 视图升为 v2、编译摘要载荷 v3，完整 SHA-256 覆盖新增表与原始来源。
  既有 P5 主/图摘要自动纳入来源摘要；通知策略变化会使主绑定及候选身份失效，
  布局摘要和物理槽数量不变。既有 v2 主运行时未改用这些字段。
- 私有表不可变，字符串诊断表返回副本，Core/Graph 借用视图无分配。主快照测试和
  实际 Godot 线程 smoke 均检查新增表没有在中间视图中丢失。

## 原生导出与构建

canonical 与安装插件源均已更新。两次 `-run=AlsStopGraph -IncludeCycle` 完全相同，
输出 69 图、12 Plant evaluator、16 Detail player、7 Cycle BlendSpace 和 2 Sequence，
assets_saved=0，退出 0、0 错误及 0 警告。未重存 UE 资源或重复导出模型/纹理。

第一次导出在替换配置前去除新增 notifySchemaVersion 和 notifies，用结构化 JSON
DeepEquals 检查其余内容与原配置一致。正式配置与独立重复导出 SHA-256 均为：
`7ABB17769DD31531DA11C53E76E76C3D7C545FC05745A2D1C3AD8BAD6E232D80`。
原生输出/日志位于 `artifacts/unreal/source-notify-metadata*-20260910.*`。

使用 ue-diagnosing-plugin-build-load 技能完成整个 Editor 目标构建、适用插件审计、
独立 BuildPlugin、打包后再次审计、资产验证和普通 Editor 冷启动。仅对 UE 构建
进程使用引擎自带 .NET 10，没有修改系统运行时、复制 DLL 或编辑 BuildId。

完整构建日志前缀为 UE 项目的
`Saved/Logs/PluginBuild/20260910T150859875Z-1609c867fb714f60a1f161d45f17768d`，
BuildId=`dda2cb23-1f68-4a51-848b-0e0267b97b5f`，fingerprint 为
`63DB4EF955217145EA93F365D72AF3CEB60BF8D214AA3BE1A2C974A271ECDE25`。
AlsGodotExporter、AutoTestTools、BlueprintLisp 全部通过审计，独立包
`artifacts/unreal/AlsSourceNotifyPluginValidation-20260910/` 构建成功。

DataValidation 检查 688 个资产，退出 0、0 错误、3 个既有警告。普通非 NullRHI
Editor 冷启动退出 0，三个模块加载成功；日志仍有此前两条 AutomationTest Condition
failed，以及 AI/Navmesh/材质/渲染线程警告，不能称为无错误 Editor 验收。日志为
`artifacts/unreal/source-notify-datavalidation-20260910.log` 与
`artifacts/unreal/source-notify-editor-restart-20260910.log`。

## 测试

| 检查 | 最终结果 |
| --- | --- |
| Import 全套 Debug | 767/767 |
| 通知、主快照和来源视图 Release | 48/48 |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1740/1740 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

新增 30 个 Import 测试：逐字段对照原生通知表、21 个缺失/失配反例、真实资源上的
有效窗口偏移消费、合成 State 结束时间和共享对象身份，以及 6 类策略/对象变化对
主摘要的影响。合成 State 测试只验证绑定合同，不代表原生 Queue/生命周期运行。
原有视图零分配检查已包含新表；旧主绑定回归、旧摘要及旧 Prepare 保护仍保留。

首轮编译修正一次缺少 using；首轮 Import 一个测试错误地把原生路径顺序当编译
AnimationId 顺序，改为按动画身份查找。首轮 Core 一个旧反例把版本 2 固定为非法，
改为 CurrentVersion+1 并全套复跑。失败 TRX 保留在
`artifacts/test-results/source-notify/`，未放宽数值容差或删除失败断言。
本批没有重跑大规模 P5aGolden/Schema 矩阵或十分钟性能采样。

实际 Cycle single/parallel 各 180 帧及 120 个来源时间检查帧通过，摘要与前批一致：
result=`C658034A39C5917B`，full_pose=`076E345A0151A012`，
pose=`D6B3D85394700CC6`，root=`309E8D0E0BEEB2CB`。
双模式 late_transaction 检查通过，来源同步、移动输入、Controller、姿势和 P4 banks
共同回滚；第 13 帧诊断是预期注入，不是未处理崩溃。

30/60/120 Hz 三个资源组件通过：Standing Cycle 15,216 次源时间和 5,040 帧移动
入口检查，Detail 71,400 个骨骼检查，Grounded Cache 1,260 帧/85,680 个骨骼检查。
相关零分配检查通过；Detail/Grounded 仍标记 demo=not_connected，不能当作 Demo
完整姿势验收。日志 `artifacts/source-notify-*.log`。

相机/输入 SHA-256 未变；canonical 和已安装 commandlet SHA-256 相同。
`git diff --check` 退出 0，仅有既有 LF/CRLF 提示。没有 commit、revert 或删除用户文件。

## 下一步

直接复用这些正式通知表，推进原生 NotifyQueue 筛选顺序、随机状态、State 去重和
生命周期，以及来源 Tick 窗口/图状态事件到同一 Prepare/Finalize 及失败回滚。
不得另设永久队列或用旧 winner/cursor 规则静默接管 v3。继续补全角色来源元数据，
组装 Main/Slot、Standing/Detail/Stop、惯性化和 Lean/Sprint Impulse，替换临时权重。

随后依原计划完成动态分层与上身、全部 Overlay/道具、Mantle/Roll/Root Motion、
Ragdoll/Get-up/Pose Recovery、完整相机和最终十分钟预算及人工验收；音频仍暂缓。
