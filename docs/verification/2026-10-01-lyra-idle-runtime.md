# Lyra Idle Provider 原机器、回调与自身曲线反馈

2026-10-03 修订：此前规则夹具没有设置 Main.CrouchStateChange，因此“两条显式 IdleStance 规则恒 false”的结论超出了输入覆盖。本次六布尔量 × 三 Provider 的192组原生规则矩阵确认，两条规则都读取 CrouchStateChange；真实 CMC 的站蹲轨迹已触发并验证。生产宿主已接 Main 的实际观察，并用独立缓冲保留嵌套机器混合的原叶姿态与曲线/属性/RootMotion。详见 [真实移动验证](2026-10-03-lyra-character-movement.md)。旧夹具、JSON及失败证据保留。

2026-10-01，在主目录实现并验证；Godot 4.7.2 .NET / 安装版 UE 5.8.1 / GASP58，沿用原 ALS 模型及 81 根逻辑姿态通道。本批关闭 **Idle Provider 组件**。完整 Main LocomotionSM、生产 Layer 实例、普通 Demo、人工观感和性能仍未完成。

## 原图执行与实例历史

原 FullBody_IdleState Root36 → IdleSM13，四状态为 Idle / TurnInPlaceRotation / TurnInPlaceRecovery / IdleBreak；Idle 内含 IdleStance15。实际闭包14节点、两机器、九条外层边、三条内层边、六个 StateResult 回调与五个 source occurrence，均绑定原编译索引和继承 CDO。

| source | 原节点 | 行为 |
| --- | --- | --- |
| Idle | 17 | Standing / Crouching / ADS 序列选择，变化请求0.2秒惯性 |
| StanceTransition | 19 | 独立播放器历史；本批旧夹具未提供 CrouchStateChange，没有可达播放覆盖；最新规则修订见文首 |
| Rotation | 24 | 非循环 Test / CanBeLeader evaluator，显式时间与内部采样时钟独立 |
| Recovery | 26 | 非循环 player，初始化从 TurnInPlaceAnimTime 开始，方向在相关时锁存 |
| IdleBreak | 28 | 原循环 player，按原 Idle_Breaks 数组顺序选择并推进索引 |

新增 LyraIdleMachineRuntime / LyraIdleLayerHost / LyraIdleResources。复用 Main 的原标准过渡栈，按真实旧权重、相关性、初始化和更新顺序执行回调；Initialize 不清除实例 NodeRelevancy。IdleBreak 延迟为 `6 + Trunc(abs(WorldLocation.X + WorldLocation.Y)) % 10`，没有另建随机数或限幅。相关自动边读取实例实际源时钟。

TurnYawWeight 来自本实例上次成功求值并提交的曲线；update-only 与隐藏时保留，取消不得发布。五条源的时钟、Marker、Delta、cached weight、ResetPending 与机器和字段共同候选化；外层登记所有活跃源后一次 Sync，再可选 Evaluate、统一预校验及提交。没有把 UE 期望时钟、反馈或姿态注入 Godot。

完整姿态包含81骨、曲线存在性/flags、整数属性和 typed RootMotion。补齐标准状态混合的 uniform 属性运算：共享整数属性分别乘权重截断后相加，共享 Transform 属性始终乘权重后合成；保留原 per-bone 算子。扩展压缩根绑定使用已经校验的实际 bank entries，包含五个 IdleBreak 扩展资源。

原图内部惯性边按原机器语义立即改变状态并向祖先请求惯性，本组件输出请求；祖先主图的真实惯性求值不在本次关闭范围。

## 编译规则与采集修订

旧 animlang 把 IdleBreak → Idle 的零时长规则记为 false。六种取消输入补验在开火出现首次分歧，保留 `idle-runtime-godot-firing-first-failure.log`。新增独立原生 ReadIdleRules，对三种装备各64组布尔输入直接调用当前 TransitionResult handler；192组证明 delegate33 / edge5 读取 GameplayTag_IsFiring，edge7 为六种取消条件的 OR。该旧矩阵中 IdleStance 两条显式规则始终返回 false，但未设置 CrouchStateChange，不能证明规则恒 false；最新补验已纠正。两次独立规则采集正常退出0、内容相同。Godot 按原退出顺序先选开火零时长边，再检查其他取消和自动剩余时间边。

零时长边在本帧仍按清理前的栈更新源，随后清栈并得到 Idle 权重1；没有将源更新权重改成最终状态权重。九次开火立即退出和45次其他取消淡出全部严格对照通过。

采集器运行真实临时 Main / ItemAnimLayers 分组实例、原属性访问 pre/post/worker 和实际 Idle 根，使用 transient ALS81 RAW 序列。IsAnyMontagePlaying 是函数，使用真实动态 Montage 活动而非伪造属性；WorldLocation 实际属于 Main，不能按旧 DSL 的 self 标签写到 Layer。初期 protected API、错误属性 owner 和根资源遗漏均修正，首次失败日志保留。

