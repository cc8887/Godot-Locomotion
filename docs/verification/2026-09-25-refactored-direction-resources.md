# 原版方向状态机资源与规则

本批导出 Standing/Crouching 全部六个 baked machine（33 状态、87 过渡）；生产编译器本批接入其中两个 Movement States：各六状态、24 过渡、八个共享规则。其余四个状态机已保留原始数据，尚未编译或执行。

保留原始状态顺序 Forward、Backward、RightForward、RightBackward、LeftForward、LeftBackward，baked 出口顺序及独立过渡身份。相同起止状态可以有多条不同规则的边，不能只按起止状态合并。通过原 native 图的 SharedRulesGuid 和具体连线核对共享 delegate，而非假设每边独占一个规则。没有连线的 TransitionResult 还可能绑定 Parent 属性，不能当恒 false。

四个方向条件直接读取 GroundedState.MovementDirection 布尔字段。换髋条件检查 HipsDirectionLockAmount 的 ±.5 与 FeetCrossingAmount <= 0；解除锁定要求 abs(lock) < .5、feet <= 0，并要求指定来源状态（原生局部 3 或 5）的权重 >= 1。原 getter 引脚默认 StateIndex=0 不是最终索引，编译器根据 SourceStateNode 完整路径映射。

蹲伏全部 24 边为 .7 秒 Cubic。站立六边 .5 秒 Cubic，触发本类局部通知 0 / ActivatePivot；七边 .75 秒 QuadraticInOut，其他十一边 .7 秒 Cubic。全部使用 MoveDirectionChange。新增共享 QuadraticInOut，保持 UE AlphaBlend.cpp / UnrealMathUtility.h 的分支和浮点计算顺序，不用不同的曲线代替。未执行 ActivatePivot 通知消费。

MoveDirectionChange 的 79 骨布局和 18 个有效 entry 与源骨架绑定，并逐项验证 UE 导出的 33 组 incoming/outgoing 权重；每个 stance 1188 个分量比较，误差预算 2e-6。没有新 UE 连续方向机器或规则真值 oracle；规则证据来自实际图结构，运行时边界测试属于移植侧验证。

## UE 与产物

按 ue-diagnosing-plugin-build-load 技能执行完整 Editor 目标：0 actions、退出 0，四项目插件审计通过；BuildId c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5，审计指纹 68FD4C514E49D886A1B9C8C4B4AFE6ACB800354460881A560948D4D96AF2D95C。

构建记录：`../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260925T012216042Z-129d837915e6480ba13c52457d2c0602-*`。

脚本 `tools/unreal/export_refactored_stance_machines.py` 复用现有通用反射接口，没有 C++/插件/配置修改或资产保存。cold 命令行退出 0；普通 Editor PID 39656 退出 0。两次 JSON 字节哈希相同：`0CAFF6ECB542990F9813CBDF0DC2F459183E2FEC251CEE71BC107108A79AF3D6`，237028 bytes，已纳入 `assets/config/refactored_stance_machines.json`。日志与原始输出在 `artifacts/refactored-stance-machines/`。普通 Editor 仍有两条旧 Condition failed 和五类旧 warning；未宣称这些错误已经修复。无本批 DataValidation（无插件/配置修改）、打包或 Godot 场景验证。

## 测试与排查

新增七项测试：两套图的 16128 次规则边界判断、来源状态权重隔离、14 种资源变异拒绝、三个频率的共享 QuadraticInOut 推进。`related.trx` 22 项通过，Core `core-stack.trx` 15 项通过，Godot Optimize 构建 0 warning / 0 error。

最终 `final-import.trx` 43 项通过（新增七项与此前武器连续姿态 36 项），确认共享混合枚举扩展未破坏已有武器原生对照。

首轮暴露并修复了不能按起止状态唯一匹配边、Standing 通知非空、站立与蹲伏混合时长/类型不同等错误假设。`direction.trx`、`direction-shared.trx`、`direction-durations.trx` 保留失败证据；另修复 Math 命名空间冲突及缺失 QuadraticInOut 枚举的编译错误。未放宽既定骨骼权重预算。

## 后续

下一步将方向资源接入连续 state/transition stack 与更新／姿态遍历，按原优先级执行规则并消费换髋、ActivatePivot；再编译 Movement Details、Standing/Crouching 主状态及 Stop States。仍须完成缓存/pose、实际 Parent 更新与统一宿主。普通 Demo 尚未切完整 Refactored 链；Ragdoll、Get-up/Pose Recovery、Mantle、Camera、十分钟性能等完整目标仍未完成。用户修改未纳入本批，音频、道具物理及头颈诊断仍暂缓。
