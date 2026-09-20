# 地面缓存接入完整主移动来源绑定

日期：2026-09-12。完整性修复第七十八批。

## 修改与边界

地面缓存现在可与空中、落地组件使用同一份完整来源绑定：75 个播放器、109 个
样本。`AlsLocomotionGraphBuilder.CompileSourceBindings` 接受调用者提供的正式
来源配置，并检查动画定义和骨架身份；图、资源库、地面来源收集器及姿势求值器
使用同一个配置。未指定配置的旧调用保留原地面范围，不擅自迁移已提交运行历史。

`MainGroundedUpdateSmoke` 移除固定 82 项采样时间数组，按实际绑定分配，并增加
`--movement-bindings` 对照模式。两套独立 owner 在相同输入下依次运行；旧 owner
释放后才构建完整绑定 owner，不共享姿势缓存或已提交历史。

对照发现 `AlsCycleDetailGraph` 原来从整个来源表收集地面缓存的曲线名。追加
未访问的空中动画后，地面曲线从 13 项变成 15 项，改变缓存布局。已将该局部
payload 限定为地面六类来源和既有 Turn 动画；完整主图之后需按名称合并各子图
曲线。这个修正不等于所有 Grounded 分支都已保留缺失曲线的 presence 语义。

本批没有把完整 Main Movement 接入 Demo。外层主状态机、各子图共同更新、
移动落地读取真实 Grounded 缓存、最终 Slot/Montage、惯性化与提交仍待统一。
当前新增对照只更新地面，刻意验证未访问的十九个空中/落地来源不被推进。

## 实测

优化构建零警告、零错误。新模式在 30/60/120 Hz 各执行两份回放，总计 3360
个回放帧、1680 个逐帧配对；每份均包含原有初始化失败、Slot 失败、候选重试、
零全局权重、非活动上下文、六个缓存及骨骼缓存计数变化。

| 检查 | 结果 |
| --- | --- |
| 地面状态、最终局部骨骼、缓存曲线、BasePose_CLF | 1680 配对逐项精确一致 |
| player/sample 时间、marker 历史、epoch、缓存权重 | 精确一致 |
| 原地面同步组、通知 tick、事件身份/载荷/顺序 | 精确一致，旧/新各 36 个事件 |
| 新增来源 56–74 | 未访问时 epoch、时间、权重均为零 |
| 绑定身份 | 完整绑定的 source、binding、layout 摘要均不同于旧绑定 |
| 旧历史误送入新绑定 | 每种帧率首次调用均拒绝；未伪造摘要进行迁移 |
| 每种绑定、每种帧率的两处热路径采样 | 共 12 处、每处重复 120 次，Prepare 分配为零 |
| 六缓存与重复读取 | 原有逐骨骼/曲线一致性和失败恢复检查通过 |

基线存储和配对检查在热路径分配测量之外；不把整个测试进程称为零分配。
也未以此替代 10 角色、十分钟 Release 性能预算。

日志：`artifacts/main-movement-binding-parity-final.log`。

## 首错与回归

第一次对照失败，补充诊断后确认 30 Hz 第一帧骨骼相同、状态相同，但曲线
长度为 15/13。保留 `main-movement-binding-parity.log` 和
`main-movement-binding-parity-diagnostic.log`；修正局部曲线范围后全部配对通过，
未放宽逐项比较，也未把新曲线强制补零。

- 空中真实来源回放：3360 帧、18 事件、18 次重入、674 帧冻结、1266 帧双
  Lean、12 次失败重试和 6 项接口拒绝通过。
- Worker single/parallel 各 180 帧、10 来源事件。结果摘要
  `21E164D829153157`、完整姿势 `CF9225D4DE9B2C8B` 保持。
- 并行晚期来源事件失败：候选状态/姿势/整帧回滚通过，无 gameplay 回调泄漏。
- Standing 全部回归通过：换髋等待 371 帧、9 次换髋转换、9 次中断回滚；
  Detail 1890 帧、Standing/Pivot 各 5040 帧，实际来源和活动姿势零分配检查通过。
  这些仍是自动回放，不作为实际角色视觉问题已经解决的证据。
- 回归日志前缀：`artifacts/movement-binding-`。

运行新对照：在仓库设置 `DOTNET_TieredCompilation=0` 和
`COMPlus_TieredCompilation=0`，成功执行
`dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 后，使用 Godot 控制台
可执行文件启动
`--headless --path . scenes/tests/main_grounded_update_smoke.tscn -- --movement-bindings`。
工作区 diff 检查与本批新增/编辑未跟踪代码、验证文档的行尾空白检查通过。

本批修改 Godot 接线与测试，没有修改 Core/Import 算法，未重跑其完整套件；
没有启动 UE、重新导出资源、人工移动截图、commit、revert 或合并分支。

## 下一项

完整来源表与地面缓存已兼容，下一步将地面 Update/Evaluate 的候选所有权从
回放夹具抽出，接主移动状态机实际的初始化/更新顺序，再把空中与落地收集器
加入同一个共享 tick。移动落地必须求真实 Grounded 缓存，不能继续用参考姿势。
随后组合主状态姿势、Slot/Montage、最终惯性化与 Demo 提交。

最终 YawOffset 朝向、动态上身 Layering/Add/LS、完整脚部约束和平台门禁仍未
完成；起步滑步、交错步、换髋等待和双臂姿态保持待验收。原 P5A–P7 全范围
继续，全部 Overlay/道具保留，音频暂缓。
