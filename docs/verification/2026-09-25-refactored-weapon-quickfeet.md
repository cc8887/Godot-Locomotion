# 四武器 QuickFeet 连续姿态参考

接续 `bc8b664`。上一批已有 QuickFeet 求值分支及独立每骨权重参考，但旧连续 trace 的实际求值栈没有 edge 2，不能证明整段姿态混合正确。

## 场景与覆盖

`export_refactored_weapon_trace.py` 增加 `ALS_WEAPON_QUICKFEET_TRACE=1`，不覆盖旧输出。每种武器在 30/60/120 Hz 各跑两段：初始瞄准后进入 Ready，保持允许放松并用普通 delta 等待三秒，完整求值 .75 秒 QuickFeet；第二段在 QuickFeet 开始约 .25 秒后重新瞄准，产生被打断的多过渡栈。沿用变化 stance/gait/方向/空中预测等姿态输入。

旧场景的 `frame(3)` 可能在触发过渡的同一次 Update 内直接将其推进到完成，导致 Evaluate 没有这条边。新场景不用大 delta 越过等待。

四武器共 8424 帧、665496 骨骼姿态。每武器三帧率各有 29/62/126 个 QuickFeet 活跃条目，总计 868；新的测试要求每帧率均保留 QuickFeet 与后续过渡并存的求值帧。对全部姿态/曲线、栈 alpha 做对照，并保持取消重试、帧身份、owner 和资源哈希检查。

## UE 证据

完整 Editor target build：0 actions、退出 0。所有项目插件审计通过，fingerprint `68FD4C514E49D886A1B9C8C4B4AFE6ACB800354460881A560948D4D96AF2D95C`，BuildId `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`。

`artifacts/refactored-weapon-quickfeet/cold.log` 冷命令行退出 0；`editor.log` 普通 Editor PID 40880 退出 0。两次均记录 `graphs=4 frames=8424 assets_saved=0`，四份 JSON 字节一致：

| 武器 | 字节 | SHA256 |
|---|---:|---|
| Bow | 34045168 | 076129CA4D24AD9D6C4E8AB9C139649083C9034E670C8A205D784B781DE0F126 |
| PistolOneHanded | 33893672 | 9695143669888C64684D74EBDADB5960DE449FB15BA817B7BB44B333CCF2B416 |
| PistolTwoHanded | 34056793 | D8CA1F0D05025F4D51B8A20FE5CF6BEB6C78A6BA71BE5D3D7F5BEBE0C5DE4B60 |
| Rifle | 34364713 | 9CA6B78A7B745D9EE9F5F86DDB185D19F7A15F4C163B04EA787E30F3AB5DB925 |

普通 Editor 两条既有 `Condition failed` 和五类既有 warning（PawnActionsComponent、旧 Navmesh、Material、MotionVectorSimulation、Crowd）仍在，不称日志完全无错。无 C++/插件配置修改，本批没有新 DataValidation 或打包验证。

## 结果与界限

首轮 `quickfeet.trx` 新增 12 项全部通过，生产求值算法未再修改。最大位置差 1.5358780e-5 cm、旋转分量差 1.2379378e-7、缩放差 8.9406967e-8、曲线/alpha 差 1.7881394e-7，沿用上一批 P1e-4 cm、Q/S/curve/alpha2e-6 预算，没有调整阈值，不是逐位一致。

最终相关回归 `artifacts/refactored-weapon-quickfeet/related.trx`：136 通过、0 失败，包含新增打断覆盖断言，共确认 44 帧 QuickFeet 与后续过渡并存。Godot Optimize 构建 0 warning、0 error。

本批完成此前缺失的 QuickFeet 连续原生姿态验证。外层 Action、隐藏/重入尚未实现，完整 Overlay 仍为 9/13；普通 Demo 尚未切换至完整 Refactored 链。Ragdoll/Get-up/Pose Recovery、真实 Locomotion/统一宿主、Turn/dynamic transition 调用端、Mantle gameplay、完整相机和十分钟性能等既有工作仍保留。未进行 Godot 场景或全量测试。用户修改未动；音频、道具物理、头颈诊断继续暂缓。
