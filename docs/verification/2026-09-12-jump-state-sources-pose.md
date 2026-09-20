# Jump 状态、来源与内层姿势

日期：2026-09-12。工作区 `../GodotALS-p5a-events-actions`。
本轮是完整性恢复路线的下一步，未提交或撤销用户现有改动。

## 原生证据与实现

`AlsStopGraph -IncludeMovement` 现在还导出 Jump States 的 baked machine。
正式 `v4_main_movement_graph.json` 共 161 个图体、十一个状态机；从新数据中
仅去掉 Jump baked machine 后，与第七十一批的数据逐字段一致。

| 状态 | 原生规则或来源 |
| --- | --- |
| Entry | 空姿势；Feet_Position < 0 到左脚，否则到右脚；均请求 0.2 秒惯性化 |
| Jump Left Foot | JumpWalk_LF 与 JumpRun_LF，分别从 0.12 秒和零开始；读取自身相关播放器剩余时间 |
| Jump Right Foot | JumpWalk_RF 与 JumpRun_RF，分别从 0.12 秒和零开始；读取自身相关播放器剩余时间 |
| Jump Loop | 起跳完成后进入，请求 0.2 秒惯性化；同帧开始到 Flail 的一秒普通混合 |
| Flail | 循环播放 ALS_Flail，常量播放速率 1.2 |

六个编译播放器索引为 266、267、270、271、274、276。
前四个属于 Jump 同步组，后两个属于 Flail 同步组。前五个读取 JumpPlayRate，
没有把角色移动时的 StandingPlayRate 复用于起跳。未增加另一个动画时钟。

`CompileWithJump` 显式生成 Grounded+Jump 来源表，追加六个来源和六个样本，
从 56/82 变为 62/88；原有四个同步组顺序保持，后面追加 Jump 和 Flail。
原有 player/sample 身份和来源元数据保持；扩展的 Sync sequence 表按资产 ID
重新排序，所以 sequence 表索引需重新绑定，不能跨新旧 stamp 借用索引。

完整 Main Movement 导出还声明父级 Jump、Fall、Land 等资产。同步元数据编译
对导出的十九个真实来源节点所声明的资产集合做完整性检查，校验额外资产的
长度、速率、骨架和 marker；任意额外 syncAssets 行仍拒绝。这些元数据已验证
不等于对应十三个未绑定节点已具有运行时来源身份。

`AlsJumpPoseCompiler` 核对左右脚 TwoWayBlend 的 A/B 连接、Speed 输入及插值
设置。输入从 200–500 cm/s 映射到 0–1，但映射结果本身不截断；FInterpTo 以
速度 5 保存未限制的历史，随后才限制节点 alpha。这使高速减速到混合区间时
保留正确响应。两只脚的状态分别保存历史，首次 Update 直接采用映射值，
初始化重置插值初始化标志，未访问的另一脚保持旧历史。

对照本地 UE：`AnimNode_TwoWayBlend.cpp`、`InputScaleBias.cpp`、
`AnimNode_SequencePlayer.cpp` 与 `AnimNode_StateMachine.cpp`。
GetRelevantAnimTimeRemaining 是 Length 减播放方向调整后的累计时间；它不把
结果除以速率。负速率时调整为 Length−Time，无相关来源时返回 MAX_flt。
本轮状态输入仍由调用方提供观察值，生产来源观察器尚待接入。

`AlsJumpPoseGraph` 用正式导入动画求内层姿势和曲线，分别处理参考姿势、起跳
双路混合、Jump Loop、Flail 及活动转换。双路曲线用 Lerp，状态转换曲线用
Scale/Accumulate；缺失曲线不伪装成存在的零值。动画库允许显式传入已编译
来源表，以加载这六个正式动画；默认地面调用仍使用其原有来源表。

## 验证结果

- Core 新专项 8/8：速度边界、第一次 Update、未限制历史、候选重试及无效输入。
  常规集合 1960/1960，沿用排除 AlsP5aGoldenTests 和 AlsP5aTraceSchemaTests 的过滤。
- Import 新专项 18/18，包括源图编译、独立脚状态、重入、同步 tick、30/60/120 Hz
  的真实资产 Sync batch、缺失/无效 JumpPlayRate 不发布输出，以及篡改输入拒绝。
  与 Main Movement 合跑 32/32，全套 1229/1229。
- Godot 优化构建零警告、零错误。真实骨架回放 840 帧、2520 次曲线检查，
  初始 Entry 为参考姿势和空曲线，输出不是恒定参考姿势，重复求值一致，
  活动姿势采样零托管分配。该场景显式给定动画时间；Sync batch 另行验证，
  不是完整真实时间驱动的 Demo 闭环，也不是 UE 全图逐骨轨迹对照。
- Standing/Detail/Pivot 回归保持九次换髋与 371 个等待帧；Main 六缓存 1680 帧、
  2360 次原始姿势检查和失败恢复通过。
- Worker 单/并行各 180 帧，结果 `21E164D829153157`、完整姿势
  `CF9225D4DE9B2C8B`，每模式十个来源事件；晚期来源事件故障回滚通过。

TRX：`artifacts/test-results/jump-runtime/`。真实姿势日志：
`artifacts/jump-pose-smoke.log`。地面与线程回归日志前缀 `artifacts/jump-existing-`。
保留初次测试失败：测试误读起跳脚、扩展导出的资产范围尚未支持、首次同步
历史错误传入未初始化记录。修正后重跑，不删除断言规避差异。
构建时误用不存在的曲线方法和资源加载器已更正，没有在失败构建后运行 Godot。

UE 完整项目 Editor target 构建和插件审计通过，输入指纹：
`195ED42DD3B4A834D7B8403E6A9807B9679F65289EE52D9E221C768E764725B7`。
BuildPlugin 验证包 `artifacts/unreal/AlsJumpPluginValidation-20260912` 成功，未部署包内 DLL。
DataValidation 退出零，零错误、三个旧 AI/导航资产警告。
普通 Editor 重启、只读导出与退出均成功；仍记录两个原先已有的 AutomationTest
条件错误，没有将它们描述为插件错误或清除它们。

正式与重复导出的 SHA256 都为
`43BEE4705484420DA79AECDBAAC34192ED000469DAEB925A013E5A626D90EC1F`。
首次导出与 BuildPlugin 并行时，Editor 启动的 AutoSDK 探测遇到 UBT 互斥；两项
任务最终均退出零。随后顺序重复导出无该冲突且字节相同，后续 UE 验证应顺序执行。
UE 日志前缀 `artifacts/ue-jump-`；导出没有保存 UE 资产。

## 仍需完成

正式 Demo 仍使用地面来源帧，容量为 56/82。需让 Main Movement 的候选状态
统一拥有嵌套 Jump 的初始化、输入历史、来源 epoch/time/weight、事件和缓存
上下文，扩展共享帧并重新生成完整 P5 来源绑定，再把真实来源剩余时间反馈给
状态规则。不能将当前 62/88 来源表直接放入旧容量，或用测试给定时间替代它。

父级 Jump/Fall 的 Lean、预测落地附加姿势，Land/Land Movement 的真实来源，
真实 Slot/Montage、最终惯性化和 Demo 最终提交仍未闭合。之后继续最终曲线
到角色旋转、上身、脚部与原 P5A–P7 全范围。音频暂缓，已确认键鼠保持。
无新增人工移动截图、完整跳跃视觉验收、平台脚锁或十分钟性能结果。
