# ALS / Lyra 共用姿态缓存控制与 Transform 属性

2026-10-04，直接在当前主目录继续实施。目标沿用 ROADMAP 顶部的可玩 locomotion、手枪/步枪和 ALS/Core 复用范围，不要求完整复制 UE 调度与 URO。本批不启动 UE、不重新导出资源、不提交或推送。

## 实际实现

新增 Core `AlsScopedPoseCache`，将已有 ALS 和 Lyra 两处重复的 scope、present、evaluating、求值次数及故障控制合为一个运行机制。原 `AlsPoseCacheEvaluation` 与生产 `LyraMainPoseCacheScope` 都实际使用它。

缓存控制不持有动画时钟、不 tick 源、不派发通知，也不规定 payload 类型。既有 ALS evaluator 保留其单精度/精确骨骼和曲线存储；Lyra 保留包含 pose、曲线、属性和 RootMotion 的资源布局适配。Main83、Main78 和 Provider78 的节点绑定继续由 Lyra 定义，未改成 Core 的固定节点配置。

每个 scope 持有缓存实例、serial 和 depth 身份。新 scope 即使 traversal counter 相同也重新采样；嵌套 scope 的载荷互相隔离；关闭后视图失效。递归求值、未完成源、源异常或吞掉内层异常均封闭当前候选，不能发布半个姿态。finally 支持释放仍有内层 scope 未关闭的祖先求值，再通过 Cancel/新候选恢复。原 ALS 的空缓存定义仍可使用，保持既有合同。

新增 Core `AlsTransformAnimationAttribute`，把原 RootMotionDelta 的 Transform 属性混合算法迁到通用模块。显式 Present 独立于单位变换和权重，零权重唯一属性仍保持存在。保留 Override 选主、四元数同向化和 uniform/per-bone 混合不同的端点规则，复用 `AlsPrecisePose` 与 `AlsQuaternion`。`LyraRootMotionAttribute` 只负责资源策略、源提取和类型适配。

## 旧 Grounded 夹具修正

新增 ALS 旧 Grounded 专项回归时发现，它将当前 79 根逻辑骨的 reference pose 交给只接受 68 根模型骨的 `AlsLocalPoseClip`。当前程序集和此前通过审计的程序集都实际退出 1，报告相同的 `Invalid local pose sample`；这不是本批缓存算法回归。

修正 `MainGroundedPoseSmoke`，借用已经存在的 `library.MovementSources(...).Create(id)` 逻辑采样器，并以 `QuickFeetLogicalWeights` 比较逻辑骨混合。没有新写动画采样器，没有改生产动作/阈值或删除旧断言。修正后 30/60/120 Hz 共 1050 source 帧、420 blend 帧、102 interrupted 帧、同帧重试和热路径零分配通过。

旧 P4 的 300 帧直接场景仍在 AimOffset 正负 yaw 覆盖断言失败，当前/旧程序集日志逐字相同，数值为 yaw[-2.9728298,-0.95137215]、pitch[-0.3,0.3]。保持它的源码、断言和失败日志；验证脚本可显式选择 `als-demo`。当前生产 ALS 回归使用普通入口 `refactored_stance_demo_smoke`，没有用旧直接场景代替普通入口证据。

## 验证

Core 相关测试最终 206 项全部通过，包含新增 9 项 scope 身份、嵌套、递归、故障、取消恢复和原空布局兼容检查；既有 ALS 缓存与 Linked/Proxy 生命周期回归保留。Debug/ExportRelease 最终构建都为 0 错误、0 警告。

最终 Debug 11 个、实际 Optimize 10 个成功进程，共 21 个相关场景均实际退出 0，无 Godot ERROR/WARNING。Debug 复用 v3 六个及 v4 两个已经通过的场景，再以 v5 完成 Grounded 修正、普通十角色及渲染；此前 Core/Import 四文件与全部生产源保持相同，GodotALS 只额外修改旧 Grounded 夹具。Optimize 使用最终产物完整运行这十个场景。六份 Debug DLL/PDB 最终按 SHA256 恢复。

