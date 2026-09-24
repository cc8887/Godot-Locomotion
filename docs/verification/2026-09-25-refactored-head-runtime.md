# Head 六节点绑定与候选运行时

新增 `AlsRefactoredHeadGraphCompiler`，绑定实际编译清单中的六节点：Root→ApplyMeshSpaceAdditive，Base为View Input，Additive为InitializeHead(OnBecomeRelevant)→RefreshHead(OnUpdate)→Look evaluator。运行时linkId/sourceLinkId与编辑图实际连线交叉检查；验证原类、编译索引、回调位置/目标、三条属性绑定、alpha/LOD/mesh空间及teleport/no-sync策略，拒绝额外属性绑定或未编译的动态引脚表达式。Profile绑定实际Head设置与Look资源，可创建各角色独立运行时。

`AlsRefactoredHeadRuntime` 接收父级已更新View状态；上游base图先Update，再Prepare Head。只在图被访问且HeadBlendAmount相关时调用Head。重新相关通过共享AlsGraphTraversalCounter.WasSynchronizedCounter判定；图初始化计数变化使下一次相关访问重新Initialize。隐藏期间保留头部状态，恢复按原生顺序先Initialize再Refresh。

Evaluate独立于状态更新，用本角色Look sampler按Head Pitch/YawAmount求值，再执行共享MeshApply。Look无曲线，基础曲线及presence原样传递。重复Evaluate不推进Head，故障后候选不能提交；宿主必须Cancel后重试。角色/代际/帧身份校验、显式ValidateCommit/Commit/Cancel沿用项目候选所有权模式；外层仍需把父状态、base图与Head一起提交。

## 验证

- 实际设置、Look原始源与Stand基础姿态：30/60/120 Hz各三秒，共630提交帧。覆盖alpha隐藏、整个Head图未访问、恢复、半权重叠加及图重初始化。
- 每个可求值帧重复Evaluate及Cancel后重新Prepare，姿态、候选Head/遍历状态一致，三条基础曲线含absent/present-zero完整保留。每频率4次初始化（首次、两次恢复、一次重初始化）。
- 每频率注入一次真实Look采样后的异常：不能提交、已提交状态不变、Cancel后同帧恢复成功。陈旧帧拒绝。
- 五个资源变异拒绝：编译连线、callback位置、teleport、root-space模式、alpha缩放。
- 新增8项。Import Head/Look/Layer定向54通过，补动态绑定拒绝后Head8项复跑通过（重叠）；Optimize构建0warning/0error。
- 首次完整图绑定3个频率均失败：误将authoredProperties中未编译的linkId=-1当运行时链接。改为runtime链接与编辑图FollowReroutes交叉检查，随后全部通过。失败TRX保留。
- 证据：`artifacts/refactored-head-runtime/`。没有新UE连续状态/整图oracle、Godot场景、全量测试或视觉验收。

本批是Head子链路的资源绑定/执行与事务验证，不是普通Demo切换。下一步补UE连续View/Spine/Head及Head整图运行对照，再连上完整Refactored宿主。此前Look35组原生采样证明仅覆盖静态姿态，不能代替本批连续状态oracle。Mantle/恢复、物理稳定性、Flail、最终预算等旧缺口及用户暂缓项保持。
