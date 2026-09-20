# Standing 方向层骨骼缓存接入

日期：2026-09-11，第六十七批。工作区 `D:\GodotALS-p5a-events-actions`。

## 修复定位

当前移植仍不是完整 ALS 动画图的等价执行。动画和曲线已导出、算法组件已实现、
组件测试通过，与最终 Demo 真正经过这些节点是不同的完成状态。此批继续原规划
紧接着的 Standing 内部姿势缓存，不能把它记作滑步、换髋或上身视觉修复完成。

| 当前问题 | 接下来要核对并补齐的执行链 | 原计划归属 |
| --- | --- | --- |
| 换向延迟、换髋与交错步 | 方向状态和活动过渡、每状态输入缓存、上一提交帧曲线、共享来源时间、Main 输出 | P3 完整性补项及 P5A 接线 |
| 起步滑步 | ShouldMove/起停和 Detail 的最终贡献，Weight_Gait/Stride/速率，实际动画相位与电机位移 | P3 基础移动；不能归因于尚缺 Mantle/Ragdoll |
| 双臂贴身、横移缺少上身姿态 | 最终 Layering、Add/LS、Lean、YawOffset 与 IK mask，之后 Overlay/道具手部 IK | P3/P4 与 P5B |
| 脚部和平台接触 | 最终 IK/FootLock 曲线、锁定捕获/释放、pelvis 和平台坐标链 | P4 |

这些是排查和补完依赖，不是已经证明每个视觉问题都由某一个缺失节点造成。
源资产行为继续以本项目 ALS V4 AnimBP 为准；固定 ALS-Refactored 的算法参考
需要标明版本差异，不能混用两者的髋方向规则。

## 本批实现

新增 `AlsStandingDirectionPoseCache`，消费前批严格编译的八个 Save、27 个 Use。
六个方向状态使用实际引用读取方向输入；Forward 内先处理步态混合，再处理
Mask_Sprint，两个原始 Forward 读取共享缓存。缓存求值不会推进动画时钟或通知。

初始化遍历记录当前方向状态进入，嵌套 Forward 顺序为步态 child 0、child 1、
Mask B；重复 Save 依靠初始化计数去重。CacheBones 和 Evaluate 使用独立计数，
骨骼缓存变化使求值失效。候选从已提交生命周期复制，成功后交换，失败后丢弃；
未提交的骨骼结果和计数不覆盖已提交缓存。

`AlsCyclePoseSampler` 的已更新移动路径使用该组件，按读取触发实际方向资源采样。
保留直接组合入口作为测试对照；Core 的方向混合尾部提取为 `ComposeDirections`，
保留 F/B/L/R 累积顺序、活动过渡顺序和前批修复的独立腿部贡献。

本机 UE 源码核对了 `AnimNode_BlendListBase.cpp` 的双路快速混合、
`AnimNode_TwoWayBlend.cpp` 的 A/B 求值顺序，以及 `AnimNode_MultiWayBlend.cpp`
初始化时保留有效 DesiredAlphas 的行为。本批未修改 UE 插件或重新导出资产。

## 验证结果

- 优化 Godot 构建：零警告、零错误。
- Core 常规 1928/1928；仍排除 P5A golden 和 trace schema，不能据此称它们通过。
  Import 全套 1178/1178。TRX 位于 `artifacts/test-results/standing-direction-cache/`。
- 新 Godot 真实资源专项：30/60/120 Hz，共 1260 帧、六方向。来源时间与
  BlendSpace 滤波使用实际运行时，方向/权重是受控覆盖输入，不是人工移动路线。
  7315 次叶输入采样、8575 次缓存来源求值；同作用域每种输入最多求一次。
- 36 次单方向及 Sprint/Mask 分支检查通过，未消费旧的非相关方向数据；缓存与
  直接组合的骨骼结果在既定误差内一致。此对照不是完整 UE AnimBP 骨骼真值。
- 六次故障重试通过：分别在嵌套 Forward 内失败，以及 Forward 成功后在 Backward
  失败；恢复并重试与原候选逐值相同。三个频率各 200 次活动求值测得 0 B。
- Standing 生产回放保持通过：Sprint 1260、Detail 1890、Standing/Pivot 各
  5040 帧，来源时间检查 17337 次，九次换髋、371 个等待帧，活动分配 0 B。
- Main 六缓存入口 1680 帧、2360 次原始姿势对照通过；旧入口同样通过。
  Main 仍是整合夹具，真实 Slot/Montage 和完整 Demo owner 尚未接通。
- Worker 单/并行各 180 帧，结果摘要均为 `3FBC2FF66A74C4D6`，完整姿势摘要
  均为 `CF9225D4DE9B2C8B`，与前批一致。各十个来源事件；并行晚期事件失败
  回滚通过，泄漏回调为零。

日志：`artifacts/standing-direction-cache-final.log`、同前缀 `-cycle.log`、
`-main.log`、`-main-legacy.log`、`-worker-single.log`、`-worker-parallel.log`、
`-worker-rollback.log`。初版专项日志 `standing-direction-cache.log` 保留。

## 未完成项与下一步

1. 本组件曲线容量为零；方向曲线、各状态 DesiredAlphas 等输入仍由外部路径提供。
   尚未统一内部 Save 的曲线载荷和完整 Update/Initialize 语义。生产骨骼计数目前
   固定，完整传播仍待 Main owner；进入状态的位掩码只用于 Save 去重，不代表
   每个非缓存节点的初始化顺序和输入历史均已移植。
2. 首次 Update 前仍保留旧采样路径，尚缺精确空 BlendSpace 样本与控制/曲线
   生命周期；零方向权重的 UnitX 回退也仍需随独立输入状态核对。下一批先补这些。
3. 然后完成 Main Movement、真实 Slot/Montage、主图外最终惯性化与统一提交，
   接完整 Demo；接着补最终曲线、速率/Stride/YawOffset、动态上身与完整脚部约束。
4. P5A 通用事件/同步/ActionPlayer、P5B Overlay/道具、P5C Mantle/Roll/Root Motion、
   P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 最终性能和人工验收全部保留。
   音频暂缓。

本批没有新 UE 完整图逐帧探针、移动截图、平台路线或十分钟性能采样。此前平台
85--96 帧脚锁为零和性能门禁失败保持未关闭；未修改路线、阈值或强制脚锁。
没有提交、回滚或合并工作区，也没有修改人工确认的键鼠控制。
