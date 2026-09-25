# 四武器完整 Overlay 连续原生对照

接续 `2345cb5`，主目录 main。新增 `ALS_WEAPON_ACTION_TRACE=1`，使用真实四个 Linked Overlay 图，输入实际 Parent 的 action、rotation mode、pose/view/grounded 等状态。

## 场景与证据

每种武器在 30/60/120 Hz 跑十个一秒阶段，覆盖 Default、Mantling、GettingUp、Rolling、动作间转换、每阶段零 delta、隐藏期间 Initialize，以及末尾 Default→GettingUp 的零权重旧分支更新与立即恢复。每帧冻结原生整图骨骼/曲线、状态机、播放器时间/权重。原插件已支持这些输入，本批未修改 C++。

新增 `AlsRefactoredWeaponOverlayNativeTests` 12 项：完整 8424 帧/665496 骨骼姿态、4548 隐藏帧、24 零权重旧分支更新、48 次机器初始化/重入；每 17 帧取消重试，所有动作均达到完整权重。检查资源哈希和骨骼布局、每个实际更新播放器的时间/权重、更新时的机器 state/elapsed、整图姿态和曲线值及 presence。

原 Action 运行时未因本批 oracle 调整。补上编译器 `AnimGraph Root→LinkedAnimLayer Overlay` 入口校验：原接口、pose link、无输入/属性转发、无通知转发、无回调；四图增加入口层名称变异拒绝。

最大差异：位置 **6.1294866e-6 cm**，旋转分量 **3.5086625e-8**，缩放 **4.4703484e-8**，曲线 **1.1920929e-7**，有效播放器时间 **0**。沿用 P≤1e-4 cm、Q/S/curve≤2e-6 的完整图预算，不是逐位一致，没有为本批修改容差。

`artifacts/refactored-weapon-action-native/native.trx`：12 通过；最终 `related.trx`：156 通过、0 失败。初始测试编译有一个多余右括号，修正后测试编译通过；无失败 native TRX。Godot Optimize 构建 0 warning、0 error。

## UE 导出

完整 Editor 构建 0 actions、退出 0，插件审计通过，fingerprint `68FD4C514E49D886A1B9C8C4B4AFE6ACB800354460881A560948D4D96AF2D95C`、BuildId `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`。

冷启动 `cold.log` 与普通 Editor PID 44116 的 `editor.log` 均退出 0，记录 `graphs=4 frames=8424 assets_saved=0`。四文件字节一致：

| 武器 | 字节 | SHA256 |
|---|---:|---|
| Bow | 33805266 | 2F483DC8DF68473E8A1A12BAD7CCBA41B4A57567FC96AA67EED03AFBFCB0130E |
| PistolOneHanded | 33618006 | 295E8BB7B7488F146CBB8A11C253BDC5BA055A223DFC0ACBCB70FF3DBA72C280 |
| PistolTwoHanded | 33667880 | B22D0F6CEFC45E8DBFE217DF5531CEEFFAE563B1BF3EA7747641498F9F998757 |
| Rifle | 33940324 | F9F952E450F6D970048C7A77C99387E70635BEC29DAED9E4F9AB36375EC35D98 |

普通 Editor 两条旧 `Condition failed` 和五类旧 warning（PawnActionsComponent、Navmesh、Material、MotionVectorSimulation、Crowd）保留。无插件/配置更改，未新跑 DataValidation 或打包。

## 阶段结论与下一步

受控 Parent 输入下，**13/13 原 Overlay 图**现在都有相应连续原生姿态证据：前九图加本批四武器；武器 QuickFeet 的连续/打断证据见上一批。这个计数仅指各自 Overlay 图，不代表完整角色已实现。

下一阶段推进真实 Refactored Locomotion 的 Standing/Crouching 图与回调、共享播放器/Montage/Notify/RootMotion 事务和统一宿主，再将实际 Locomotion→Overlay→PostLocomotion/Layering/Head/ControlRig/Ragdoll 整链接入普通 Demo。游戏输入驱动的 Overlay 生命周期和 Montage 通知消费仍须在统一宿主验证。

普通 Demo 当前仍为旧链。Ragdoll/Get-up/Pose Recovery、Mantle gameplay、完整 Turn/dynamic transition 调用端、完整相机、十分钟性能和人工视觉验收等旧缺口仍在。没有 Godot 场景、全量或性能测试。用户已有修改保留；音频、道具物理、头颈诊断继续暂缓。整体目标未完成。
