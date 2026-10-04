# Lyra 原 Emote 行为与 ALS 角色接入

本批将原 `GA_Emote` 的本地动画行为接入现有 Lyra Main、物理 Montage bank 与 ALS 人物。普通 Lyra Demo 新增 **E 表情动作**。人物仍使用现有 ALS 模型和骨架，Main/Linked Layers 的换装流程保留同一个角色动作实例。

## 原始行为证据

新的可选 Editor 探针执行原生成类、`ALyraCharacterWithAbilities`、Lyra ASC 和 `AbilityTask_PlayMontageAndWait`，使用原 FingerGuns MW 和 Pistol Reload Montage。原 CDO 为 InstancedPerActor、Independent、禁止有效期间重新激活；PlayMontageAndWait 完成、中断和取消输出连接 `K2_EndAbility`，自然 BlendOut 输出未连接。

原移动回调使用 **OldVelocity 的 double 长度 > 0 或实际 bIsCrouched**。触发后清空整个 `OnCharacterMovementUpdated` 委托，再调用 `ACharacter::StopAnimMontage`；它跳过已经开始淡出的 Montage。自然结束、外部取消和前置 Montage 中断均不会解绑移动委托。清空委托时已复制到本次调用的外部观察者仍会执行。

在原生成类运行中，激活时 UnCrouch 先清除 CMC 的 bWantsToCrouch；是否真的退出蹲伏取决于随后的移动/净空边界。参考采集显式控制实际蹲伏应用和移动回调，没有宣称执行完整 UE 世界物理。

特别保留原来的淡出边界：自然淡出只清除 ASC 的动画关联，Ability 到正常 End 才结束。自然淡出后被其他 Montage 替换时，`bAllowInterruptAfterBlendOut=false` 不广播 End 中断，Task 结束但 Ability 保持有效；后续移动清委托也不会结束它，显式取消才结束。本批未修正这个原行为。

基础 36 条和补充 18 条轨迹覆盖 30/60/120 Hz、自然结束、极小/垂直/晚到旧速度、激活退出蹲伏、保持蹲伏、晚到蹲伏、外部取消、替换、再次激活、无移动回调，以及淡出后的移动/取消/替换。两批均有两次独立 UE 运行，原始导出结构一致，四次进程退出 0；原模板和项目未保存修改。

## 实现边界

- `LyraEmoteAbility` 按原事件顺序维护角色动作，独立于装备 LayerEpoch；没有另外建立 Montage 时钟或自动生成 Align 目标。
- 真实胶囊移动成功后才发布 Advance 行为及 typed 移动回调，传入移动前的位置/速度和净空处理后的实际蹲伏状态。角色、生命周期和物理帧身份拒绝跨角色、异代、重复及销毁后的调用。
- 移动取消在已消费根运动之后停止物理 Montage。Main 取消重试先还原相同原始 root range/context，再重放同一后置停止命令；不重放胶囊移动、委托或 Ability 事件。
- Main 完整提交后，先按已有顺序投递资源通知，再派发 Emote 的 Montage 结束事件。外部取消和更新前的 Montage 替换保持原来的前置结束阶段。
- 普通 Demo 使用 E 激活，真实站立净空决定是否退出蹲伏；现有玩家和独立场景角色均接入。换装备保留有效动作和委托，角色销毁撤销动作及回调资格。

这是原动作的本地行为移植。没有实现完整 GAS、网络预测/复制、任意 GameplayAbility 或任意 AbilityTask。原参考省略资源 Notify 的执行；资源通知的现有 Godot 队列另有此前证据，整个 Main 联合连续原生对照仍开放。

## 验证与交付

最终证据由 `scripts/verify-lyra-emote.ps1` 的 Debug/Optimize 矩阵及 `tools/verify_lyra_emote.py` 审计。原生行为对照每个构建覆盖 **54 条、27,720 帧、27,720 次动画取消重试及 1,774,080 项状态/位置/时钟/权重比较**；原误差门槛没有放宽。

六个真实 ALS 角色覆盖自然结束、旧速度取消、正常退出蹲伏、顶上净空阻挡、外部取消、换弹中断、动作期间装备替换、结束后委托保留/清除和重新激活。每个构建的三种帧率共 **10,080 次胶囊移动、移动前失败重试和动画重试**，同帧完整 Main pose 精确一致。每个构建 21 次激活、18 次结束、9 次清委托；最后一个重新激活的动作在测试末尾仍有效，报告没有把它提前算作完成。

另有原 Warp 与根运动物理回归、普通十角色换装/武器基线及真实 E 输入验证。525 项既有 Core 动作/通知/时钟/root/Warp 边界测试通过。最终构建 0 警告、0 错误；优化矩阵记录实际加载 DLL，并在结束后按 SHA 恢复六个 Debug DLL/PDB。

最终调试与优化的完整物理摘要一致。实际 GPU 60 Hz/480 帧的摘要与对应 headless 一致，采集 FramePostDraw 的三张图并人工查看。该远景检查只确认模型、动作和场景可渲染，不代表近景握持、材质或全观感验收。

852 个已有 JSON、709 个原 UE 包及项目/Config 保持原 SHA；本批新增五个参考 JSON，总数 857。可选探针只在构建时进入 GASP 的任务目录，随后移回 `artifacts/unreal/lyra-emote-oracle/package-ready`；原工程未留下该插件。首次探针编译、策略读取重复 UberGraphFrame、测试局部变量及错误 SDK 工作目录失败日志保留，修复后只以最终构建/矩阵作为验收证据。

证据汇总：`artifacts/lyra-analysis/emote-verification.json`。最终矩阵：`emote-{debug,optimize}-verified.log`；原生参考：`emote-native-{first,independent}.log`、`emote-edges-{first,independent}.log`。

完整 Lyra 移植目标继续开放：剩余 NotifyState/命名事件、自定义 Warp delegates/通用 Seek、整个 Main 联合连续原生对照、Shotgun/Feminine 完整 Main、多 Layer Group/self/Unlink、复杂地形/移动平台、近景握持/材质和性能。音频、道具物理及头颈专项继续暂缓。
