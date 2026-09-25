# Movement Details 外层惯性化

工作目录 `D:/GodotALS` / `main`，承接 `895593d`。本批仍不切换普通 Demo，用户已有修改保留。

## 实现

- 原119 Inertialization →117 Details节点绑定，compiled/authored链接交叉校验；无blend profile、无bone/curve过滤、ResetOnBecomingRelevant/ForwardSkipped=true、Tag=None及无额外回调。
- `AlsRefactoredMovementInertialization` 消费同帧Details请求与统一Traversal的skipped批次。保留请求metadata/去重，Core按最短duration处理；同节点回送为AddUnique空操作，其他目标生成候选ForwardedRequests，供以后外层宿主接收，不能当成已经跨节点投递完成。
- 完整候选Update/Evaluate/Commit/Cancel；update-only允许提交并保留请求/累计delta，不创建pose历史。首次无历史请求由Evaluate丢弃；counter间断按原119节点清请求/历史，保留此前update-only累计delta；显式Initialize清全部。
- 重复Evaluate从相同Update候选重算，不重复推进历史；失败阻止提交，Cancel后可同帧重试。组件transform、attach parent和teleport进入实际求值。

接入时发现原Core只保存双精度旋转，位置和scale仍走单精度。当前UE `FInertialPoseSnapshot` 使用FVector/FQuat，差分存储才降为FVector3f/float。因此新增 `AlsInertialization.EvaluatePrecisePose`：完整double TRS/component/history，原生明确float差分轴/幅值/速度边界，double叠加，原curve差分表达式、deficit/中断/teleport/根骨参考系规则。旧单精度和仅double rotation入口保持兼容，精度模式切换需Reset，CopyFrom包含完整历史。

原生依据为本地Engine的 `AnimNode_Inertialization.cpp/.h` 与 `AnimInertializationRequest.h`，本批无UE源码/插件修改、构建或新导出。

## 验证

- 新Core4项：无惯性化时double位置/scale逐值透传；大位置下小增量不被float吞掉，float差分作用到double基值；400帧中断/附件/teleport、CopyFrom/retry及模式门禁；已有原生15轨迹900帧完整pose对照。
- 原生900帧/3600骨：max位置=0 cm、scale=0、curve=0，旋转quaternion向量距离 `1.0623285032658512e-6`。沿用旧预算位置/scale/curve3e-5、旋转1e-4，没有放宽；不能宣称旋转逐位一致。
- Core相关21通过，`core-native.trx`；早期不含native新增case的`core.trx`20通过。
- 原三Hz1260帧真实Movement/Details测试接上119后的求值，确认惯性化实际active、有skipped回送尝试、所有pose/curve在逐帧Cancel/retry后严格相同。
- 新Import生命周期case覆盖update-only请求跨帧保留、同帧取消、Evaluate失败提交门禁、counter间断后无旧历史混入。
- Import相关18通过（Movement traversal5、Details pose4、Overlay pose runtime9），`final.trx`；补authored连线门禁及pending计数后最终目标复跑见`authored-final.trx`。
- Godot Optimize构建0警告、0错误。

初次编译因Core.Math命名空间遮蔽System.Math、以及模式匹配局部变量request重名失败；改为ScalarMath别名/局部名incoming后编译通过。失败发生在测试启动前，无对应TRX；工具输出保留。没有运行时测试失败或容差变更。

## 未完成

旧900帧参考验证惯性化算法，不是本批Refactored整条Movement Details图的新UE oracle；1260帧整链仍由受控Parent输入驱动。无Godot普通场景、录像截图、全量测试或性能预算验收。

下一步真实Parent/ResetPivot/SetHipsDirection消费，以及整个Details→119原生连续导出/对照。外层cache66/Standing主机器、Crouching其余机器和外节点转发接收仍未接；Movement Lean evaluator自己的update-only提交仍待补。最终共享角色宿主、Notify/rootmotion、Ragdoll/Get-up/Pose Recovery及十分钟预算未完成。音频、道具物理、头颈排查仍暂缓。
