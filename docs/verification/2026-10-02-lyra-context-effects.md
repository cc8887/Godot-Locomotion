# Lyra ContextEffects 接口与实际地面探测

2026-10-02。在原 ALS 人物、68 skin / 69 raw / 81 logical 骨架及现有 Main / Linked Layer 宿主上继续推进。本批接通原 ContextEffects 的探测、接口消息、地表 Context 和 DefaultSkin 效果选择。音频播放按此前要求暂缓；原库没有 Niagara 配置，未增加粒子效果。

## 原资源及执行语义

直接执行安装版 UE 5.8 和 GASP58 中原 `UAnimNotify_LyraContextEffects::Notify`、`ULyraContextEffectComponent::AnimMotionEffect_Implementation` 与 `ULyraContextEffectsLibrary::GetEffects`。本批不展开 5.8/5.9 差异。

原通知共 686 对象：674 Walk、12 Land，均绑定左右 foot socket、Visibility 通道、忽略 owner、世界坐标 `(0,0,-50)` cm 终点偏移。Location/Rotation offset 为零，VFX scale 为 1；两条 Land 的 Volume 为原全精度约 0.3，其余 Volume/Pitch 为 1。不能把探测偏移随角色旋转或缩放。

原 Notify 首先调用实现接口的 owning actor，再遍历实现接口的 actor components；每次传入的原始 Contexts 为空。组件随后合并 CurrentContexts，并由 HitResult 的有效物理材质查询地表标签。效果库要求 exact effect tag、所有行 Context 的 exact tags 均包含且双方空集状态相同；返回所有匹配行，保留顺序和重复 SoundBase，不能使用“最佳匹配”替代。

`CFX_DefaultSkin` 共六行 Walk/Land × Default/Concrete/Glass，全部引用 MetaSoundSource，零 Niagara。原 Lyra `origin/5.8` 的 DefaultGame.ini 映射 Surface 0/1/2/3 到 Default/Character/Concrete/Glass，两个原 DataTable 注册动画效果及地表标签。当前 GASP58 缺少这些标签表注册和地表映射，原生采集分别记录了实际 GASP 空映射与原 Lyra profile。探针仅在内存暂设原 profile 并恢复 CDO；Godot 显式加载它，没有修改 GASP 配置。

原 `UpdateLibraries(empty)` 调用会更新组件字段，但 Subsystem 对空列表提前返回，保留该 actor 的旧库；真正卸载使用 `UnloadAndRemoveContextEffectsLibraries`。Godot 分别保留这两条操作。

## Godot 接入

不可变 `LyraContextEffectsCatalog` 共享原行、标签和地表映射；角色独立持有消费者、接收器、当前 Context 与 actor 库登记状态。来源仍使用原统一通知 callback、真实对象 identity、Main/Linked/player/sample/epoch，没有新动画时钟。

`LyraContextEffectsConsumer` 在 Main 最终求值后，使用本帧最终 logical pose 与实际 component transform 计算 socket 世界位置，通过 Godot 4.7.2 Jolt DirectSpaceState 执行真实 ray。Visibility 显式映射到 collision mask 1，自体递归 RID 使用角色物理快照排除。Godot collider 的 `lyra_physical_surface` 整数 metadata 映射原 surface；未设置时使用默认 surface 0。保留真实 collider/shape/点/法线，预提交检查命中对象、transform、surface、接收器及配置变化。

消息的 Bone/Effect/Animation、Location/Rotation offset、Scale、Volume/Pitch、Hit 与原始 Context 均保留；组件才聚合其当前 Context。最终 skin 仍由原唯一发布器写入。Prepare/Evaluate/Cancel 不发送信号，完整 Main、通知队列、Montage bank 与 skin 提交后才按原 callback 顺序分发。GameplayEvent 与 ContextEffects 共用同一遍历顺序；发送前先消费候选，拒绝重复和重入。

普通玩家与 `LyraSceneCharacter` 已挂 `LyraContextEffectComponent`，提供 `ContextEffectSelected` Godot 信号和 typed 完整消息。选择音频资源不代表播放 MetaSound，也不等同于 Godot 支持 UE MetaSound 图。

## 验证与证据