| 验证范围 | 每构建结果 |
| --- | --- |
| cache owner / 完整缓存姿态 | 原 90 行所有权检查及 1260 帧完整载荷检查通过 |
| RootMotion / Transform 属性 | 源图 3780 帧、完整姿态 3528 帧及原 72 个数据探针通过 |
| Slot 合成 | 47610 帧，完整 pose/curve/attribute/root，含取消和晚期故障 |
| 实际最终反馈 Main | 11340 帧、23442 owner 检查、621 提前缓存拒绝 |
| 最终 Main/Rig | 7560 帧、7296 姿态、188184 sweeps |
| 当前 ALS 普通入口 | 1700 帧、站蹲/空中/冲刺、3 Pivot 与 4 动态补步 |
| 既有 ALS Aim | 十 owner 各 single/parallel 6000 帧，10000 热帧零分配 |
| 修正后的旧 Grounded | 1050 source、420 blend、102 interrupted、零分配 |
| Lyra 普通十角色 | 各 4800 发布帧；Debug/Optimize 完整报告与此前基线逐值相同 |

最终 `scoped-cache-core-v5-audit.json` 为 passed：9 份最终源与其余 4606 份初始基线保持预期，870 JSON、710 原 UE 包和 9 配置哈希不变，原两进程 oracle 与本机源保护通过。该报告分别记录旧 P4 两次失败与 Grounded 修正前两次失败，没有将它们计入 21 个成功进程。

Debug 渲染的七张图来自 FramePostDraw，记录实际物理帧、Provider 和 epoch。汇总检查全部七张，并查看 Pistol Aiming 与 Rifle Crouching 原图：模型完整可见，武器、站蹲、移动、跳跃、落地及反向动作可见。它们是平地短轨迹抽查，不能关闭近景手掌接触、复杂地形或硬件键盘验收。

图像汇总：`artifacts/lyra-analysis/scoped-cache-core-v5-render-contact.png`；原图和帧记录：`scoped-cache-core-v5-debug-rendered-frames/`。

## 证据与复跑

- Core：`scoped-cache-core-v2-tests/scoped-cache-core-v2.trx`，206 通过。
- 最终构建：`scoped-cache-core-v5-build.log`、`scoped-cache-core-v5-optimize-build.log`。
- 首轮 Debug 六个已通过场景：`scoped-cache-core-v3-debug-verification.json`。同一批 Core/生产源继续使用，不重复运行无变化的场景。
- 当前 ALS 普通入口与 Aim：`scoped-cache-core-v4-debug-verification.json`；旧 Grounded 的首次失败也在其中保留。
- 最终 Debug 余项：`scoped-cache-core-v5-debug-verification.json`。
- 最终 Optimize：`scoped-cache-core-v5-optimize-verification.json`。
- 独立审计：`scoped-cache-core-v5-audit.log`、`scoped-cache-core-v5-audit.json`，passed。
- 旧程序集故障复现与恢复：`scoped-cache-core-v3-baseline-restore.json`、`scoped-cache-core-v4-grounded-baseline-restore.json`；六份程序集均按 SHA256 恢复。
- 冻结与最终审计：`tools/verify_scoped_cache_core.py`，资产均保持文件字节级哈希依赖。

```powershell
& ./scripts/verify-lyra-core-reuse.ps1 -Configuration Debug -EvidenceTag <new-tag> `
  -Cases @('cache-owner','root-motion','main-cache-pose','slot-composition',
           'main-pose-feedback','main-rig','als-ordinary','als-aim','als-grounded','ordinary-ten') -Render
```

运行使用当前 ignored `assets/generated/als_v4` 与 `assets/generated/lyra_als` 资源，只有代码的检出不能当成可运行资源交付。当前审计源快照为 9 份，保护初始其余 4606 份基线；首次快照与失败版本保留。

## 剩余工作

本批只关闭上述共享缓存控制、Transform 属性 Core 抽取和 Grounded 夹具适配范围。整数动画属性混合、带 flags 的曲线载荷和曲线覆盖算子仍留在 `LyraLayeredDataBlend` / 源 bank 中，是下一项通用 Core 抽取；其它 Godot/Lyra 通用算法仍需按当前源码审计。手枪/步枪近景、地形和实际键鼠验收继续开放。URO、完整原生绝对计数/调度、所有 Provider 和私有边界继续留在后续路线，不扩大当前验收。
