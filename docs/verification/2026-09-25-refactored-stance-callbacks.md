# 原版站立／蹲伏回调绑定与生命周期

直接在主目录 main 完成 Standing 15、Crouching 12 个 CallFunction 节点的资源绑定。使用完整嵌套路径解析内部函数模板和外部动画节点，校验具体函数、self 目标、无连接参数、调用阶段、附加回调和原始 Source 双向连线及 property ID。包括六向 SetHipsDirection、两次 ResetPivot、Grounded/Standing/Crouching 更新和 Rotate/Turn/DynamicTransitions 回调。

特别注意：内层 SetHipsDirection 模板的参数全部是 Forward，实际六向值位于外层动画节点 NewHipsDirection 引脚。UE `Editor/AnimGraph/Private/AnimGraphNode_CallFunction.cpp` 的 ExpandNode 使用外层引脚 CopyPersistentDataFromOldPin。首轮两个测试暴露错误读取内层模板的问题，已修正，无改动资产。

新增独立候选生命周期：Prepare → Enter → 调用方更新候选 Parent 并递归 Source → Leave → Commit/Cancel。所有当前原图回调均在 Source.Update 前触发，Leave 后才同步节点遍历计数；OnBecomeRelevant 使用已移植的 WasSynchronizedCounter，支持 signed counter 回绕、同一计数多次访问、跳过遍历后重入，不把渲染帧间隔当成失活。initializeInstance 明确对应 OnInitializeAnimInstance 的计数清空，不等同普通节点 Initialize。未执行 Parent 方法体，也没有代替完整状态图遍历或对外派发通知；宿主必须协调 Parent 与此候选一起取消/提交。

原生依据：`Runtime/AnimGraphRuntime/Private/AnimNodes/AnimNode_CallFunction.cpp` 和 `Runtime/Engine/Public/Animation/AnimTypes.h`；ALS LinkedAnimationInstance 将函数转发 Parent，SetHipsDirection 写 GroundedState，ResetPivot 清 StandingState.bPivotActive。

## 验证

- 初次编译误用 private Require，增加本地检查后修正。首次 `bindings.trx` 两项六向断言失败，证明内部模板不能作为实际参数；保留失败记录。
- `artifacts/refactored-stance-callbacks/outer-pins.trx` 两项绑定通过。
- 最终 `related.trx` 15 项通过：新增四项，加此前移动五项、旋转六项。含 27 个原图绑定、八种政策/函数/参数/源变异拒绝，两个 stance 的计数回绕/间断/同计数重访/实例初始化、嵌套返回顺序、候选取消重试和故障后恢复。
- Godot 优化构建通过，0 warning / 0 error。无本批 UE 连续 callback oracle、UE/Godot 场景、全量测试、十分钟性能或打包。

## 下一步与边界

继续实际 stance 状态条件与状态机、缓存姿态及 pose 图，把此回调生命周期接入图遍历并执行实际 Parent 更新，再接统一角色宿主。不能据本批声称换髋延迟或普通 Demo 已修复；普通 Demo 尚未切完整 Refactored 链。Ragdoll/Get-up/Pose Recovery、Mantle、Camera、性能等所有旧缺口仍在目标中。用户改动保留，音频／道具物理／头颈诊断仍暂缓。
