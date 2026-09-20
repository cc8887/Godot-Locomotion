# 第十四批：原生播放器与采样身份

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
归属：原 P5A 接线所需的真实源图合同，继续服务于完整 Locomotion 移植。

## 本次发现

不能把当前 Cycle Demo 的 26 个姿势采样槽位直接当成 UE 播放器表。
新增的原生导出读取了 `(N) Locomotion Cycles`、`(N) CycleBlending` 及原有 Stop/Detail
图，共 69 张图和六个编译后状态机。Cycle 内容状态的原生 playerNodeIndices 为
199、200、201、202、203、205、206、207、209，对应九个播放器：六个 WalkRun
BlendSpace、一个 Lean BlendSpace、Sprint F 和 Sprint F Impulse。

| 域 | 播放器/求值器 | 采样实例 |
| --- | ---: | ---: |
| Cycle（包含 Lean、Sprint Impulse） | 9 | 31 |
| Detail | 16 | 16 |
| Stop Plant | 12 | 12 |
| 本批源表合计 | 37 | 59 |

这是指定子图的源表，不是整个 ALS 或最终 P5 图的总槽位数；不包括外层 Idle、
蹲姿、空中、Aim、Turn/Rotate、Action 等其余来源。不能用 59 再冻结一个全局布局。

新保留的源数据：

- WalkRun FL 初始归一化位置 0.93，FR 为 0.33，F/B/BL/BR 为 0.2。
  这是播放器初始化值，不是每帧强制相位；之后可能被组同步调整。
- RunPose 样本资产 RateScale 为约 0.0416，片段长度约 0.0333333 秒，其有效长度
  约 0.8013 秒。不能仅凭短片段认定它在同步中可忽略。
- 原生 WalkRun 样本均非 SingleFrame evaluator；WalkPose 约 1.1333333 秒。
  当前 Demo 将 Pose 角固定在零时刻，尚未完整对齐这些采样的时间与曲线语义。
  本批没有证明每个 Pose 骨骼轨道随时间变化，也没有因此直接宣称滑步原因已唯一确定。
- Sprint Impulse 有独立序列播放器，PlayRateBasis 约 0.833；不能只保留 Sprint F。
- BlendSpace 通知策略为 HighestWeightedAnimation，不是所有样本都发送 Notify。
- UE `AnimNode_BlendSpacePlayer.cpp` 的 UpdateAssetPlayer 为整个 BlendSpace 创建
  一个 TickRecord，携带独立的 BlendSampleDataCache 和归一化时间，并提交到 Sync。
  内部采样有自己的源时间/通知身份，但不是每个样本都作为独立 SequencePlayer 加入组。

因此，早先“26 个真实采样源不能合成一个身份”的方向仍成立，但不等于要创建
26 个彼此独立的组播放器。新的合同明确区分播放器身份和采样实例身份。

## 实现

`AlsStopGraphCommandlet -IncludeCycle` 在既有只读导出器中增加 Cycle 图和编译后属性；
保留旧 Stop / IncludeDetail 调用方式。新增 `AnimGraphRuntime` 模块依赖，读取
原生 BlendSpace player getter、样本数组顺序/倍率和源节点编译索引，不保存资产。

`AlsLocomotionSourceCompiler` 编译这份源图，复用原有 Detail/Stop 严格编译器：

- 使用编译后 Cycle player 顺序，不按动画 ID 去重或按节点名字排序。
- 校验完整资源闭包、骨架、导入样本顺序/坐标/倍率、编译索引、输入变量及支持的同步策略。
- 同一个资产在多个采样实例中的资产倍率必须一致；不同采样实例仍保留独立身份。
- 导入模型保存源路径与审计信息；另外生成无托管引用、顺序布局的 Core 数值绑定。
  输入变量编译成枚举，运行时不解析源图字符串。源图 SHA256、资产定义摘要、来源记录
  和数值绑定都参与绑定摘要；数组采用防御性复制。
- DetailMachineSmoke 已使用这些数值绑定中的身份、组 ID、起点、时长和倍率调用现有
  Length Sync，替换组件测试里的硬编码 Sync 名称映射和局部 0..15 播放身份。
  不新增生产调度器，其候选/提交宿主仍只是组件测试，不是正式 P5 所有权。

