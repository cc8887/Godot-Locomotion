# Lyra 十个 Locomotion 根的资源与 Source 宿主

2026-10-01，直接在主目录实现并验证，Godot 4.7.2 .NET。继续使用 ALS 模型和原68蒙皮骨；所有根求值完整81逻辑骨。关闭范围是**统一资源地址空间、十根 Source owner 和受控联合生命周期**。完整 Main 状态选择、最终混合、生产 Layer 和普通 Demo 保持开放。本批没有启动、修改或重新采集 UE。

## 资源和实例

`build_lyra_locomotion_resources.py` 从既有真实地面/Air/Idle捕获构造 `assets/generated/lyra_als/locomotion_resources.json`，大小12,507,382字节。重复资产的源长度、rateScale、Marker和压缩Root载荷必须完全一致；三Provider的继承CDO资源绑定逐轨迹相同。所有原642份JSON逐字节SHA256保护，未格式化或覆盖既有资产。

共同地址空间有194条绝对Sequence和3条Main Lean，共197条，三个同步组为Locomotion / Stop / Test。Test属于Idle转身evaluator，不能将其合入Locomotion；`ItemAnimLayers` 是实例组，与同步组用途不同。源身份仍为角色playerBase加原节点索引，Lean有独立范围，初始化代际由角色持有。

新增 `LyraLocomotionResourceCatalog / LyraLocomotionResources`。运行时加载紧凑清单、源/曲线/压缩Root数据和CDO绑定，不解析数百MB的原生期望姿态文件。不可变定义可共享；每次Create独立分配四地面、Idle两机器/五源和五Air根的机器、时钟、回调及Warp历史。本批尚未将这个owner接入装备Router的生产生命周期。

## 更新、同步和提交

`LyraLocomotionSourceScope` 接收外层提供的十根访问及顺序。Main运动观察/宏更新一次，Idle和Air字段取本候选观察。源按十根实际登记顺序重排全局SampleStart，地面各根之后登记其Lean；角色只调用一次共同Sync。源保留自己的时钟，Scope不再创建播放时钟。

Scope保存同一Sync结果，根输出包含81骨、曲线及存在性、typed整数属性与RootMotion。异代、外来/重复身份、缺失或坏样本、重复Resolve、不同Sync求值、无姿态提交和旧候选提交会拒绝；发生求值或Resolve故障后整帧输出不可读、不可提交，必须取消。各根预校验后共同提交；取消和晚期重试不发布部分机器/源/反馈历史。

补齐地面pose host的update-only入口。未求值时源时钟可提交，Start/Cycle/Pivot保留上次实际求值的Orientation/Stride状态；原初始化仍按原标志重置。Idle自身已提交TurnYawWeight也保留。默认调用继续要求活跃地面根已求值。本批联合测试逐帧断言这些保留/重置行为；没有新增UE十根联合或稀疏Warp轨迹，不能将该本地门禁表述为新的原生稀疏整链验收。

## 实际验证

统一资源工厂重放原有三Provider、30/60/120Hz真实UE轨迹，原位置/quaternion/scale、位级时钟/状态、曲线、属性和RootMotion门槛不变：

| 重放 | 帧 | 完整姿态 | 骨求值 |
| --- | ---: | ---: | ---: |
| 四地面共同原图 | 3780 | 8400 | 680400 |
| 五Air Provider | 3780 | 7650 | 619650 |
| Idle主轨迹 | 26460 | 26268 | 2127708 |
| Idle六取消 | 26460 | 3780 | 306180 |
| 合计 | 60480 | 46098 | 3733938 |

地面最大位置1.044281e-13cm，Air1.226008e-13cm，Idle1.158861e-13cm，scale误差0；真实source时钟与原有状态比较通过。这里地面保持原四根共同UE执行证据，Air/Idle为各自原Provider捕获，**不是十根共同UE oracle**。

新联合场景从既有物理观察构造受控十根访问，不用UE期望时钟或姿态驱动源。3780帧、10851姿态，1008帧十根全访问、252全隐藏、543仅更新、1191父inactive根访问、20种交错顺序，实际三个同步组与24个活跃源身份；25680次坏操作拒绝和逐帧早/晚取消重试通过。额外积累RootYaw用于覆盖Idle Test组，输入属于受控组件夹具，未执行完整Main机器来选择这些根。

首联合场景完成逐帧断言但末尾覆盖门禁失败，因为没有触发Idle Test组；补受控持续转向后覆盖完整，未放宽Sync或姿态门槛。首次smoke编译错用Idle.State成员已修为实际Fields/机器历史。失败日志均保留。

Debug、ExportRelease Optimize构建0错误0警告；最终Godot退出0且无ERROR/WARNING。`verify_lyra_locomotion_scope.py` 核对60480帧原生重放、最终联合门禁、508原UE包和642旧JSON哈希以及清单来源，输出 `locomotion-scope-final-verification.json`。本批没有新Core全量、UE构建/采集、普通Demo、渲染、人工矩阵或性能验收。

主要证据：`artifacts/lyra-analysis/locomotion-scope-native-first.log`（成功完整原生重放）、`locomotion-scope-joint-packet-final.log`、`locomotion-scope-{debug,optimize}-final.log`、`locomotion-resources-generation.json`。失败为 `locomotion-scope-debug-smoke.log`、`locomotion-scope-joint-{first,coverage}.log`。

## 下一项边界

十根Scope仍接收外部current/上一机器权重和根遍历；地面根的原Main回调已复用，但Main Idle根的ProcessTurnYawCurve/RootYawMode及上一完整Main曲线反馈尚未接齐。已有Main机器组件仍需消费Godot自己的相关源/同步/通知历史，产生真实初始化、清权重与更新访问，再求值最终状态混合和原祖先惯性。

随后将十根owner接入同角色ItemAnimLayers实例与typed14入口，完成统一Notify/Montage、其余上身/足部和角色Gather/Worker/Commit，最后普通Demo、三装备视觉及性能验收。self-layer、Unlink、部分覆盖、多组和显式持久实例仍需独立合同验证，不宣称当前单组实现支持任意UE动画接口。
