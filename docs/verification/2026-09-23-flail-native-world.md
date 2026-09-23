# 同条件原生 Flail 场景物理轨迹

UE world 导出增加可选 `-PhysicsWorldFlail`。读取 Godot 本次生成的完整初始身体/环境配置，原生 SingleNode Flail 每步按真实 pelvis 速度更新播放速率，使用实际 SetAllMotorsAngularDriveParams/UpdateRBJointMotors，再执行同步 Chaos 场景。禁用动画更新 kinematic 骨骼，保留显式捕获的固定根初态；没有让 Core 输出反过来驱动原生身体。每帧记录 Flail 时间、速率、输入速度、原始刚度、实际目标/K/C及身体结果。该场景是受控物理对照，不是完整 ALS Gameplay Tick。

Godot 增加可选 `--flail-capture=<新绝对路径>`，成功物理提交后记录输入和结果，正常退出或验收失败退出均保存。不开启则不生成这些逐帧诊断对象。比较工具 `tools/physics/CompareFlailWorld.ps1 -Native ... -Core ...` 核对帧数/次序，恢复速度 JSON 的 float 存储边界，输出选定帧差值。K 比较只覆盖 Core 非零启用轴，不宣称完整 flags/零轴对照；目标四元数按符号对齐。

## 原生发现

产物目录 `artifacts/flail-native-world-20260923/`。本次两端均60Hz、同一初始配置、十秒：

| 模型 | UE 原生 | Godot Core |
|---|---|---|
| AnimMan | 220帧起睡眠，最后一秒V/W为0 | 206帧起睡眠 |
| Mannequin | 十秒未睡，最后一秒最大V=20.743444cm/s、W=1.790171rad/s | 十秒未睡，整组末秒最大V=49.817822cm/s、W=2.840357rad/s |

**原生 Mannequin 也未满足现有20cm/s及一秒休眠门槛。** 这纠正了“60Hz失败全部是移植错误”的假设，但不能解释/忽略 Core 比原生更大的运动残差。

两个模型第一步时间、rate、pelvis输入速度、非零启用K相同，目标最大分量差4.40839e-8。第一步输出最大速度分量差：AnimMan1.52588e-5cm/s、Mannequin1.66893e-5cm/s；不是所有身体都逐位相同。

Mannequin选定帧的位置/速度最大分量差：

| 帧 | 位置cm | 线速度cm/s | 目标四元数分量 |
|---|---|---|---|
| 1 | 8.02888e-7 | 1.66893e-5 | 4.40839e-8 |
| 60 | 2.42659e-5 | 1.64032e-4 | 8.11868e-8 |
| 120 | .047718 | .567010 | 1.47822e-5 |
| 300 | .437174 | 1.842830 | .000473001 |
| 600 | 2.371663 | 22.465677 | .0200624 |

后期输入时间/rate/K也随各自物理速度分化。证据支持从首步目标精度及随后接触反馈调查，尚不能确定哪个因素导致全部后期差异。禁止用后期原生目标直接控制Core后再称闭环等价。

## 验证

- Godot Optimize通过。带捕获60Hz重跑仍是同样的49.817822/2.840357失败，没有改善或放宽门槛。
- 完整UE Editor目标构建与插件审计通过，fingerprint `625DDD11E57D578A238D805D1A20E19BCB98560C6E5401C455FA7C3BBCF565F1`。首编译缺少PhysicsConstraintTemplate头文件，修正后成功；首失败日志保留。三份导出器源码与UE项目镜像一致。
- 两次冷导出成功，`native.json`/`repeat.json`均28475405字节、SHA256 `56459DCBB0FE86D9C5639CA2B0EFF45D57C4565E81ED5AA14F86261DD761B434`。每模型601身体帧、600动画驱动记录。大体积诊断保存在主目录artifacts，本批未新增冻结配置资产。
- 不开Flail的冷复导，其两个60Hz case的完整 samples JSON与既有 `v4_physics_world_reference.json` 对应case逐字一致，睡眠帧A231/M471；旧导出路径未改变。
- 比较工具复跑输出一致，`comparison-checked.log` SHA256 `CD81EDB126FEC672A46A216307F3B64A4C7CFB4CD6B8A9A1963C512F973D21F4`。
- DataValidation退出0，三个旧警告。普通Editor PID28512加载验证标记并退出0，两条旧Condition failed仍在；旧间歇AV未宣称修复。

下一步先对齐首步实际目标的采样精度，再在同输入下确定早期身体/接触分歧。Flail落地矩阵仍1/3、静态目标9/12；本批没有关闭60Hz残差或30Hz睡眠问题。普通Ragdoll生命周期、Get-up/PoseRecovery、Mantle、完整Camera、十分钟性能目标全部保留；主目录main，用户P4修改未动。