V1 JSON 数字不能保留 double 的负零；保留 V1 三文件及其探针源码归档，V2 独立捕获五个 double 字段的十六进制位模式。没有修改运行时符号算法来迁就 JSON，主轨迹264600项前后字段比较包含364个负零。精度、属性截断与近全权重混合的早期失败也保留。

## 最终验证

| 轨迹 | 帧 | 姿态 / 骨比较 | 时钟观察 | 原边 |
| --- | --- | --- | --- | --- |
| 三Provider × 三Hz × 42秒 | 26460 | 26268 / 2127708 | 132300 | 0,1,2,3,4,6,8 |
| 六种取消 × 三Provider × 三Hz × 7秒 | 26460 | 3780 / 306180 | 132300 | 0,5,7 |
| 合计 | 52920 | 30048 / 2433888 | 264600 | 全九条外层边 |

原门槛为位置1e-8 cm、quaternion1e-10、scale1e-12，未放宽；主轨迹最大位置差1.1588601317882371e-13 cm、quaternion6.579526241045213e-16、scale0，取消轨迹分别6.394884621840902e-14 / 6.332287220439566e-16 / 0。时钟、字段、机器 elapsed / 当前与上一权重 / 栈 alpha、初始化与更新上下文逐位同；22828曲线、120192整数属性和所有 RootMotion 存在性/身份/值通过。主轨迹 RootMotion present11414 / absent14854，包含两种分支。

主轨迹四状态、521次初始化、787惯性请求、21次进入Break、96隐藏/159父inactive/96 update-only、逐帧取消重试和26364异代拒绝。取消轨迹162初始化、84请求、54次进入Break、22680 update-only和26460异代拒绝。五条源均观察，四条真正活跃；31个绑定资源中主轨迹实际20个、取消轨迹11个，不能称31个均已播放。每个取消输入在三装备三频率各覆盖一次。

V2主轨迹与取消轨迹各两次真实UE采集均退出0且 canonical JSON 内容/字节保持；四日志各有799条既有警告。两次编译规则探针各744条既有警告。最终没有新 Python Error、ensure/assert/Fatal；Godot 最终无 ERROR/WARNING。最新外部插件完整42 actions构建成功，Debug / ExportRelease Optimize 均0警告0错误。

相关回归已通过：Main连续机器7560帧、既有姿态栈840帧、四根共同宿主3780帧/8400姿态/680400骨、Air3780帧/7650姿态/619650骨。最终 Idle 修订只改变该组件 edge5 条件及覆盖断言，最终构建和完整两套 Idle 场景再次通过；共同算子回归来自该算子最终代码版本。

最终 `tools/verify_lyra_idle_runtime.py` 通过资源依赖、探针源一致性、192编译规则、六类取消、日志和构建审计，汇总 `artifacts/lyra-analysis/idle-runtime-final-verification.json`。独立规则探针进一步保护508原包与当前全部642份JSON；未保存UE资产、修改引擎或GASP58源码/配置。本批未新增全量测试、渲染、人工或十分钟性能验收。

## 资源与复跑

V1作为历史保留；当前V2和取消六份 ignored JSON 位于 assets/generated/lyra_als，不能格式化：

| 文件 | SHA256 |
| --- | --- |
| idle_runtime_v2_requests.json | 3dff67f64faa9bffc45da521c0e281e0a09bd2ccdb615e211ed60e37853745a3 |
| idle_runtime_v2_roots.json | 95896d0b2ba1475271e2f5a001196351973a2b307340de66270a5efb2fb0e1d7 |
| idle_runtime_v2_native.json | 134a5df854a2424f609f8b8b94c2072b26b074e4768eb9c591971968c3fa775b |
| idle_runtime_gates_requests.json | ad8e90689e82876686bd7cc16726a51796c4eee7ef3eb7a25a9646b4f356049c |
| idle_runtime_gates_roots.json | 2c2b344b81319a722c3c0d6984f7b3b96bcc5c60d3e21104dea0ab321735a550 |
| idle_runtime_gates_native.json | a5d75e8d209122aba1677b2168ad5078ce18c1c4ed0402f0d1ad1b6c6012f23b |

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-idle-runtime.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-idle-runtime.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject' -CaseSet gates
.\scripts\export-lyra-idle-rules.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . scenes/tests/lyra_idle_runtime_smoke.tscn
python tools/verify_lyra_idle_runtime.py
```

审计要求文中两次采集和最终构建/回归日志；包装脚本可用 LogName 保存独立重复日志。构建仍从 .. 调用可用SDK，不改项目 global.json；纯代码检出没有 ignored 资源时不能运行。组件仍消费外部 Main 字段与根访问；下一步统一地面/Air/Idle资源地址、生命周期和共同Source Scope，接 Main自主选边/最终混合与原惯性，再统一Notify/Montage、实际Layer接口、后处理和普通入口。全部ALS R2–R7与用户暂缓项保留。