另外，旧 `AlsP5OccurrenceLayoutCompiler.Compile` 之前会接受带 StandingWalkRun 的
Cycle 配置，却仍生成旧 Base22 布局。新增用例先复现“不抛异常”，再增加明确拒绝，
避免静默漏源。旧纯 P3 配置的布局及契约保持不变。拒绝混用是迁移保护，不是完成新布局。

## 原生验证

使用 `ue-diagnosing-plugin-build-load` 技能：每次修改后完整 Editor target 构建并审计
所有适用项目插件，成功后再冷启动 commandlet。没有改引擎、BuildId 或复制 DLL。
该技能引用的两个 superpowers 技能本会话不可用，不声称执行过。

最终构建日志前缀：`20260910T073145771Z-39cc4019978b4db4b141389a11e5fcac`。
BuildId：`0de43418-1cd2-47c8-9f70-40628649acf3`。
构建指纹：`93ACBCF19F08B280D952EC83E092C555C8479D17F0D443B2151AA5C9A1E6EE34`。

```text
ALS_STOP_GRAPH_OK graphs=69 plant_evaluators=12 assets_saved=0
ALS_DETAIL_GRAPH_OK players=16 assets_saved=0
ALS_CYCLE_GRAPH_OK blendspaces=7 sequences=2 assets_saved=0
```

正式源文件：`assets/config/v4_locomotion_source_graph.json`，1,205,562 bytes。
SHA256：`5CF8EFB56711A8B608BADC9DC2915F5A49B0DFACC65497478FE67989FD0DBE27`。
两次独立进程导出哈希一致：

- `artifacts/locomotion-source-graph-native-complete-20260910.log`
- `artifacts/locomotion-source-graph-native-repeat-20260910.log`
- 重复结果：`artifacts/locomotion-source-graph-repeat-20260910.json`

两次均退出 0，0 errors / 0 warnings。首次试运行使用了错误属性名，退出 13，未生成
结果；已依本机源码修正为 bAllowMarkerBasedSync，保留首个失败日志。
未运行 GUI 重启、项目数据验证或打包，不将本批当成插件发布验收。

## 测试

- 新源合同测试 19 项；全量 Import Debug 620/620。
- Release 源合同、旧布局和 P5A profile 合同共 42/42。
- Core 筛选回归 1547/1547；筛选排除 AlsP5aGoldenTests 与 AlsP5aTraceSchemaTests，
  两个长时集合仍待正式接线后完整验收，未用本次结果代替。
- Godot 编译 0 warnings / 0 errors。
- 数值身份接入后的 DetailMachine：30/60/120 Hz、71,400 次骨骼检查、1,050 次候选重试、
  38 次惯性化请求、6 次重置、热路径 0 B，输出明确标记
  `sync=length_runtime identities=source_graph demo=not_connected`。
  日志：`artifacts/detail-machine-numeric-source-identities-20260910.log`。
- StandingCycle 回归通过：三种帧率、三种相位、9 次换髋转换、9 次普通回滚和
  9 次中断回滚，空闲/活动热路径均 0 B。日志：
  `artifacts/standing-cycle-source-bindings-regression-20260910.log`。
- Camera/Input SHA256 与上一批一致；`git diff --check` 通过（仅已有换行符提示）。
  未新增 Demo 多帧截图或人工动作验收。

一次中途 Import 全量测试暴露带字符串的导入模型误用了 P5 运行时命名边界；已拆清
导入配置与纯数值 Core 绑定，保持原有“P5 运行时不得携带字符串”的测试不变。

## 下一步

1. 补 UE BlendSpace Tick 与组同步轨迹，覆盖播放器初始化、样本倍率/长度、组 Leader
   切换、内部 Marker 映射及 HighestWeightedAnimation 选择。不能用现有 Sequence
   Sync 单测证明 BlendSpace 等价，也不能先把 24 个 WalkRun 样本当作独立组成员。
2. 据此补正式 P5 全局播放器/采样布局与组历史，保留所有其余来源及 digest/容量门禁。
   当前 Core 数值源绑定尚未接入 P5 Prepare/Finalize，也不代表所有状态时间已经对齐。
3. 继续有效 Notify 元数据、Queue/State 生命周期、统一回滚与源图曲线消费，再接外层
   ShouldMove/Stop/Detail、动态分层和可玩 Demo。

当前没有新的滑步或交错步视觉改善结论。原计划 A/B/C/D 的其余范围不变，音频仍暂缓。
