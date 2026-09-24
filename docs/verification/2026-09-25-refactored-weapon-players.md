# 四武器播放身份与同步角色

## 实现

主目录 `.`，`main`。新增 `AlsRefactoredWeaponPlayers`，从原 catalog 的 baked state player 列表和原始嵌套状态图中编译 SequencePlayer。SequenceEvaluator 不分配播放时钟。

- Bow、PistolOneHanded、PistolTwoHanded 各三个 Idle 播放身份，分别属于 Relaxed/Aiming/Ready。虽然资源路径相同，身份不能合并。
- Rifle 共六个：上述三个 Idle，加 Relaxed 状态的 Run Arms、Sprint Arms、Sprint Impulse Arms。
- Idle 使用 `Secondary Motion / CanBeLeader / rate=1`；Rifle 三个手臂源使用 `Movement / AlwaysFollower / rate=0`。后者的时间由真实 Movement 同步领头推动，不能擅自把 rate 改成 1。
- 校验原始资源、状态归属、节点类型、compiled/property 索引、原文本路径/组、runtime 与 authoredProperties 的角色/循环/初始时间/缩放/回调策略，拒绝未编译的动态绑定。
- 宿主显式提供起始 player ID 和同步组 ID；禁止把不同原组映射到同一个组，禁止缺组。资源身份与宿主播放身份保持分离。

发现并修复 `AlsRefactoredSourcePlayerRuntime` 未传递原同步角色的问题。Definition 增加 Role，默认保留 CanBeLeader；构造拒绝未知角色，Prepare 将角色送入既有 Core 同步执行器。Core 算法本身没有改动。

## 验证

新增 8 项通过，相关 Import **120 项通过**、Core `AlsAssetSyncRoleTests` **9 项通过**，Godot Optimize 构建 **0 警告、0 错误**。

四种武器全部 15 个播放身份与既有 UE 连续导出的 propertyIndex 集合相符；同资源的三个 Idle 保持独立编号。实际原始 Rifle 三手臂与低权重 Walk Forward 在 30/60/120 Hz 下连续两秒（共 420 帧）接入真实源播放器和 Sync bank，手臂速率 0、权重 1，移动源权重 .001。验证移动源成为领头，手臂按 follower 更新、时间推进、原始 79 骨可采样；晚取消重试时钟/组历史一致。

测试同时重现旧适配器丢失角色的行为。不能简单描述为“最终领头必然被抢”：这些原始资源带有同步标记，旧路径最终仍选择 Walk Forward，但之前的三个手臂源已经按候选领头执行并停在时间 0，不会再作为 follower 跟随最终领头。修复后，低权重移动源先执行，三个手臂源随后跟随。

初轮新增 8 项通过。加入旧行为对照后，错误地断言最终领头不是移动源，导致相关测试 117 过/3 失败；依据实际 Core 标记领头尝试顺序修正为检查尝试角色及手臂时间，最终 120 项通过。未修改算法或放宽容差来掩盖失败。产物在 `artifacts/refactored-weapon-players/`。

没有新增 UE oracle、UE 插件修改/构建/启动，也没有 Godot 场景/人工视觉或性能验证。本批实际资源同步测试不能替代完整武器原生连续姿态对照。旧 UE 参考只用于确认玩家身份；Core 原有角色回归另外通过。

## 后续及边界

本批完成播放资源编译和共享播放器角色传递，不是完整武器图。仍需按状态图计算 source update 权重、进入/重新进入初始化、与共享 Sync batch 合并，再求值固定姿态、Aim mesh additive、Idle、状态过渡与外层动作曲线。尤其 Rifle 还有 StandingState 的 sprint acceleration 输入，不能用已有受控轨迹中的默认 0 代替生产输入。

Overlay 完整姿态仍 9/13，普通 Demo 未切到新 Refactored 整链。真实 Locomotion/统一角色宿主、完整 Turn 和 DynamicTransitions 调用端、Ragdoll/Flail/Get-up/Pose Recovery、完整相机边界和十分钟性能验收等旧缺口仍保留。音频、道具物理和头颈诊断继续暂缓。用户未提交修改未纳入本批。
