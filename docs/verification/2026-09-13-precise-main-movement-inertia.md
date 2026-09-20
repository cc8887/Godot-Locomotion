# 正式主移动精确姿态链与 BaseLayer 惯性历史

第一百五十三批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。
承接第 151 批精度隔离与第 152 批 Cycle 精确采样。本批真实 Godot
主移动输出接入精确惯性化，横移/Sprint 共 3780 帧的严格最终姿态对照
全部通过；不是给游戏灌入 UE 导出的姿态，也没有放宽比较门槛。

## 接入的实际路径

- Detail 四路附加采样/混合、Standing/Stop 状态叠加、Plant 固定采样
  与组件旋转空间腿部分层、转身 Slot 保留实际 double 姿态。
- Grounded 的候选/已提交共享缓存切为精确缓冲区。所有缓存生产者和
  消费者同步接入，包括蹲姿方向缓存、Stride、对角缩放、Lean、Stop、
  Main Grounded 的参考/绝对来源、QuickFeet 和真实 Grounded Montage。
- Jump 内部状态、Fall/Jump 的预测落地和 Lean、Landing 的附加混合，
  以及 MainMovement 外层过渡保留精确姿态至 BaseLayer。
- BaseLayer 使用既有 `AlsInertialization.EvaluatePrecise`，从真实
  主移动姿态提取 double 旋转，保留候选复制、两帧历史及应用输出；
  然后进入精确 BaseLayer Action Slot。Slot 输出不写回惯性历史。
- 旧 float API 保留为明确的兼容/诊断消费入口。正式 BaseLayer 已走
  精确重载；Grounded 逻辑缓存不经过这些投影。来源时间、epoch、事件、
  更新顺序及提交所有者没有另建一份。

位置/缩放仍经过既有惯性化的单精度输入与输出边界，组件世界变换来自
Godot 的 float 输入；BaseLayer 之后的部分分层/IK 也仍是 float。
本批证明所测轨迹符合原严格门槛，不能称所有 TRS、任意世界尺度、全部
姿态分支都与 UE 逐位相等。兼容接口和预分配精确缓冲区增加了常驻内存，
最后的十角色/十分钟预算仍需实测。

## UE 同输入严格验收

门槛保持：位置 .001 cm、旋转 .02°、缩放 1e-5、曲线 1e-4。
两组输入都因实际姿态产生的脚锁位置变化重新导出，未复用旧请求。

| 正式路径 | 失败帧/总帧 | 最大位置差 cm | 最大旋转差 ° |
| --- | --- | --- | --- |
| 横移 MainMovement | 0/1260 | .0000198769 | .0000163782 |
| 横移 BaseLayer | 0/1260 | .0000198769 | .0000163782 |
| 横移最终根 | 0/1260 | .0000744546 | .0004114587 |
| Sprint MainMovement | 0/2520 | .0000216616 | .0000167305 |
| Sprint BaseLayer | 0/2520 | .0000216616 | .0000167305 |
| Sprint 最终根 | 0/2520 | .0000816937 | .0003475603 |

每组覆盖 30/60/120 Hz 和两侧视角映射。曲线存在性和数值全部通过，
最大曲线差 2.384e-7。第 152 批横移最终根 27 帧、Sprint 54 帧失败，
本批均关闭；此前横移 BaseLayer 21 帧失败也关闭。

请求 SHA256：

- 横移 `42F1604CD38496449C85079F4A0AD1DC3BFB4C697F839783B3F4EF2BE14F83CC`。
- Sprint `B77EC26D520F5FCE3E8DE5531E02B5B0E2EB416175C2620624F8525A732CB12D`。

有效配对为 `artifacts/full-graph-godot-153-final.json` 与
`full-graph-ue-153.json`；`full-graph-godot-153-sprint.json` 与
`full-graph-ue-153-sprint.json`。六份报告为
`full-graph-parity-153-{main,base,final}.json` 和
`full-graph-parity-153-sprint-{main,base,final}.json`，全部通过。

`full-graph-sources-153.json` 与 `full-graph-sources-153-sprint.json`
分别验证 4252、9016 次 Godot 实际 tick：时间差 0，最大缓存权重差
2.98e-7、2.38e-7，无失败。该来源检查不证明完整 UE 节点访问集合。

这些是受控动画属性输入的原生 AnimBP 配对，不是两引擎 CharacterMovement
或实际地面/移动平台物理解算的配对。蹲姿、空中、落地已有生产回归，
本批没有为它们增加完整原生逐帧场景，因此不能扩大 UE 验收范围。

## 构建、事务和渲染

- 优化 Debug 构建成功，0 警告、0 错误；本批相关空白检查通过。
- Core 既有相关专项 67 项通过；新增精确姿态专项 3 项通过；Import
  相关专项 130 项通过。TRX 分别为 `precise-grounded-core-153.trx`、
  `precise-grounded-native-153.trx`、`precise-main-import-153.trx`。
