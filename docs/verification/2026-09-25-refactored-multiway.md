# Refactored MultiWay 算子

在 `.`、`main` 承接原生 94 节点清单。新增共享运行时节点 `NormalizedMultiWayBlend`，对应实际 Layering MultiWay 的默认配置：`bNormalizeAlpha=true`、`bAdditiveNode=false`、AlphaScaleBias=(1,0)。不冒充支持其他配置；完整图编译器接入时必须校验这三个设置。

## 原生依据及实现

核对本地 UE `AnimNode_MultiWayBlend.cpp` 的 UpdateCachedAlphas/Update/Evaluate，以及 `AnimationRuntime.cpp` 的 BlendPosesTogether、既有 AlsPoseBlender/曲线运算：

- float 顺序累加原始 desired alphas，再以总量的 clamp01 判断相关性；总量相关时，对每项 desired/total 执行 clamp01。不能先 clamp 每项再求和。
- 权重大于 1e-5 才更新和求值对应 pose link；传递分支权重，不重新归一化被阈值过滤后的剩余分支。
- 按输入顺序加权累加姿态和曲线，保留曲线存在性、四元数最短方向累加；多个源按 BlendPosesTogether 归一化旋转，随后按 MultiWay 再归一化一次。
- 无有效分支时恢复参考姿态并清曲线。初始化/CacheBones 遍历全部输入，更新/求值只遍历相关分支。复用原候选状态、缓存和丢弃/提交机制。
- 非有限总权重在 Prepare 阶段拒绝，避免把溢出传播至姿态；这是输入防御边界，不声称与 UE 对无效数值的处理一致。

## 验证

- Core Release `FullyQualifiedName~AlsRefactoredLayer`：19 通过。新增 8 组权重边界和 1 个嵌套三输入用例；覆盖零值、普通比例、大于1、负权重抵消、阈值相等、小分支过滤后剩余权重小于1、曲线 presence、每条输入链各自更新、同姿反号四元数、每组丢弃后重试。
- Import Release `AlsLayerBlending|AlsRefactoredLayering|AlsLayeringInput`：73 通过、1 个既有条件跳过。`NormalEditorRepeatsTheConsumedGraphAndInputSemantics` 需要 `ALS_LAYERING_REPEAT_FILE`。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning、0 error。
- 记录 `artifacts/refactored-multiway/core.trx`、`core-final.trx`、`import.trx`。本批无失败测试；后补嵌套用例后重跑 Core。`git diff --check` 无空白错误。

这是基于原生源码和实际节点默认值的算子实现，不是新 UE 运行 oracle。没有修改 UE 插件、重新导出资产、执行全量测试或新 Godot 场景验收。真实站立/蹲姿 SequenceEvaluator 采样、PoseState 绑定、94 节点完整骨骼图、Refactored VB/Slot/Head 与普通 Mantle 宿主仍须接通。

用户 project.godot/P4 规划/头颈诊断/uid 修改保持未纳入提交。旧物理稳定性 9/12、Flail 0/3、复杂相机碰撞和最终十分钟性能预算未关闭；头颈拉伸、道具物理、音频维持暂缓。
