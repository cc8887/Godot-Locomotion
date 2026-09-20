# Stop 状态通知与生产播放链路补全

第一百六十三批，2026-09-13。承接移植完整性修复，归属 P5A，并作为 P3/P4
停止姿势、曲线和脚锁验收的前置依赖。本批不修改脚锁角度阈值。

## 本批实现

原 V4 的 `(N) Stop States` 中，Lock/Plant Left 的进入通知为 `->N Stop L`，
右侧为 `->N Stop R`。两者直接调用 PlayTransition，不经过 Overlay 的
ShouldMove/Standing 门控。严格编译器验证原 EventGraph 连接、生成通知索引、
资产路径及共同播放函数：Grounded Slot，起点 0.4 s，倍率 1.5，淡入/淡出
各 0.2 s，播放一次，BlendOutTriggerTime 为 0。

- 新增两项 Stop Down 原始动画的独立资源闭包，连同实际加法基准共 3 个
  Sequence、1 个 Skeleton；不修改既有移动/Overlay 播放身份。公共原始
  文件在导出时逐字节验证后复用，运行时复用一致的不可变资源。
- Grounded 所有者按初始化和 Update 的原顺序保留状态通知，包含重复通知。
  BaseLayer 求值完成后创建 Stop 候选播放实例；之后外层分发 Overlay。
  两者共用 Grounded Slot 和物理 Montage 所有者，保留组中断与退出淡出。
  冻结的当前帧姿势不因新请求改变，新实例从下一帧推进。
- Slot 采样使用对应原始资源闭包的精确加法求值器。原始负曲线保持不变，
  经过图和 Slot 混合之后再送入已有最终曲线反馈。
- 接入两项动画的 4 个原生通知。动态动画不必伪造普通图播放器身份，
  通知参数仍由已验证的动画清单编译器映射。当前清单把这些 Blueprint
  Footstep_AnimNotify 绑定为 Generic；本批保留该正式绑定，并没有实现
  音频或根据显示名称推断脚步玩法。
- 移除生产适配器中“Grounded Slot 必须无权重”的旧限制，改为校验真实
  Montage 帧身份、图节点和 Slot 权重。增加已提交 Stop 请求计数，以及
  调试失败时的原异常栈，避免只有异常类型而无法定位生产故障。

核心文件：`AlsStopTransitionDefinition.cs`、`AlsStopTransitionCompiler.cs`、
`AlsGroundedFrameRuntime.cs`、`AlsBaseLayerFrameRuntime.cs`、
`AlsGroundedMontageSlot.cs`、`AlsMovementGraphDefinition.cs`、
`AlsMontageNotifyCompiler.cs`、`AlsProductionMovementRuntime.cs`。

## 原生数据与测试

- UE 冷导出和普通 Editor 重导出的源索引、124 组原始姿势及通知 JSON
  逐字节一致。原始资源哈希和绑定摘要也通过编译时验证。
- Godot 对照 UE：124 组原始姿势（9796 个骨骼样本）、104 组真正的
  Mesh Space 加法姿势（8216 个骨骼样本）及逐次重采样通过。最大位置误差
  约 1.10e-7 m，四元数分量距离约 5.14e-16，曲线误差约 5.97e-8。
  这是资源求值一致性，不是完整角色接触/视觉一致性。
- 48 项 Import 专项通过，覆盖原通知绑定、资源闭包、四通知时间与正式
  参数、30/60/120 Hz 推进/重试，以及重复 Stop、左右 Stop、Overlay 同组
  顺序替换与回滚。
- 1260 帧完整上身共享所有者测试通过：3 个真实 Stop 状态请求、3 次在
  Stop 请求后注入的晚期 Overlay 失败，逐帧重试保持姿势、曲线、实例 ID、
  时钟和通知一致。隐藏/恢复及其它分层失败测试同时通过。
- 优化 Debug 构建通过，0 警告、0 错误。生产测试现在明确要求实际发出
  Stop 请求，不能仅因图中存在 Stop 状态而通过。

资源对照日志：`artifacts/stop-raw-163.log`、`artifacts/stop-additive-163.log`。
专项：`artifacts/tests/stop-final-163.trx`。
完整帧事务：`artifacts/stop-shared-frame-163.log`。

单线程/并行各 960 帧、两种脚锁路径的成对结果：

| 路径 | Result | 完整姿势 |
| --- | --- | --- |
| V4 | EDE506BBD850B05C | 96B2DB325984773A |
| Based / Refactored | DB9FEFC95ADA4B15 | 765E1669B4501131 |

新摘要反映原 Stop 动画实际进入姿势和曲线反馈后的行为变化。旧摘要首次
失败日志保留；在原生采样、事务回归和四个独立生产运行配对后更新固定
回归摘要，未调整画面检查的 30° 阈值。
最终生产日志：`artifacts/stop-production-{v4,based}-{single,parallel}-163.log`。
四个运行均 exit 0，各实际提交 1 个 Stop 请求。并行生产晚期通知失败注入
也通过，姿势、运行状态、来源时钟、随机状态、通知身份全部回滚且无回调
泄漏，见 `artifacts/stop-late-failure-163.log`；该注入位于第 25 帧，Stop
请求之后的回滚覆盖来自上面的 1260 帧测试，二者不混称。

本批未更改 UE 原生插件。按 ue-diagnosing-plugin-build-load 技能复核
第 162 批完整 Editor-target 构建状态，4 个项目插件审计通过，再执行冷
导出与普通 Editor 重启导出；没有复制 DLL 或修改 BuildId。普通 Editor
已有 Condition failed/项目兼容警告保留，不宣称全部引擎日志无警告。

## 实际截图结果与未完成项

两条脚锁路径都在第 611 帧开始实际采样 Grounded Slot 的 Stop 动画，
共 86 个播放帧。`artifacts/stop-playback-capture-163.json` 保留停止状态、
实例、采样位置和混合权重；不把 Turn Slot 的权重误当 Grounded Slot 权重。

- V4 路径：720 帧、24 张截图通过，最大单帧脚部转角约 11.615°，
  `artifacts/movement-visual-v4-163/`。
- Based 路径：720 帧、120 张截图完整保留，第 616 帧仍有 41.100025°
  突转，测试按原阈值失败，`artifacts/movement-visual-163/`。
  检查了停止前后第 612、618、642 帧截图，不能把后续站稳视为突转消失。

因此，“Stop 没有播放”这一完整性缺口已经补上，但它不是 Based 脚锁
突转的充分解释。第 161/162 批已证明相同输入下原生 Refactored 约束也
会产生该跳变。接下来要追踪 V4 图的满锁时刻、前一帧 Final、当前 Target
与 Refactored 所要求的输入历史，建立一致版本的整链对照；不能只换曲线名
或放宽大腿限制。原 V4 路径本次通过也不能代替完整 UE 同输入场景验收。

默认完整根、起步支撑接触/左右换髋与上身视觉验收仍开放。基础修复后
继续 P5A 剩余通用分发、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera，以及 P7 十分钟性能与人工
验收。音频暂缓。本批未提交或回退用户工作区改动。
