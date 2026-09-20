# 蹲姿方向机器与姿势执行

日期：2026-09-11，第四十七批。工作区 `D:\GodotALS-p5a-events-actions`。
上一批完成主蹲姿状态规则和姿势内容合同。本批没有 commit/revert，没有改 UE 插件、
原生动画资产或已确认的键鼠控制。完整目标保持，未关闭基础移动视觉验收。

## 原生依据

- `v4_grounded_dependencies.json`：原生 `CLF_Directional States`、六个方向内容图、
  `(CLF) CycleBlending` 及完整 Cycles 输入连线；复用已有正式导出，不重复导出资产。
- `v4_pose_cache_graph.json` 的完整 `compiledNodeInventory` 已包含蹲姿缓存：
  使用 `cacheSourcePropertyIndex` 连接读取节点与保存节点，并验证 property/compiled
  索引的对应关系。不能从显示名称猜测保存节点身份。
- 本机 UE `AnimInstanceProxy.cpp` 的 GetInstanceStateWeight/GetRecordedStateWeight
  读取前帧缓冲；`AnimNode_MultiWayBlend.cpp` 按引脚顺序更新/求值，归一化后剔除
  低权重，无有效输入时返回 Reference Pose。本批读取源码，没有新增原生运行探针。
- ChangeDirection 的 15 个骨骼条目/因子 2 来自既有曲线导出；复用第四十四批已
  原生对照的通用 WeightFactor 实现，保留独立 incoming/outgoing 归一化及最小值。

原生蹲姿与站姿不能直接共用规则表：蹲姿状态顺序为 F/B/RF/RB/LF/LB，24 条转换
全部为 0.7 秒 Cubic。中性偏向规则分别读取 RB（3）和 LF（4）的前帧权重，方向
与髋转换可以在同一帧连续发生。这里没有套用站姿自定义 0.75 秒转换曲线。

## 实现

`AlsGroundedMachineCompiler.CompileGrounded` 新增 CrouchingDirection 配置。
同一对状态之间可能有方向、偏向、中性偏向多条边；编译器按每个 baked delegate
的所有出现位置求候选规则图交集，消除歧义后仍按 baked exits 遍历。测试覆盖编辑器
节点数组反转及 delegate 重新编号，输出保持不变。旧 input schema 入口仍不切换。

共享 Core 状态执行器新增方向输入、HipBias、FeetCrossing、指定 WeightState。
严格保留 ±0.5 边界、Feet_Crossing == 0、完整前帧权重、每帧三次转换上限、重入、
初始化、候选状态和事件顺序。Pivot 索引 22，髋状态事件 16..21；尚未接入蹲姿玩法反馈。

`AlsCrouchingDirectionPoseCompiler` 严格读取：

| 层输入顺序 | F | B | LF | LB | RF | RB |
| --- | --- | --- | --- | --- | --- | --- |
| 正式 Player ID | 49 | 50 | 55 | 51 | 52 | 53 |

原生保存缓存更新顺序映射为 `[2,5,0,1,3,4]`。六个方向状态的四路缓存贡献和
FYaw/BYaw/LYaw/RYaw 写入均读取实际引脚；校验 24 个缓存读取的编译后归属，保留
图层输入到实际序列的身份，不按资产名称里的 CLF/CRF 推断方向。

`AlsCrouchingDirectionPose` 执行四路 MultiWayBlend、逐层活动转换栈及最终旋转
归一化；普通曲线使用标量 alpha，YawOffset 在每个方向状态中覆盖后参与过渡。
腿部按原生因子 2 及 1e-5 最小权重分别累计两端，不将 outgoing 改为 1-incoming。
零总速度权重返回参考姿势/零来源曲线，但仍执行方向 YawOffset 写入。

来源时间仍属于共享 Sync。本批没有另建播放时钟。`CrouchingSourceSmoke` 使用
正式 SourceAware 绑定、真实资产、共享时间和通知，然后执行新方向组件及候选重试。

## 验证

