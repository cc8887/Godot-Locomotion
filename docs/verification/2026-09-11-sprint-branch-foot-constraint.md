# Sprint 源分支与 Foot Lock 约束范围修复

日期：2026-09-11。第三十六批，续来源事件事务接线。

## 本批结论

已修正实际 Cycle 中 Sprint 的分支位置、原生二路步态插值、局部缓存更新贡献，
并修复平地冲刺暴露的 Foot Lock 方向约束作用范围错误。不是完整 Standing、
Detail、Stop 或动态上身的完成验收。未提交 Git，未撤销已有工作区修改。

## 源图与执行

- V4 `(N) CycleBlending` 的 Sprint 在 Forward 输入内部；此前 Godot 在整个方向姿势
  合成之后混入 Sprint，位置不等价。现在先在 Forward 中进行步态混合，再以
  `Mask_Sprint` 混回 Forward，之后才参与方向状态及过渡栈。
- 补导出器 `BlendNode` 属性。UE TwoWayBlend 不使用其他节点常见的 `Node` 字段，
  原导出遗漏其 Curve 输入、Clamp 和子分支更新配置。生产源图已重新导出；其他
  不消费这些属性的历史配置未批量重写。
- 编译器读取实际 BlendTime 引脚：进入 Sprint 0.2 秒，退出 0.3 秒，Cubic；
  不使用结构体默认的 0.1 秒。拒绝未支持的 Blend Profile、曲线处理及更新模式。
- 新 `AlsBinaryBlendList` 对应 StandardBlend 双子节点的每通道 FAlphaBlend 状态、
  中途反向、首帧激活、归一化与零时长旧子节点更新。它不是完整通用 BlendList。
- 本地 Forward 缓存同时被步态分支和 Mask 直通引用时，更新贡献取最高权重，
  相同权重保留先到上下文；姿势仍按两个原生混合步骤执行，不合并旋转归一化。
- Sprint 候选状态、Mask 与已有 Cycle 帧一起提交/回滚；真实资源检查在部分混合
  权重时注入失败，验证姿势、同步和新状态恢复。

原生证据来自本机 UE `AnimNode_BlendListBase.cpp`、`AlphaBlend.cpp`、
`AnimNode_TwoWayBlend.cpp` 和真实节点 Update 调用，不是另一套手写数学预期。
12 个案例、2520 帧，包含 30/60/120 Hz、反向、初始 Sprint 和零/大 Delta。
两次独立命令行导出 SHA256 均为：

`1F5360056D0CC8F078560CBCC3A0411E6B4BA5D90C15E8171AB99377F7596590`

## 脚部失败与修复

首轮 Sprint 视觉测试跑出了原有小平台，虽然退出零，但不是有效平地证据。
该结果保留在 `artifacts/sprint-branch-visual.log`，不作为通过记录。
扩大测试平台并加逐帧 Grounded 断言后，第 128 帧触发 33.736 度脚旋转失败。

增加 `--diagnostic-capture` 后，保留相同失败阈值并继续记录，最后仍返回失败。
720 帧诊断发现第 128/129 帧分别为 33.736/72.976 度；对应原动画最大脚变化
分别约 13.709/9.661 度，修正链显著放大了变化。

右脚锁定权重仅约 0.00000107 时，Core 仍对基础落点应用全强度大腿方向限制，
导致落点偏到角色侧面；下一帧权重归零，Godot 切回未锁定动画落点，产生跳变。
截图 `sprint-branch-diagnostic/frame-0126.png` 可见右腿异常向外弯折。

对照固定 Refactored `UAlsAnimationInstance::RefreshFootLock/ConstrainFootLock`：
约束应作用于锁定目标，然后按锁定权重混合。现移除基础地面候选和最终混合结果上
的无条件方向限制，保留锁定目标约束；必要的地面间距恢复在该锁定目标上完成，
不再把近零锁定扩大成满权重修正。腿长约束和其他地形/平台安全检查保留。
这是约束范围修复，不代表锁定采集、骨盆参考轴、释放规则及整个 Control Rig
已与 Refactored 完全一致，也不混淆 V4 与 Refactored 两套脚部规则。

