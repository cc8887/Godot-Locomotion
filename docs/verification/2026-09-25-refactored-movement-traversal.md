# Movement / Direction 统一缓存遍历

在 `D:/GodotALS` 的 `main`，承接 `8658915`。普通 Demo 入口不变，用户已有修改保留。

## 本批实现

- `AlsRefactoredMovementTraversal` 将 Details 六个读者与方向子图26个读者放入一个 `AlsPoseCacheTraversal`，一次Drain，原编译顺序 `[67,133,136,138,137,132,134,135]`。
- 先从六个独立Movement上下文选最大weight（同权重保留先到），在67源更新时以这个上下文更新方向状态机、登记方向读者；Forward内部仍在原138位置登记137的两个独立读者。没有合并读者weight或串联两个独立Drain。
- Direction source保留原public独立入口及requester门禁，新增内部deferred入口供共享遍历；统一Drain完成前不能提交。Shared source-player合并保留24个原始播放身份，每个ID每轮最多一次。
- 保留root-motion权重、祖先state、Inactive、inertialization sync/requester/skipped handler；所有source tick继承其上下文的RequestedInertialization。Skipped消息按原回调顺序保存handler与完整context区间，包括空批次，等待惯性化消费者接收。
- 原始VelocityBlend输入从Details保留到Direction，分别按原节点归一化，避免对已经归一化的结果再归一化造成浮点改变。
- Movement初始化计数在候选内去重，给方向机器和Lean/PoseMoving owner提供初始化标志；Cancel不发布计数/播放器reset/Forward插值历史。共享更新失败可取消并重试。

## 原生回归纠正了一项错误推断

开始时根据SaveCachedPose.Initialize中的注释和条件，尝试记录缓存UpdateCounter并在离开后重置。旧原生移动对照立即出现两项失败：Standing30第51帧thumb_03_l旋转分量差2.0288746864216556e-6，Standing60第75帧pelvis差0.0035059580133215834。

继续读取本机 `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Animation/AnimNode_SaveCachedPose.cpp` 全部Update/PostGraphUpdate以及头文件，确认此UE版本只声明/读取该UpdateCounter，没有同步它；相关条件实际上不会因为正常Update变成已更新。已撤回这项推断性改动，保留真实初始化计数行为。没有修改参考资产、UE或误差预算。失败记录 `direction-regression.trx` 保留（14通过/2失败）。

## 验证

- 新4项。三Hz各6秒，共1260帧/99540骨：Details→同一cache遍历→共享真实Sync→真实方向pose→Lean/PoseMoving→Details pose。全部六Details状态、八缓存，零外层weight/零delta/零和非单位总量VelocityBlend、多个读者、惯性化tick和Skipped消息；每帧Cancel/retry的pose/curve/cache/source/context/message严格相同。
- 补充第一次初始化、正常连续帧不重置、实例reset撤销、重新初始化以及Forward非法输入造成中途失败后的原帧重试。
- `initial.trx` 新三Hz3通过；`final.trx` 24通过（新4、Direction source10、旧MovingNative6、Details pose4），位于 `artifacts/refactored-movement-traversal/`。
- 旧MovingNative六场景1680帧/132720骨全部恢复通过：time/weight差0，maxP7.097004612433011e-6 cm、Q1.1394327932567894e-8、S1.643672321582912e-7、curve2.3841858e-7。误差预算不变。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 0警告、0错误。

## 边界及下一步

这是以Details机器为入口的统一移动子图，不包含原外层cache66/Standing主状态机。新的整条1260帧测试没有新UE整链参考，不能将旧方向oracle延伸宣称为Details或全角色原生等价。无Godot场景/人工观感/全量测试/性能预算。

下一步：接119惯性化消费者（含本批Skipped消息），消费真实Parent/ResetPivot/SetHipsDirection，再新导出整个Movement Details连续参考。外层66/Standing及Crouching其余机器、update-only提交、Notify/rootmotion/统一角色宿主仍待实现，普通Demo未切换。Ragdoll/Get-up整体验收和最终十分钟预算未完成；音频、道具物理、头颈排查继续暂缓。