独立 C++ 探针创建真实临时 PIE World、ALS 原网格、Chaos box 与 physical material，执行全部 686 原通知对象：Default/Character/Concrete/Glass、无命中、平移加 yaw37 与 scale1.7，共 4,116 次；追加五条组件 Context / convert / empty update / explicit unload 用例，共 4,121 次、8,242 条原接口消息。64 个 exact effect/context 组合由真实原 GetEffects 输出。实际原组件在禁用音频的世界中执行原 Subsystem，记录音频选择对应的 null spawn 条目计数，未播放音频。

两个独立 UE 进程正常退出 0、结果结构相同，825 旧 JSON、682 保护包及项目描述/Config 哈希保持，0 资产保存。新增探针源码单独编译，不宣称重建整个 Editor；临时项目插件已经移回 artifacts，GASP58 没有残留安装。重新构建脚本亦已实际通过。原有普通 UE warning 不在本批修复范围。

原生资源 `context_effects_v1_policy.json` SHA256 为 `e9fb546e0903d3db88c0ebb6ab03373b41301ac07b85cd348430c28f66e74fbc`；`context_effects_v1_native.json` 为 `29f6beeadfda4d62d72c0eb282945c5efca5e2425050ada714f2d90bc2c73d19`。它们位于 ignored `assets/generated/lyra_als`，仅有代码的检出仍不构成可运行交付。

Godot 独立库存测试通过：4,139 个实际窗口帧，其中 18 个原 `.5` chance 被过滤的帧真实提交并继续同一 RNG，未绕过原策略；4,121 次取消重试、40,523 次拒绝、3,434 次迟到命中 surface 改变检查。原始消息、接口顺序、效果选择和所有载荷与 native 比较通过。ALS socket/world end 及 Chaos/Jolt ray contact 比较门槛为 0.0002cm，法线为 1e-6；这些平面 box 用例不代表复杂地形通用物理等价。

Debug / Optimize 各六角色三Hz共 10,080 角色帧、336 条 ContextEffects 消息、312 个音频资源选择，24 换类/2 重建每Hz，取消后历史不发布，双角色的完整输出与已提交历史同。实际 GameplayEvent 仍为每Hz 2 Melee + 2 Reload；每次 Commit 比较跨两个消费者的原 callback 顺序，并在监听中校验完整 bank/queue/skin 已发布。

两配置各普通十角色 4,800 发布帧、231 条 ContextEffects 消息、217 条真实地面命中，玩家六次换类且其他角色历史独立。该夹具没有 Montage 动作输入，不能把零 Montage callback 当作新动作覆盖。旧 GameplayEvent 3,735 调用/36 事件、Montage 33,235 帧、Source 队列 8,042 帧均回归通过。两配置三份角色完整报告及普通十角色报告逐值相同；两个八进程矩阵退出均为 0，无 Godot ERROR/WARNING，两次构建 0 警告/0 错误，六个 Debug DLL/PDB 逐文件 SHA 恢复。

最终 `tools/verify_lyra_context_effects.py` 实际退出 0，汇总为 `artifacts/lyra-analysis/lyra-context-effects-verification.json`。Core 算法未修改，没有重复此前 124 项 Core 门禁；没有新增渲染或性能验收。

## 保留失败及开放范围

最初外部插件路径的 UBT source 发现为空，改为项目内新建的临时插件编译，再移回 artifacts。新增 cpp 首轮 gather 缓存导致链接缺符号，强制 rules/gather 后编译通过。新建 World 首帧未刷新 solver material 表导致命中但无物理材质，补真实物理 step 后通过。随后发现空库更新仍保留原库，依据原 Subsystem 代码及执行结果修正。Godot 首轮把 direct native Notify 库存当作必过随机过滤的窗口；保留原 `.5` chance 并增加真实过滤提交后通过；测试属性名编译错误也已修正。失败日志与原观察结果均保留，未改资产、算法阈值或原 native 输出来接受失败。

本批范围为这套原资源的 ContextEffects 查询、接口投递、选择及实际角色接入。尚未关闭完整 AnimInstance 连续 NotifyState dispatch、武器 mesh / 命名事件 / MotionWarping、RootMotion 生产碰撞、整个 Main 连续 native、复杂地形、完整视觉及性能；全部 Lyra 移植目标继续推进。音频、道具物理、头颈暂缓项保持。

复跑入口：`scripts/build-lyra-context-effects-oracle.ps1`、`scripts/export-lyra-context-effects.ps1`、`scripts/verify-lyra-context-effects.ps1 -Configuration Debug|Optimize`。输出均保留已有证据，不覆盖原日志或哈希依赖 JSON；重跑需新的日志/报告位置。