新增 0、极小值、0.25、0.5、1 权重反例：修复前四项失败、满权重通过。
更新旧半锁斜坡测试的错误满锁角度预期，精确检查加权落点与离地修正；容差未放宽。
推导过程中保留了中间失败记录，未采用单纯放宽角度范围的处理。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema 套件 | 1818/1818 |
| Import 全套 Debug | 783/783 |
| Sprint/姿势/脚部/P4 golden 相关 Release | 112/112 |
| FootPlacement 单项 | 75/75 |
| 实际 Standing Sprint | 1260 帧，9 次部分混合回滚 |
| Standing Cycle | 30/60/120 Hz，零分配组件检查通过 |
| GroundedCache、DetailMachine | 真实资源组件通过，仍为 demo=not_connected |
| 实际 Cycle 单/多线程 | 各 180 帧，9 个来源事件，摘要一致 |
| late_source_event、late_transaction | 两种模式均通过，无事件泄漏 |
| P4 姿势、生命周期、动画图 | 通过；首个姿势性能运行未控制分层 JIT，不作为零分配证明 |
| FootPlacement 地形/平台与回滚 | 单/多线程均通过 |
| 平地冲刺视觉 | 720 帧、120 图，240 个 Sprint 帧、52 个部分混合帧，最大脚旋转 18.884 度 |
| 左右换向视觉 | 720 帧、120 图，最大脚旋转 9.987 度 |

最终 Cycle 双模式摘要：result `8CED067A081CB773`，full_pose `4BE93FAC6C035F8C`，
root `309E8D0E0BEEB2CB`。这一短回放姿势摘要不变，不否定冲刺回放发现的修正差异。
相机与键鼠输入文件 SHA256 与上一批一致，未修改已人工确认的控制行为。

证据位于 `artifacts/test-results/sprint-branch/` 和 `artifacts/sprint-foot-*.log`。
有效视觉目录为 `artifacts/sprint-branch-fixed-visual/`、
`artifacts/sprint-foot-strafe-visual/`，逐帧 JSON 与截图同时保留。

### 旧预期与首错

Core 全套首次出现三项 P4 port oracle 不匹配，1815 通过、3 失败。它们比较的是
旧 Core 生成的 `portExpected`，不是 UE 独立脚部姿势等价断言。
使用现有 `AlsPoseTrace.WritePortOracle` 重建派生预期，旧五份文件保存在
`artifacts/sprint-foot-port-oracle-before/`。结构化比较确认只有 `portExpected`
改变，`nativeActual`、源身份、刺激输入和容差保持不变。不能把此快照更新当作
新的跨引擎脚部一致性证据；新增独立权重反例及实际回放才覆盖本次错误。

另外保留最初 Import 公开契约未包含新增 StandingSprint 属性的失败、
误传 `--als-failure-policy=normal` 的命令错误，以及上述诊断和中间测试首错。

## UE 构建与加载

遵循 ue-diagnosing-plugin-build-load 技能：完整 Editor 构建、跨插件审计、
独立 BuildPlugin 包、打包后审计、DataValidation 和普通 Editor 冷启动均已执行。
未复制二进制到项目，未改 BuildId。构建状态指纹：

`3C807FA7BC02E64ADB7AB94ED80E0521B391CD83C5FD7D6A731196BE9D4378BC`

BuildPlugin 输出：`artifacts/unreal/AlsSprintBranchPluginValidation-20260911`。
DataValidation 返回 0，日志保留旧 PawnActionsComponent、导航版本等警告。
普通 Editor 初始化和模块加载成功，仍有此前同样的两条 AutomationTest Condition
failed；不是完全无错误启动。Quit 命令未自动结束，随后仅对本次 PID 39144 发送
CloseMainWindow，日志记录正常关闭到 `LogExit: Exiting`；重新获取进程对象未保留
退出码，不能声称本次普通 Editor 已取得数值 0 的退出码证据。

## 未完成项与下一步

1. Main/Slot 上游、完整 Standing/Detail/Stop 姿势及惯性化接入实际 Controller，
   移除外层临时 MoveToward Idle/Cycle 混合。已有组件通过不代表 Demo 起停已完整。
2. 状态进入/退出与 Pivot 事件、全图缓存优先级和所有来源统一 P5 事务。
3. `Mask_Sprint` 当前仍取上一帧 Cycle 曲线，并非完整上身/Overlay 最终曲线。
   方向层缓存贡献仍有历史求和路径，完整 StandingPlayRate/Weight_Gait 也未关闭。
4. 动态 Layering/Add/LS、Lean、YawOffset、上身/髋部、手部 IK 及 Overlay gameplay。
5. 原 P5C、P6、P7 按主计划继续，音频暂缓。最终要求相同输入下的 UE/Godot
   整图状态、源时间、曲线和骨骼逐帧对照，以及人工动作验收和十分钟性能预算。