结果目录：`artifacts/test-results/crouching-direction/`。

| 检查 | 结果 |
| --- | --- |
| 新方向规则/姿势专项 | 36/36 |
| Core 常规最终 | 1863/1863 |
| Import 初轮（新增最小值测试前） | 977/977 |
| Import 最终代码首轮 | 977 通过、1 失败，总计 978 |
| Import 布局分配用例隔离复跑 | 1/1 |
| Import 完整复跑 | 978/978 |
| 相关 Import Release | 141/141 |
| Godot Optimize 构建 | 零警告/错误 |
| 蹲姿实际方向组件 | 30/60/120 Hz，共 420 帧、6 状态、30 转换、315 帧中断混合 |
| 蹲姿来源 | 7140 次真实姿势采样、30 事件、6 条 Walk 有运动 |
| Standing 实际 Controller | 5040 帧；Pivot 5040、Detail 1890、Sprint 1260 |
| Main 组件 | 来源 1050 帧、受控混合 420 帧、中断 104 帧、活动求值 0 B |
| 单/多线程 Worker | 各 180 帧、10 通知，首次通知第 25 帧，结果及姿势摘要相同 |
| 单/多线程来源事件晚期失败 | 第 25 帧注入，来源/随机状态/身份/姿势/Controller 回滚，0 回调泄漏 |

新方向状态/姿势/曲线在专用工作线程暖机后 2000 次重复求值分配 0 B。
Worker 保持 result `A9DF0647AFC3574C`、full_pose `04D4A5651B87E0E4`、
pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB`。

## 保留失败

- 新规则测试初次把“方向转换优先”理解为本帧不再换髋。原生允许再从新状态走下一条
  边；修正夹具为完整两次转换及三个事件，不减少实际转换上限。
- 新姿势测试用 `200 + 100*alpha` 作为预期，在浮点舍入边界和原生加权累计结果
  相差一个 ULP。改为原生先乘两端权重、再相加的精确断言，不放宽容差。
- 新 Godot 覆盖夹具首次没有让 LB 停留到帧末；输入路径增加 Backward -> Left，
  现在全部六状态均实际作为帧末输出，仍保留首轮失败日志。
- 中间一次 Import 全套的既有布局用例
  `ViewsRemainImmutableAndRecordCopiesRefreshTheirCoreEntryTable` 测得 1504 B，
  期望为 0。未修改该用例或生产布局代码，隔离与全套复跑均通过；不宣称根因解决。

## 边界与下一步

方向组件的来源活跃权重、速度权重和 Yaw 输入仍由测试提供。原生缓存归属及更新
顺序已经编译，但尚未接入实际最高权重更新/缓存生命周期。没有新完整 UE AnimBP
逐帧轨迹，没有新 Demo 移动截图，因此不宣布滑步、交错步或上身改善。

接下来完成完整 `(CLF) Locomotion Cycles`：StrideBlend 双路混合及原生 Alpha
插值、ik_foot_root 在 Component Space 的缩放、Lean 加法及外层单状态机器。
随后组装上一批主蹲姿状态的实际姿势、Slot、缓存和 Main 上游权重/P5 事务。
最终曲线/动态上身继续按原规划推进；Standing 较早实现的 WeightFactor 和零总权重
边界也需要同源回归统一，不能只因为当前普通 Standing 回放通过就忽略边界差异。

正式数据未改。Grounded SHA-256 为
`B0B5DFB8BFDE3059FA25BF9E8685AAC14CF71FD9F0C795C67A46A224AE589EF2`；
缓存清单为 `7935D7B91999D58B9E1F3293519A1619563B842C766006B969E3C2D02F9F344A`；
曲线表为 `695EC59C1D262A56B745C9483561B824967F8EE0D07EC5ED8C0BE2B8515FC27E`。
本批未启动 UE，既有两条 Editor AutomationTest 错误仍未关闭，也未重跑完整 P4
脚本套件或 P7 性能门禁，不沿用旧记录声称本批已全部验收。
