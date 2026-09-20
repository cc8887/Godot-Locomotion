# Full AnimBP Source Inventory

日期：2026-09-10，第十八批。属于完整性补完 A/B 与 P5A Task 15 的布局前置核对，
不是新布局、统一帧事务或 Demo 接线的完成记录。

## 原生证据

`AlsStopGraph -IncludeInventory` 新增可选整图清单；未带该参数的 Cycle 导出内容不变。
清单遍历 ALS V4 AnimBP 的全部编辑器图，使用编译后 CDO 判定资产播放器及其实际资产、
同步组、循环、求值/Teleport 标志和 BlendSpace 内部样本。结构节点、Slot、Snapshot、
缓存读写单独保留，不把读取同一缓存的多个分支当成多个播放实例。

| 原版图范围 | 资产播放器/求值器 | 静态采样引用 |
| --- | ---: | ---: |
| BaseLayer | 75 | 109 |
| AimOffsetBehaviors | 7 | 17 |
| BasePoses | 2 | 2 |
| OverlayLayer | 148 | 148 |
| AnimGraph | 1 | 1 |
| 合计 | 233 | 277 |

全部编译节点/属性为 933；资产来源分为 70 个 SequencePlayer、148 个 SequenceEvaluator、
10 个 BlendSpacePlayer、5 个 BlendSpaceEvaluator。另外有 32 个 SaveCachedPose、
105 个 UseCachedPose、11 个 Slot 和 1 个 PoseSnapshot。它们不是同时活跃数量，
也不是 Godot 最终物理槽位数量；Montage 运行实例、Godot 双 bank 与原版缓存共享还需映射。

特别记录两种不可互换的索引：`compiledNodeIndex` 是原始编译编号，`propertyIndex`
是实际属性表编号；本引擎版本中为 `propertyCount - 1 - compiledNodeIndex`。
缓存 PoseLink 使用后者。导出器从编译后 CDO 读取缓存目标，不能使用编辑器 Node 中的
默认 `linkId=-1`。本批 105 条缓存读引用均解析到对应 SaveCachedPose。

全图可见 10 个非 None 同步组：Fall、Flail、IdleAdditive、Jump、Land、Locomotion、
Pivot 1、Pivot 2、Run Start、SecondaryMotion。当前四组元数据只属于已编译的局部来源，
不代表其余六组已经移植。

## 本批实现

新增 `Als.Import/Inspection/AlsP5SourceInventoryCompiler`，在初始化/检查层完成：

- 原生节点与属性表的完整覆盖、唯一性和索引转换验证。
- 全部 277 个静态采样引用与现有导入资产、骨架、BlendSpace 样本顺序对账。
- 按 SourceNode 和编译编号关联现有 37 个源播放器、59 个采样身份，不按 AnimationId 去重。
- 校验缓存引用、播放/求值类型、已绑定来源的循环及同步组；未知播放器类型明确拒绝。
- 输出防御性复制的检查快照，摘要覆盖原始导出、导入资产摘要、源绑定摘要和全部映射。

检查数据含原生路径/类名，留在 `Inspection`，不加入 P5 的纯数值 Worker ABI。
原有禁止 P5 编译合同含字符串的测试未放宽。快照编号不是 OccurrenceHandleId，
本批没有另建播放时钟、分发队列或第二套全局布局分配算法。

对账暴露并修复了 Stop 源绑定的一处简化：固定姿势求值器原生 `bShouldLoop=true`，
旧绑定硬编码 false。现在保留原始标志；其 Teleport、DoNotSync、固定 ExplicitTime
合同仍然决定不推进播放时间/通知，不能仅依据 Loop 标志启动时钟。

## 验证

- 完整 UE Editor 目标构建和项目插件审计通过，BuildId：
  `a62acd02-ebd3-4df3-b93a-71842617113c`。
- 最新构建日志前缀：`20260910T092047451Z-df1dd037b1d64a7485c511accb877921`；
  输入指纹 `A6A922EE435B0625902E10AF5A3C04B830077D638856D31B4CB3F4B8E57E45F2`。
- 两次冷启动导出退出 0，commandlet 汇总 0 errors/0 warnings，`assets_saved=0`。
  日志：`D:/AdvancedLocomotionSystemV/Saved/Logs/AlsGraphInventory-20260910-links.log`
  和 `AlsGraphInventory-20260910-links-repeat.log`。
- `assets/config/v4_anim_graph_inventory.json` 与
  `artifacts/v4-anim-graph-inventory-repeat-20260910.json` SHA256 相同：
  `A40494DB29676774C1C6EC826A3369242348B90EB54B439F9CC7810D0022FA19`。
- 原有 `v4_locomotion_source_graph.json` 仍为
  `60E5D6F540F186C1A2279004E25AD537A9166B814990B86992C4C03F83E0301F`。
- Import Debug 全量 **649/649**；Release 源绑定与整图检查 **48/48**。
  新增 16 项测试覆盖缓存/采样/身份错误、漏掉未绑定节点、不可变快照和摘要。
- Godot 工程构建 0 warnings/0 errors。
- `DETAIL_MACHINE_OK`：30/60/120 Hz，71400 bone checks，1050 retries，0B；
  `STOP_PLANT_OK`：12 fixed sources，2448 bone checks，0B。
- Camera/Input 文件 SHA 与上一批一致；本批未修改 Godot Demo、相机或输入。

本批没有重新运行 Core 全量、Golden/TraceSchema、原生交互 Editor、DataValidation、
打包、P5 全矩阵或人工 Demo 验证。UE 构建技能规定的全目标构建/审计已用于本次导出，
不能把 commandlet 验证扩称为插件发布或完整项目认证。

## 后续归属与边界

37/59 是 SourceCompiler 已绑定范围，不是 Demo 已实际消费范围。其余 196 个原版资产
来源尚未进入这张源绑定表，也不等价于 196 个完全未实现的功能：部分已有旧 P3/P4
近似路径，需要替换映射，部分属于后续 Overlay、恢复等模块。

下一步应以本清单明确新布局的模块范围、播放器/样本映射及共享缓存归属，再改唯一
`AlsP5OccurrenceLayoutCompiler`、Core snapshot 和 Godot controller/Worker 事务。
不得直接把 49、59、233 或 277 固定成完整生产总槽位数。

优先贯通 Cycle/Detail 的混合同步、ShouldMove/Stop/Pivot、源事件与失败回滚；随后
处理动态上身分层和完整 Aim，再按 P5B/P5C/P6/P7 推进 Overlay、动作/Root Motion、
恢复/相机与十分钟性能预算。起步滑移、多帧姿势与上身问题仍需 Demo 实际接线后验收。
