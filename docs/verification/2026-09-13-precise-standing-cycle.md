# Standing Cycle 精确采样与混合接入

第一百五十二批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。
承接第 151 批惯性化精度隔离，将实际来源至 Standing Cycle 尾部的精确
姿态计算接入正式图。尚未完成外层缓存和 BaseLayer 惯性化的精确链。

## 正式实现范围

- Movement 来源工厂改为使用已有原始精确采样器，保留原始参考姿态、
  重定向及 root lock 的精度；同一次采样供给精确姿态与兼容 float 投影。
  没有新增播放身份、来源时钟或另一份曲线历史。源位置键仍有编译后的
  float 米制边界，不能称全部 TRS 与原生逐位相同。
- Pose Cache 增加精确缓冲区模式，保留初始化、骨骼缓存、嵌套作用域、
  候选提交和失败回滚语义；混合精度的候选银行禁止互相复制。
- Standing 方向缓存、26 路方向混合、Sprint/Impulse 输入实际使用精确
  姿态，继续保留 UE 权重及其 float 运算边界。
- Cycle 尾部的组件空间对角缩放、五路附加 Lean 使用精确姿态。
  原有来源批次、共享同步时间与曲线消费顺序保持。

当前在 `AlsCycleDetailGraph.EvaluateCachedSource` 的 Cycle 分支末尾
仍投影到外层 float 缓存。Detail、Grounded、MainMovement 的外层混合
和 BaseLayer 惯性化尚未全部迁移。因此本批不能宣称已经修复完整精度链，
也没有把测试中 UE 原始姿态灌入游戏运行时。

## 同输入 UE 对照

严格门槛保持位置 .001 cm、旋转 .02°、缩放 1e-5、曲线 1e-4；
比较器继续检查逐帧输入、曲线存在性及归一化四元数点积。

| 正式路径 | 超限帧/总帧 | 最大位置差 cm | 最大旋转差 ° |
| --- | --- | --- | --- |
| 横移 MainMovement | 0/1260 | .0000224214 | .0000181515 |
| 横移 BaseLayer | 21/1260 | .0000224214 | .0500546838 |
| 横移最终根 | 27/1260 | .0285840626 | .0500421702 |
| Sprint 最终根 | 54/2520 | .0304123242 | .0549332951 |

全部曲线通过。上一批最终根横移 30 帧、Sprint 62 帧超限；本批减少
但仍失败，不能调整阈值使其通过。MainMovement 通过也不能代表整个图通过。

横移输入请求 SHA256 与第 151 批完全相同：
`7C82022194DC0C73C344446FF29B5146B009B7B7AB248F761F6F6F72A7540D6A`，
故复用 `full-graph-ue-151-stages.json`。新 Godot 捕获为
`artifacts/full-graph-godot-152-cycle.json`；报告为
`full-graph-parity-152-main.json`、`full-graph-parity-152-base.json`、
`full-graph-parity-152-final.json`。

Sprint 脚锁位置输入随实际精确采样有细微变化，新请求 SHA256 为
`EA4AF6F37F7F65F949CDF566E245D4DF1B26018C3A408F34DC011352F5926A7F`。
旧 UE 请求配对被比较器正确拒绝，随后重新冷导出本批输入。最终有效
配对为 `full-graph-godot-152-sprint.json` 与 `full-graph-ue-152-sprint.json`，
报告 `full-graph-parity-152-sprint-final.json`。不能把旧请求的来源比较
`full-graph-sources-152-sprint.json` 当作本批有效配对证据。

有效来源报告 `full-graph-sources-152-cycle.json` 与
`full-graph-sources-152-sprint-final.json`：横移 4252 次、Sprint 9016 次
实际 tick 均通过，时间差为 0，最大权重差分别 2.98e-7、2.38e-7。
该检查覆盖 Godot 实际 tick 来源，不证明 UE 全部节点访问集合相同。

## 回归和视觉检查

- 优化 Debug 构建成功，0 警告、0 错误。
- Core 专项 71 项、Import 专项 64 项通过，结果在
  `artifacts/test-results/precise-cycle-core-152-final.trx` 和
  `precise-cycle-import-152-final.trx`。
- 新增精确缓存共享/作用域、晚期失败、精度模式拒绝及万次零分配测试；
  精确对角缩放覆盖 108 组 UE 样本。初次 Import 用未归一化点积衡量
  非严格单位四元数产生一项误报，改为既有归一化指标后通过，旧 TRX 保留。
- 单线程与并行各 960 帧全部通过，事件 39、lag/stale 为 0。
  新结果摘要 `1800D2271E8B4BD7`，完整姿态摘要 `6C7625FF9CE7D7EF`，
  采样姿态 `BB4B18C8954E0295`，根摘要 `DB5B813964D3479C`。
  正式 golden 已更新；首次旧 golden 失败日志保留。
- 横移/Sprint 共 3780 帧逐帧丢弃和同帧重试通过，包含姿态、曲线、
  来源时间/事件及 Sprint 已提交状态。
- 实际渲染横移 720 帧、120 张截图，退出 0；分析脚本通过。已逐图
  查看生成的连续姿态与方向对照图，存在可见的摆臂、侧身和步态变化。
  本组没有 UE 并排渲染真值，截图不能关闭滑步或换髋效果验收。
  最大相邻帧脚旋转 14.117°，仅为诊断值，不是支撑脚或正确性判据。
  图像位于 `artifacts/precise-cycle-visual-152/movement-contact-sheet.png`
  和 `strafe-directions.png`。

## UE 构建契约

遵循 `ue-diagnosing-plugin-build-load`：完整项目 Editor 构建成功，
0 个待编译 action，三项项目插件审计全部通过。日志前缀为
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T045738027Z-37c1a03dba954a5881719b0e12dfee7a`。
本批没有修改原生插件源或部署 DLL，fingerprint 仍为
`CC596A2A3B406B414C02E0152572EE86D4CD52967404B31E335F61E2C70C2B7A`。
新 Sprint 冷导出 2520 帧、79 骨骼，退出 0、0 error/0 warning、0 资产保存。
日志 `artifacts/unreal/full-graph-ue-152-sprint.log`。第 151 批的普通
Editor 重启、DataValidation、隔离 BuildPlugin 仍是相同原生二进制的
已有证据，本批未重复这些流程，不能称新 Sprint 已做普通 Editor 配对。

## 下一步和未关闭项

继续从 Cycle 外层扩展精确姿态缓存与混合，覆盖 Standing/Detail、
Grounded、空中分支、MainMovement，到 BaseLayer 惯性化候选和两帧历史。
保留原生差量的 float 边界，避免先降精度再升精度。随后按相同分段和
最终根门槛复验，并完成真实 Motor、平台支撑接触窗口和人工体验验证。

默认仍为 BaseLayer，完整根仍需显式测试开关。P3/P4 最终效果、P5A
剩余通用事件/动作、P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/恢复/完整 Camera、P7 十分钟预算均未完成。
既有 Core 23 项失败、Import 分配不稳定和旧 p95 超预算未关闭。
音频暂缓；本批未 commit、revert 或合并，保留现有其他修改。
