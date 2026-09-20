# 外层移动来源与落地内容

日期：2026-09-12。工作区 `../GodotALS-p5a-events-actions`。
完整性恢复第七十四批，上一批属于已验证的实质进展。本批未提交、回滚、合并，
保留既有工作区改动；未修改 UE 插件、源资产或已确认的键鼠。

## 来源完整性

`CompileWithMovement` 在已实现的 Grounded+Jump 62 个播放器/88 个样本后，
追加十三个来源与二十一个样本，完整来源表为 75/109。旧 player/sample 身份
和既有来源数据保持。共享帧扩容，旧图仍使用自己的正式绑定数量，不依赖容量。
六个旧同步组后追加 Fall、Land；来源绑定、样本、通知 occurrence 用正式编译器
生成，不能跨不同绑定 stamp 复用历史。

| 所属状态 | 原生编译节点 | 来源 |
| --- | --- | --- |
| Fall | 251/252/253 | Flail、FallLoop、FallLoop_Fast 三个循环序列 |
| Fall | 257/258 | Heavy/Light 预测落地，固定零时刻 evaluator |
| Fall | 260 | Falling Lean BlendSpace，五个独立样本 |
| 父级 Jump | 282 | 另一实例的 Falling Lean，独立样本与时间 |
| 父级 Jump | 285/286 | 另一实例的 Heavy/Light 固定预测姿势 |
| Land | 290/291 | Heavy/Light 非循环落地，倍率 1 |
| Land Movement | 297/298 | Light/Heavy 网格空间附加落地，倍率 1.75/1.5 |

来源编译校验 baked 状态归属、节点闭合、动态输入、生命周期函数、同步策略、
速率/起点/循环、BlendSpace 原生样本与导入资产的一致性。相同资产并不共用
播放器或样本身份。预测 evaluator 的显式零时间、Teleport 和循环策略按原图
核对，实际共享批次验证它们不进入活动时钟或通知 tick。

## 落地节点与运行时

`AlsLandingPoseCompiler` 验证 A/B 连线、Abs(FallSpeed) getter、映射参数、
ModifyCurve 输入、网格空间附加类型和 Main Grounded States 缓存读取身份。
注意静止落地的 baked 相关性顺序是 Heavy/Light，而双路初始化/更新顺序是
Light/Heavy。不能用一份统一顺序替代两者。

`AlsLandingBlendInputs` 分别保存 Land 和 Land Movement 的节点输入；只在被
更新的状态捕获下落速度，单位由 m/s 转为原始 cm/s，取绝对值再映射。
静止落地 500–1000 -> 0–1，移动落地 750–1500 -> 0–0.75；映射本身不截断，
最终限制到 0–1，因此 1750 cm/s 时移动落地可以达到重落地权重 1。
移动附加节点首次 Update 前保持未激活，Initialize 不清除已捕获内部状态。

`AlsLandingSourceCollector` 按候选初始化/更新提供真实 source epoch、时间、
权重及上下文；移动落地先初始化/更新地面缓存，再处理附加分支。缓存消费者
由显式接口提供，没有在缺少真实所有者时自动忽略它。时间由共享 Sync batch
统一推进；静止落地完成 getter 和移动落地自动规则读取实际来源时间。
暂时不参与同步更新的播放器仍可按保留权重成为相关来源；它的原始累计时间
保持可用。对本批非循环落地序列，缺少活动 delta 历史不妨碍自动规则判断。

`AlsMeshSpaceAdditivePose` 对照本地 UE `AnimationRuntime.cpp` 和
`AnimNode_ApplyMeshSpaceAdditive.cpp`：附加旋转在网格空间求差/累加，再转回
局部旋转；平移和缩放始终在局部空间。绝对导入动画按自身 ALS_N_Pose 基准
重建附加值。没有把它当成普通局部空间附加，也没有实现成普通上身分层。
零权重直接保留基姿势，避免无效空间转换改变浮点值。

`AlsLandingPoseGraph` 用真实导入动画求两个状态的内容。静止落地对轻重绝对
姿势做双路混合；移动落地先对两条附加姿势混合，再应用到调用方提供的地面
姿势。曲线按各动画自身 ID 采样、保留缺失和存在零、组合附加曲线，最后执行
原生 ModifyCurve 的 Blend 写入。Land 写 Enable_FootIK_L/R、FootLock_L/R、
BasePose_N 为 1；Land Movement 只写两个 IK 和 BasePose_N，不发明脚锁写入。

## 验证

- Core 新专项 8/8：速度映射、状态输入保留、非交换父子旋转重建、局部平移、
  零权重、原地缓冲区、层级和非法输入。Core 常规 1968/1968，沿用排除
  AlsP5aGoldenTests 与 AlsP5aTraceSchemaTests 的过滤，不称这两组已通过。
- Import 新专项 15/15：十三来源闭合和独立身份、原图参数、落地内容连线，
  篡改归属/倍率/循环/组/预测时间/Lean 输入/样本/落地曲线和缓存均拒绝。
  全套 1249/1249。TRX：`artifacts/test-results/movement-sources/`。
- Godot 优化构建零警告零错误；未在待完成或失败构建后运行 Godot。
- 落地真实回放：30/60/120 Hz、静止/移动落地、三档下落速度，共 3780 帧。
  1752 帧内容姿势、1588 组实际动画端点重建、10512 次曲线检查、18 个来源
  事件、18 次按实际播放器时间退出、18 次非法姿势候选后的恢复通过。
  同帧重试的状态/输入/时间/epoch/权重/事件身份/骨骼和曲线一致。
  单独验证移动附加首次 Update 前不生效，以及未活动来源的相关时间保留。
- 完整来源绑定还验证九个外层活动播放器/十七个样本/八个组可进入共享批次，
  四个预测 evaluator 不推进时间或产生 tick。此项给 Lean 受控样本权重，
  不是 Falling Lean 原生网格或最终姿势验证。
- Jump 6684 帧、36 个事件、48 次初始化/重入、24 次故障恢复通过。
  Standing/Detail/Pivot 的九次换髋、371 等待帧保持；Main 六缓存 1680 帧和
  2360 次原始姿势检查、共享批次 2100 帧、蹲姿 1671 帧回归通过。
- Worker 单/并行各 180 帧、每模式十个事件；结果 `21E164D829153157`、
  完整姿势 `CF9225D4DE9B2C8B` 保持。晚期来源事件故障回滚通过。

日志前缀 `artifacts/movement-`；最终落地日志
`artifacts/movement-landing-final.log`。本批没有测试失败或放宽既有断言。
额外初始化与暂时不活动来源问题在代码复查中补齐，并运行最终落地复查。
新落地联测不声明零分配或最终性能预算通过。

## 下一步与尚未验收

这些是正式可复用来源/内容组件，尚未构成统一 Main Movement/Demo 所有者。
落地联测的地面缓存输出为实际 ALS_N_Pose 参考动画，接口调用被检查，但不是
Main Grounded 实时缓存；不能由此声称移动落地完整效果已经等同 UE。
主状态混合、最终 Slot/Montage、惯性化、状态事件 Gameplay 仍需外层接线。

下一步核对/导出 ALS_N_Lean_Falling 专属网格、过滤及附加数据，完成 Fall 和
父级 Jump 的独立混合输入、预测落地和曲线内容，再连接完整主移动提交。
继而继续最终朝向反馈、动态上身/脚部及原 P5A–P7 全范围。音频暂缓。
没有新增 UE 完整图逐骨轨迹、人工移动截图或十分钟性能验证；交错步、换向、
滑步、上身侧身、平台脚锁及最终预算仍未关闭。
