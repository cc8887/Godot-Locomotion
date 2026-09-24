# 四种武器原版状态机资源

在主目录 main 补齐 Bow、PistolOneHanded、PistolTwoHanded、Rifle 的编译状态机资源。现有动画源快照只有节点 runtime 和原始编辑文本，缺少 baked states/transitions；本批单独补出这些表并绑定原 catalog SHA256，未改动旧快照或资产。

## 实现与发现

新只读 UE 脚本复用 ReadBakedStateMachines，输出每种武器的三状态、六过渡、节点索引/出口顺序、通知索引、两条全精度曲线、QuickFeet profile 及原生采样参考。编译器形成不可变资源，交叉验证旧 catalog 的 StateResult、TransitionResult、player 节点及编辑节点的图路径和政策。

四种武器的状态顺序均为 Relaxed / Aiming / Ready。Relaxed 出口为 [0]、Aiming 为 [1]、Ready 为 [2,3,4,5]；同一对端点的不同边不能合并：

| 边 | 方向 | 混合 | 时长 | 其他 |
|---|---|---|---|---|
| 0 | Relaxed→Ready | HermiteCubic | .2 | StartNotify 0 |
| 1 | Aiming→Ready | Custom Aim_Out | 1 | 无通知 |
| 2 | Ready→Relaxed | Cubic | .75 | QuickFeet、StartNotify 1 |
| 3 | Ready→Relaxed | Cubic | .75 | 无 profile/通知 |
| 4 | Ready→Aiming | Custom Aim_In | 弓 .5，其余 .2 | 无通知 |
| 5 | Ready→Relaxed | HermiteCubic | 弓 .4，其余 .2 | 无通知 |

实际机器 maxTransitionsPerFrame=3、skipFirstUpdateTransition=true、reinitializeOnBecomingRelevant=true。三个状态均无强制 entry reset。QuickFeet 使用原 SK_Als 79 骨布局、18 个 entry、WeightFactor 模式。两条原曲线复用通用 RichCurve 采样，保留原 float 键、切线和超出 [0,1] 的曲线值，未换成拟合曲线。

本批只保存过渡规则的 compiled delegate 身份，不执行规则或推断其语义。尤其不能用旧 V4 条件代替原 Refactored 的 elapsed/Locomotion/Rotation/TransitionsState 判断。通知目前也是生成类局部索引，尚未绑定名称和实际消费函数。

## 验证

新增 11 项测试：四类完整资源/曲线/profile；七类错误 catalog、出口顺序、弓时长、player 缺失、root 绑定、曲线、profile 拒绝。相关 Import 共 85 通过、0 失败/跳过，Godot Optimize 构建 0 错误/0 警告，Python 语法通过。本批无编译或测试失败。

每类 402 个曲线采样及 594 对 QuickFeet 权重比较（4 类合计 1,608 / 2,376；各类使用同两曲线与同 profile）。最大曲线差 1.1920929e-7，最大 profile 权重差 5.9604645e-8，预算 2e-6 未改变。没有状态机连续 tick、动画姿态或游戏通知执行对照，完整 Overlay 姿态进度仍为 9/13。

## UE 导出

按 UE 插件技能再次执行完整 Editor-target wrapper 和全部项目插件审计，0 actions，仍为 fingerprint 7B77EF5C4F1072CB20D57DE94700D2CAAE4897BFE4A6C33CD8033CBA95B6E84F，BuildID 4cd31a69-ae92-41ab-8118-ffba44340a1a。日志前缀 20260924T221007795Z-0d9def44eaa24e95b49e41d5f53f6f8d。

冷启动和普通 Editor PID 41000 实际 exit 0，导出逐字节一致，260,367 bytes，SHA256 7A1C489128E2606D3B179A270512D22BC47D7D0F9BC72EF5A8B7DDEFDC399967。原生 C++/插件配置未修改，因此本批没有再跑 DataValidation。普通 Editor 两条旧 Condition failed 和五类旧警告仍保留。

证据位于 artifacts/refactored-weapon-machines；入库文件 assets/config/refactored_weapon_machines.json。没有 UE 资产保存、Godot 场景、全量测试、性能或打包。

## 接下来

编译实际过渡条件、原通知名称/函数与门控，接三状态更新/打断/相关性恢复和各自 source-player，再完成四类武器完整姿态原生轨迹。原始 transition graphs 与 EventGraph 在 refactored_animation_sources 对应 payload 的 nativeText，可结合本批 rule/root/player 索引定位。

普通 Demo 未切新图，真实 Locomotion/Standing/Crouching、统一角色事务/Notify/root motion/骨架适配、Ragdoll/Flail/Get-up/Pose Recovery、Mantle gameplay、相机和十分钟性能/人工验收全部旧缺口仍待完成。音频、道具物理、头颈诊断继续暂缓，用户工作区修改保留。