- 新测试用原始 double 数据验证全部 80 组 UE 组件旋转分层样本，
  同时检查 Detail 的小于 float 精度的位移、原地输出，以及 Plant/
  Stride 热身后万次求值零分配。未只测试新增 API 能被调用。
- 横移/Sprint 3780 帧每帧丢弃和重试通过，姿态/曲线/来源/事件及
  Sprint 提交状态一致。
- `precise-base-layer-frame-153-final.log`：3360 帧，271 帧隐藏、
  3 次冷隐藏、15 次重入、6 次晚期失败和 12 项守卫通过。
- `precise-base-actions-153.log`：真实 Roll Slot 1050 帧及 1050 次
  重试，354 帧完整 Roll 姿态、372 帧来源隐藏、28 次晚期失败通过。
  此项仍不包含 Roll 根运动和 gameplay 实现。
- `precise-grounded-runtime-153-final.log`：30/60/120 Hz 共 1680 帧，
  运行时与集成图姿态/曲线/来源严格一致；2356 次直接精确 Standing
  求值与缓存输出比较通过。来源交换、初始化溢出、Slot 晚期失败、
  骨骼缓存变化与重入通过，6 次运行时分配检查均为 0。
- 单/并行生产各 960 帧通过，事件 39、脚锁 316 帧、偏移 910 帧，
  lag/stale 为 0。新结果摘要 `F6021FE2E30E494D`，完整姿态
  `81FD4BE13734881C`，采样姿态 `E5F771206045421A`，根
  `DB5B813964D3479C`，两模式一致。日志
  `precise-main-production-{single,parallel}-153-final.log`。
- 720 帧实际渲染、120 张截图及分析脚本完成，已查看连续姿态接触表。
  图像在 `artifacts/precise-main-visual-153/movement-contact-sheet.png`。
  最大相邻帧脚旋转 14.117°，不是接触支撑或无滑步判据。本轮微小
  数值误差修正不能仅凭截图宣称肉眼显著改善。

初次捕获暴露 Action Slot 错误使用骨架 ID 0，已改为绑定动画的实际
骨架；首个 `full-graph-godot-153.log` 保留。新精确测试首次编译的
`Math` 命名空间冲突已修正。BaseLayer 晚期失败测试的新重载漏接
故障开关已修正，首个失败日志保留。生产首次旧 golden 失败也保留，
更新后两种模式重新完整验证。

额外 Grounded 测试先暴露了旧守卫将渲染骨骼数与含虚拟骨骼的逻辑
骨骼数混为一谈，现分别核对绑定定义的两个数量。随后旧 float 原始
求值不能再作为精确缓存的逐位真值，已补齐无外层缓存的直接精确求值，
包含 Cycle 尾部与按实际访问状态采样，继续要求姿态/曲线完全相等，
没有删除比较或放宽误差。两次失败日志分别保留在
`precise-grounded-runtime-153.log` 和 `precise-grounded-runtime-153-mapping.log`。
直接求值是缓存透明性检查，独立原生姿态真值来自上述 UE 配对。

## UE 构建与重复导出

按 `ue-diagnosing-plugin-build-load` 完成完整 Editor 目标构建（0 个
待编译 action）与三项项目插件审计。日志前缀：
`../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T051913980Z-ccd9b60ec85745f88616671d0c98c776`。
fingerprint 为 `CC596A2A3B406B414C02E0152572EE86D4CD52967404B31E335F61E2C70C2B7A`。
本批无原生插件、配置或加载修改，没有复制 DLL 或修改 BuildId。

两次冷导出均退出 0、0 error/0 warning、0 资产保存，日志为
`artifacts/unreal/full-graph-ue-153.log` 和 `full-graph-ue-153-sprint.log`。
普通 Editor 重启导出横移 1260 帧后退出 0，生成的
`full-graph-editor-153.json` 与冷导出全部 JSON（包括两个阶段）递归
严格相等。普通 Editor 日志仍含两条既有 AutomationTest Condition
failed，不能称其全日志无错误。相同原生二进制的 DataValidation 和
隔离 BuildPlugin 沿用第 151 批证据，本批未重复。
技能引用的 superpowers 验证技能在本机未找到，采用实际构建、进程
退出码、原生 JSON 配对和回归日志作证据。

## 下一步与原计划边界

已关闭本组受控横移/Sprint 的严格姿态差距。下一步转向实际 Motor
同输入、不同步态相位的 A→D/D→A 反向、起步支撑接触、移动/旋转平台，
并扩展原生配对到蹲姿和空中/落地。完成这些及人工键鼠/视觉验收后，
再启用默认完整根入口；默认目前仍 BaseLayer 75/109，完整根为 224/258。

P3/P4 整体验收、P5A 剩余通用事件/动作、P5B Overlay/道具玩法、
P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/恢复/完整 Camera、
P7 十分钟预算均未完成。既有 Core 23 项失败、Import 分配不稳定与
旧 p95 超预算未关闭，音频暂缓。本批未 commit、revert 或合并。
