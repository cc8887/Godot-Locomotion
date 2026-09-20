# 原图腿部函数组合与参考骨架初始化

日期：2026-09-13，第一百六十八批。原 P4 完整脚部链继续推进；新函数尚未接 Demo。

## 已实现

`AlsRefactoredLegRig` 将原 ApplyFootIk 组合为一次候选组件姿势求值：

1. 读取骨盆写回后的 Pelvis/Thigh，执行脚位置弹簧、骨盆距离和腿长约束。
2. 从尚未执行腿 IK 的当前三骨位置计算 pole，保留失败时的旧输出，执行独立平滑。
3. 以 TargetRotation 和位置结果作为 Effector，执行原左右轴、参考长度及权重的双骨 IK。
4. 从 IK 后小腿/脚旋转计算脚踝约束，再按原 SetRotation 权重写回，并传播子骨。

权重为零不会跳过弹簧、pole 和脚踝历史更新；外层无效分支才跳过整个函数。
SetRotation 的跳过条件是 `< SMALL_NUMBER`，双骨 IK 为 `<= SMALL_NUMBER`，
保留两者差别。部分旋转使用 UE Slerp，复用前批已验证的计算。
所有姿势写入 caller 提供的候选数组，历史以值返回；参考姿势与 scratch 不允许重叠。
函数本身不负责整角色的 Prepare/Commit/Cancel，该所有者接入仍待完成。

新增 `assets/config/refactored_foot_rig_inputs.json`，原生导出包含顶层 RigVM、
RefreshFootIk、ApplyFootIk 和变量声明。`AlsFootRigCompiler` 校验拓扑、引脚类型、
注入变量、函数引用、左右骨骼、空间、曲线、拉伸/传播、参考长度与 Effector 缩放。
时间、角度区间、pole 距离、位置弹簧参数和 PoseMoving 三组 double Lerp
端点从图中编译，保留超出 [0,1] 输入的外插，不以移动布尔值代替。

参考骨架绑定保留原 PrepareForExecution 语义：

- FootHeight 从 `foot_l` 初始全局 Z 进入实际 float 节点边界。
- LegLength 从 `thigh_l` 到 `foot_l` 初始全局链逐段累加，每段 double 距离
  加 float 累计后再转 float。两腿使用原图共享的左腿长度变量。
- 根据 useFootIkBones 选择 `ik_foot_root` / `VB foot_root`。
- 检查骨名唯一、父先顺序、左右腿祖先链及必要骨骼；不会用当前动画姿势初始化。

## 组合对照证据

`AlsLegRigProbe.cpp` 在临时 RigHierarchy 中实际调用 ALS/UE 原节点，按导出图
次序串联位置、pole、平滑、IK、旋转限制和 SetRotation，另直接调用 ChainLength。
这是受控输入的原生节点组合，**不是整个 CR_Als VM 或完整 AnimBP 求值**。

12 组 × 120 帧 = 1440 帧，覆盖左右腿、30/60/120 Hz、非均匀缩放、
零 delta、重置、退化 pole、远目标、PoseMoving 外插以及 6 档权重。
包括 60 帧跳过执行、240 帧零权重；1380 次执行均从同一旧状态重试，
候选姿势和状态逐值一致。读取 native ChainLength 结果验证参考绑定。

| 实测项 | 最大误差 |
| --- | ---: |
| 最终骨骼位置 | 2.618362753381646e-6 cm |
| 旋转 1−绝对四元数点积 | 1.7763568394002505e-15 |
| 缩放 | 1.1102230246251565e-16 |
| 位置弹簧/pole/法线历史 | 3.814697265625e-6 |
| 弹簧速度 | 6.103515625e-5 cm/s |

脚部环境及新原图合同检查 18/18、原生组合专项 1/1、Core 原有专项 65/65
通过。组合专项覆盖上述全部帧，不能把测试方法数量当对照帧数。
Godot Debug 优化构建零警告、零错误。
记录：`artifacts/foot-rig-contract-168-tests.log`、`artifacts/leg-rig-native-168-tests.log`、
`artifacts/leg-rig-core-168-tests.log`，相应 TRX 位于 artifacts/tests。

冷启动、普通 Editor 与正式夹具三份数据完全一致，SHA256：
`BE11384399EC8E4C51AB4FE7723F6C4165BF3AA29809062DBD81CEEE4E7E2C13`。
夹具为 `tests/Als.Core.Tests/Fixtures/FootIk/native_leg_rig.json`，无资产保存。
完整 Editor 目标构建与审计通过，BuildId
`33127afa-6328-46c8-831f-0725f7953aac`，fingerprint
`2B4F92D47E20F9190D620351F4A7ADA7DE082C011D9DF36F4CB48B668B34D979`。
冷启动与普通 Editor 导出和退出均为零；普通 Editor 仍有两条已有
AutomationTest 启动 Condition failed，未把它们报告为已修复。
探针及头文件两份源码一致。DataValidation 退出零，0 error(s)、3 warning(s)。
含 ALS 依赖的隔离插件包构建成功、ExitCode=0，DLL/PDB/modules 齐全，
包后项目审计 AUDIT_PASS。打包时 UBA 因低内存终止一次编译进程并自动
重试成功，保留原日志；未部署隔离包 DLL。产物位于
`artifacts/unreal/leg-rig-168-package/`，相应构建/审计日志同目录前缀。

## 实际帧接入仍缺什么

当前 `AlsFootIkFrameRuntime` 仍拥有 V4 控制器，只在 based 分支替换锁定状态。
新腿部函数与前批骨盆/查询还未接生产。下一步要统一两腿、骨盆和地面观察
的候选/提交所有者，保留无效输入分支、重初始化和整帧失败回滚；将原图
查询空间/阶段接入主线程采集，再替换 based 路径的 V4 后续 IK。

V4 资产当前消费者使用 Enable_FootIK_L/R、FootLock_L/R；Refactored 图读取
FootLeftIk/FootRightIk/PoseMoving。必须证明各曲线的来源与混合语义后适配，
不能仅改名、以 Weight_Gait/移动布尔替代，或将缺失值默认为理想状态。
完整原图对照、真实移动/支撑窗口、平台、截图与人工验收仍需要完成。
第 616 帧约 41.100025° 的旧视觉失败、默认完整入口以及原 P3/P4 和 P5A–P7
全部未关闭，音频仍暂缓。
