# Standing 方向缓存契约与 ChangeDirection 权重修正

日期：2026-09-11。完整性恢复第六十六批。

## 原始图与实际消费

新增 `AlsStandingDirectionCacheCompiler`，组合正式源图的引脚/属性和缓存导出的
compiled/property identity，编译 `(N) CycleBlending` 的八个缓存、27 个读取及
更新顺序。使用两个来源各自负责的数据，不假设旧缓存导出包含后来补齐的
所有动画节点属性。

| 缓存角色 | compiled index |
| --- | --- |
| Forward（含 Sprint/Mask） | 765 |
| Backward | 764 |
| Left Forward / Left Backward | 763 / 762 |
| Right Forward / Right Backward | 761 / 760 |
| 原始 Forward 输入 | 758 |
| Sprint 输入 | 757 |

更新顺序为 763、760、765、757、758、764、762、761。24 个状态读取以 Core
方向枚举顺序保存，每状态按 F/B/L/R 引脚排列。原生 baked 状态顺序 RF/RB 在
LF/LB 之前，不能把原生数组下标直接当作 Core 枚举。另有三个 Sprint 内部读取：
752、754 都指向原始 Forward，753 指向 Sprint，保留其别名身份。

编译器核对原生缓存 property 引用、姿势引脚、VelocityBlend 权重轴、MultiWay
标准混合模式、Forward/Sprint 依赖和读取闭包。实际 `AlsCyclePoseSampler`
已消费解析出的方向输入映射。未来缓存求值组件将复用同一契约。

最初怀疑来源编号累加与 F/B/L/R 顺序不同；展开六状态后，当前左侧来源都是
LF/LB、右侧都是 RF/RB，编号顺序实际上与引脚顺序一致。因此本批没有证明
旧排序造成了错误，也没有把保持不变的映射当作视觉修复成果。

## 实际权重修复与证据边界

Standing 的腿部过渡此前手算 `alpha*factor / (alpha*factor + (1-alpha)/factor)`，
缺少 UE `UBlendProfile::CalculateBoneWeight` 的最小权重钳制。现使用已经核对
原生实现的 `AlsGroundedPoseBlend.WeightFactor`，分别钳制、归一化入场和退场
贡献。`AlsStandingCyclePose` 使用这两个独立贡献进行累加，不用 `1-incoming`
重建退场权重；`AlsTransitionStack.Weight` 的骨骼权重路径同步使用同一规则。
未带 profile 的身体权重仍按普通 alpha 处理。

原生依据是本机引擎 `Engine/Source/Runtime/Engine/Private/Animation/BlendProfile.cpp`
的 WeightFactor 分支。本批只读源码，没有修改 UE 插件或重新导出资源。

检查历史 `AlsPoseBlendCommandlet.cpp` 发现，旧探针的 LegAlpha 本身也是手算
上述简化式。因此旧 64 个姿势运算案例证明的是 FTransform 累加、四元数符号
及归一化行为，不能反过来证明真正的 UBlendProfile 权重。旧导出不修改，测试
继续使用该探针实际传入的权重，并注明其范围；生产代码不再依赖该简化式。

新增边界测试在 alpha=0 保留活动过渡：非 profile 骨保持源姿势，profile 骨
保留约 0.00002 的目标贡献。放大平移差异后，该贡献可被明确检测；测试同时
检查姿势输出与状态骨骼贡献。这个受控测试不是实际移动效果验收。

## 验证

- Core 相关 58/58，最终常规 1928/1928；常规范围依旧排除 P5A golden / trace
  schema，不据此宣告两类通过。Import 新专项 7/7，最终全套 1178/1178。
- 编译器变异案例拒绝错误缓存别名、权重轴、输入角色、加法模式、依赖顺序及
  多余未消费读取；正式八缓存、27 读取及角色映射通过。
- Godot Standing 长回放通过：Sprint 1260 帧、Detail 1890 帧、Standing/Pivot
  各 5040 帧、来源时间检查 17337 次，九次换髋、371 个等待帧、九次中断回滚。
  三个 Cycle 来源重入/溢出重试保持通过，活动分配 0 B。
- Main 六缓存与旧入口各 1680 帧通过；六缓存入口 2360 次原始 Standing
  对照，既有来源溢出、Slot 故障、重试和活动零分配通过。
- Worker 单/并行各 180 帧、各十个来源事件，结果摘要均为 3FBC2FF66A74C4D6，
  完整姿势摘要均为 CF9225D4DE9B2C8B。权重修改改变了原生产结果，没有宣称
  旧摘要保持；线程一致性和并行晚期事件回滚通过，事件回调泄漏零。
- 最终优化 Godot 构建零警告、零错误。涉及文件空白检查通过。

TRX：`artifacts/test-results/standing-direction-contract/`。
Godot：`artifacts/standing-direction-contract-cycle.log`、`-main.log`、
`-main-legacy.log`、`-worker-single.log`、`-worker-parallel.log`、`-worker-rollback.log`
均使用相同 `standing-direction-contract` 前缀。

保留首次缓存契约 0/7 失败的 `cache-contract.trx`：把缓存旧导出直接传入 Sprint
属性编译器时缺少 child update mode。改为源图提供节点属性、缓存导出提供
编译身份，之后专项及全套通过。没有补猜测默认值或放宽已有属性校验。

## 剩余工作

本批不是八个 Save 的完整运行时求值缓存。仍需接内部 Initialize/CacheBones/
Evaluate、Forward/Sprint 嵌套读取与作用域，并完成首次 Update 前的精确
BlendSpace 空样本、控制/曲线缓存及姿势输出。当前普通 pose sampler 仍会
取样所有方向，编译契约不能替代实际缓存的生命周期。

之后继续 Main Movement、实际 Slot/Montage、最终惯性化、统一生产事务与
完整 Demo；最终曲线、上身、完整脚部及原 P5A-P7 全部保留，音频延后。
没有新完整 UE 动画图逐帧探针、移动截图、平台路线、短矩阵或十分钟性能验收。
滑步、交错步、上身与平台脚锁/性能问题仍未关闭。未改已确认键鼠，未提交、
回滚或合并工作区。
