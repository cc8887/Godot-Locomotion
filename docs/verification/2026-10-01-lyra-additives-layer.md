# Lyra FullBodyAdditives 原生落地恢复与共享 Layer 接入

2026-10-01，当前主目录，安装版 UE 5.8.1 / Godot 4.7.2 Mono。按用户要求继续使用 ALS 模型与骨架，不展开 5.8/5.9 差异。本批完成第12个执行入口的组件和固定 Provider 组合，完整 Main、普通 Demo 与整个 Lyra 目标仍开放。

## 原生规则纠正

旧 `pose_layer_contracts.json` / AnimBP2FP DSL 将 `AirIdentity → LandRecovery` 序列化为 false；此前基于它实现的两状态 identity 层和文档结论不准确。本批在真实编译节点执行 handler9，结果为 Main `IsOnGround`。保留旧 JSON 字节及失败证据，用新的实际编译规则 policy 作为执行合同，不能继续用 false 规则跳过恢复。

原 FullBodyAdditve_SM 有三个状态和四条边：Identity→AirIdentity 为 `!IsOnGround` / 0秒，AirIdentity→LandRecovery 为 `IsOnGround` / 0.2秒，LandRecovery→Identity 第一优先边为 `!IsOnGround` / 0.2秒，第二优先边为原自动剩余时间 / 0.2秒。初始化、首次过渡丢弃混合、隐藏重入、上一权重、原标准混合栈全部保留；零 delta 与 inactive 更新上下文分别验证。

真实 `UpdateJumpFallData` 每帧在图前更新 double `TimeFalling`：Falling 累加，Jumping 清零，其余保留，包括 Layer 隐藏帧。StateResult4 的 BecomeRelevant 回调以 MapRangeClamped(TimeFalling,0,0.4,0.1,1) 生成恢复 alpha，蹲姿减半。UE 5.8 的 `SelectFloat` 实际参数和返回值均为 double，直到 TwoWayBlend6 的 float 暴露边界才转换。没有按函数名字过早量化。TwoWayBlend 的 Pose 混合还保留 `1-(1-alpha)` 的 float 补权重。

## ALS 资源与原生采集

三套 Unarmed/Pistol/Rifle 已存在的 `locomotion_extras` JumpRecoveryAdditive 资源直接复用：原目标 ALS 网格 skin68 不变，raw69 / logical81 保留 weapon 与虚拟骨。恢复播放器5使用 local additive、原 self/local 基底、非循环 rate1、DoNotSync；Layer Group 所有权与 Sync Group 独立，不能因为同属 ItemAnimLayers 就把它强行登记到 Locomotion 组。

新增 `AlsLyraAdditivesLibrary` 在临时 GamePreview Main→真实 ItemAnimLayers 实例上执行原 Root12 / Machine1 / StateResult2–4 / TwoWayBlend6 / RefPose7 / Player5。只替换绑定为 ALS81 transient sequences，执行原 PropertyAccess、编译规则、原 UpdateJumpFallData 和 StateResult 回调；真实 FAnimSyncGroupScope 将源汇入 Main，之后一次 native Sync 再 Evaluate additive context。捕获全部81骨、曲线存在性、typed属性与RootMotion。原生输出只用于断言，Godot运行时不加载 oracle。

外部 exporter 构建通过，两次独立 UE Cmd 采集均退出0，JSON语义重复检查通过；保护508原包与此前658份 JSON 的字节哈希，未保存新 UE 资产。新 ignored 三文件：native 49,383,764字节，policy 62,638字节，requests 1,417,644字节。policy 增加三个压缩 root 资源；共同序列地址保留原194 absolute +3 Lean 的前缀，后缀增加3 Recovery，旧JSON不改。

## Godot 实现与验证

`LyraLinkedMachineRuntime` 从既有 Idle 标准栈提取共享执行机制，Idle适配器维持原API；新 Additives宿主执行原三状态/四边、回调字段、相关播放器自动时间规则、源登记和候选提交。输入 Main Ground/Falling/Jumping/Crouching，登记 Recovery 到同一角色的共同 Sync 批次，无单独 tick；曲线/整数属性/RootMotion 随姿态一起混合。候选视图绑定具体帧，取消/提交后拒绝旧视图。

| 验证 | 结果 |
| --- | --- |
| 三 Provider ×30/60/120Hz 原生组件 | 7560帧、6075姿态、492075骨 |
| 真正变化的 additive 输出 | 1496姿态；四条边均覆盖 |
| 数据通道 | 曲线为空的原集合保持；5984整数属性、1496 RootMotion属性对照通过 |
| 状态与时钟 | 状态/权重/上一权重/初始化/栈/TimeFalling/double alpha/源资产/clock/interval/weight/marker均通过 |
| 事务 | 7560逐帧取消重试；25785旧视图、缺求值及双提交拒绝；24隐藏自动重入 |
| 原生姿态误差 | 最大位置9.02321e-14 cm、单位quaternion3.56527e-16、scale0 |
| Main共同Source/Layer组合 | 11340帧、9762姿态、530恢复tick、456实际恢复姿态、207晚期重试、726门禁 |
| 旧十入口与 Main→Left 回归 | 11340帧/9762姿态各通过，510/618门禁 |
| 编译 | Debug与Optimize ExportRelease均0错误0警告 |

原门槛保持：位置1e-8cm、单位quaternion1e-10、scale1e-12，曲线值/flags、typed属性presence/value按原门禁。198次所选边与native summary的201次状态变化不是同一计数：后者还包含隐藏重初始化导致的状态变化。

Additives进入同一个组的typed入口，绑定 Main调用节点/实例/epoch/候选身份，与移动源共用一次Sync和提交。当前组合宿主提供 LeftHand绝对输出与单独Additives输出，保留实际Main node76的0.65 Update分支权重；尚未把Additives应用到完整Main的Aiming/Slot之后。既有LocomotionSM oracle中的未访问Manny Player5不用于断言新增ALS81源，恢复源本身以本批完整组件native验证。**没有新Main+Additives联合原生oracle，不称完整Main输出或普通Demo生产接入。**

## 失败证据与范围

`additives-layer-ue-export-first.log` 是缺SyncScope的断言退出；`...-sync.log` 与 `...-diagnostic.log` 保留实际可达落地边触发Manny/ALS骨布局ensure以及旧false断言失败。`additives-layer-native-sync-diagnostic.json` 是诊断数据，不是有效oracle。随后正确绑定ALS81并采集，两有效进程无Error/Handled ensure；UE仍有原资产/编辑器警告。

两个有效UE采集的汇总均为0 error / 710 warnings，实际Warning行762；这些编辑器/原资产警告保留，不宣称UE零警告。

Godot早期编译的history构造签名错误、以double要求RAW rational时长等于float sequence时长，以及直接Equals InlineArray的测试错误已修正，日志保留。后续Main首轮用旧移动oracle的Manny Player5比较新增ALS恢复源而失败，改为明确按各原生边界验证，未改门槛、未丢弃恢复测试。

旧独立 Demo 的两状态 `LyraFullBodyAdditivesLayer` 仍属于旧简化路径，其false说明已被本批原生结论纠正；本批未擅自将恢复提前应用到左手边界。下一步Aiming与SkeletalControls剩余入口、上身/Slot/完整Main拓扑与最终惯性、统一Notify/Montage、换类生命周期、普通Demo/多角色/渲染/人工与性能验收保持开放。

复验：`python tools/verify_lyra_additives_layer.py`；运行 `scenes/tests/lyra_additives_layer_smoke.tscn` 和 `scenes/tests/lyra_main_als_native_smoke.tscn -- --main-additives`。
