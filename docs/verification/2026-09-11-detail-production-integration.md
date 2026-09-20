# Detail 接入实际角色播放

日期：2026-09-11，第三十九批。保留既有工作树，未 commit 或 revert。
上一批属于实际代码和验证推进，不是等待或无进展；本批继续执行链接入。

## 本批实际变化

Controller 的站立移动分支现在执行 Detail 状态机、共享来源同步、局部 Additive
姿势/曲线组合和惯性化，再将候选姿势交给真实 AnimationTree。它不再只存在于
DetailMachineSmoke/GroundedCacheSmoke 中；原组件测试仍保留，不能代替本批生产验证。

- AlsCycleDetailGraph 消费正式 Detail 表及 16 个独立来源身份，校验资产、Additive
  基础姿势、播放速率和来源布局一致。库构建的两个常规入口均补 Detail 资源闭包。
- Cycle 与 Detail 先收集相关来源，再执行一次共享 Sync batch。四个组的历史、
  播放时间、epoch、缓存权重、样本与通知均在同一候选帧内，不复制测试专用时钟。
- 来源容器按正式 playerId 索引，容量为 40 个播放器、62 个样本和通知 Tick。
  容量不是已接入数量：本批消费七个 Cycle 来源及相关 Detail 来源；Rotate 等仍未接。
- Detail 按状态进入重置来源、清理缓存权重，并用上一帧正式来源时间/权重选择
  RelevantTimeRemaining。来源通知沿用现有 P5 筛选、生命周期和主线程提交。
- 实际姿势与曲线作为值型候选输出保存，旧基础过渡、失败回滚和重试不会引用
  下一帧被覆盖的数组。惯性化历史单独候选/提交，只在成功提交时更新。
- 惯性化使用实际 Skeleton 组件变换；父节点身份取该角色持有的固定骨架父节点。
  曲线批量采样复用一次方向权重计算，避免为每条曲线重复遍历全部过渡贡献。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema 套件 | 1835/1835 |
| Import 全套 Debug | 815/815 |
| Godot 最终构建 | 0 警告、0 错误 |
| 实际 Controller Detail 专项 | 30/60/120 Hz，1890 帧，RunStart/来源/惯性化重试通过 |
| 原 Standing Cycle 专项 | 换髋 9 次、等待 371 帧、来源时间检查 17337 次 |
| 实际 Controller 稳态与活动采样 | 0 B 托管分配断言通过 |
| 新 Cycle 单线程/多线程 | 各 180 帧，结果和完整骨骼摘要一致 |
| late_source_event | 两模式均在第 25 帧回滚，无通知回调泄漏 |
| late_transaction | 两模式均在第 13 帧回滚，来源/结果/骨骼保持 |
| 旧 verify-p4-pose.ps1 全门禁 | 图、姿势、地形/平台、双模式晚期回滚及零分配通过 |

实际 Detail 专项检查状态机进入 RunStart、正式 Detail 来源确实 Tick、来源不重复，
候选统一时间与输出一致，惯性化期间回滚恢复姿势/曲线/组历史/epoch/缓存权重，
同帧重试骨骼一致。它不是完整 UE AnimBP 逐帧原生对照。

最终 worker 摘要：result=C4C947F89E9E0645、full_pose=951498B0B705A676、
pose=20E580B81AB64ED0、root=309E8D0E0BEEB2CB；来源事件仍为 9 个、首次第 25 帧。
不同于上批的姿势摘要证明执行结果改变，不单独证明视觉更接近 UE。

证据：artifacts/detail-production-*.log、artifacts/test-results/detail-production/。
旧 P4 门禁由脚本执行并退出零，输出包括 P4_POSE_VERIFICATION_OK；未改旧门禁容差。
首轮编译遇到错误的项目路径，以及 record struct 属性返回值不可按引用修改，
修正调用路径及局部值回写后编译通过，没有运行时断言失败被放宽。
本批只读本地原生导出图，未修改 UE 插件、运行 UE 构建或新增原生导出。

## 连续截图与剩余视觉问题

三组实际渲染均为 720 帧、120 图，目录位于 artifacts/：

| 回放 | 最大单帧脚旋转 | 起步低脚位移代理峰值 |
| --- | --- | --- |
| detail-production-strafe-visual | 9.986 度 | 6.1238 cm/帧 |
| detail-production-run-strafe-visual | 11.860 度 | 9.3969 cm/帧 |
| detail-production-sprint-visual | 18.884 度 | 本批未计算 |

已检查步行 66/294、跑步 66/72/78、冲刺 72/132 帧：角色可见、连续运动，
未触发既有异常旋转守卫；上身和起步整体仍不能验收为正确。
跑步采集第 66 至 118 帧共有 53 帧含非 Cycle 组的 Detail 来源（组 3），
与实际 RunStart 来源接入一致；步行换髋开始于 291/487，跑步为 319/496。
位移指标是低脚位置代理，不是严格支撑足滑移；未设置验收阈值，不作为通过。
截图期间并发编译/测试，截图帧率不能作为最终十分钟 Release 性能预算结果。

## 明确未完成

1. 本批没有接完整 Standing/Stop/Main/Slot。外层仍是临时 MoveToward 混合，
   Detail 相关性仍来自这条旧外层分支，MainGroundedWeight 暂为 1。
2. Pivot gameplay 反馈尚未接入，输入仍为 false。因此没有实际 First/Second Pivot
   触发证据，不能称为完整 Detail gameplay 或换向功能已完成。
3. 原生缓存获胜上下文、祖先状态链、被跳过请求的完整传递及 Main/Slot 状态权重
   尚未端到端接通；惯性化仍置于现有基础分支边界，未替换完整原生 Main 图。
   当前固定父节点、teleportDistance=0，跨完整图失去相关性/重挂接/瞬移语义仍待接线。
4. Stop/Plant、Rotate、Idle ModifyCurve/Slot、状态事件反馈与完整来源布局执行，
   以及动态上身、手部 IK、Overlay、原 P5C/P6/P7 等继续保留，不削减原目标。

下一批优先接外层 Standing/Stop 及 Main/Slot 的真实更新上下文，复用本批 Collect
和统一来源事务，移除临时外层混合。随后接 Pivot/状态反馈及动态上身，最终以
同输入 UE/Godot 全状态、来源、曲线、骨骼、连续截图和人工验证关闭问题。
