# 方向记忆与动画速度历史

基线 `main / f1c61f8`，在主目录实现。保留用户README、场景、HUD、LayerBlending和漫游角色改动。

## 原生来源与修正

本地原ALS源码确认：`AAlsCharacter::PreRegisterAllComponents`以角色yaw初始化两种方向；`RefreshInput`仅在有输入时更新InputYaw；`RefreshLocomotion`仅在水平速度至少1 cm/s时更新VelocityYaw。之前Demo在停止时把两者改回角色yaw，并以0.01 cm/s而非1 cm/s判断是否更新速度方向。

`UAlsAnimationInstance::RefreshLocomotionOnGameThread`读取自己的上一速度，非pending且delta>UE_SMALL_NUMBER才计算加速度；原Demo直接使用Motor的float差分。新 `AlsRefactoredLocomotionHistory` 在原生世界厘米/double速度边界生成候选，使用该动画owner上一成功提交的速度，第一帧/pending/极小delta为零。方向和速度、平台身份/旋转一起随完整角色帧提交；取消不发布，禁止异角色/异代/旧帧历史。

输入使用Motor已采集的方向和实际消费的模拟量，先恢复输入向量，再按原GetSafeNormal的平方长度门槛（提升后的float 1e-8）归一化；不能把任意微小摇杆量提前变成单位输入。当前Motor的输入量和速度仍有Godot/现有float采集边界，不声明端到端double或原生逐位等价。

原CMC `bIgnoreBaseRotation=true`，而Character的空闲VelocityDirection平台yaw继承是另一独立设置。只读导出的实际CDO确认前者true、后者false。默认Demo因此不因平台旋转而旋转上一速度。Core保留可配置的相对旋转分支：同一base才补偿，换base/离地不沿用旧旋转；仅空闲且显式继承时累计VelocityYaw。初次观察用当前角色yaw初始化；完整Character初始化/停用重入时序及非默认配置仍须新原生轨迹验证。

| 生产阶段 | 当前数据 | 消费者 |
| --- | --- | --- |
| Main Motor | 实际速度、输入方向/消费量、角色yaw、真实floor快照 | worker只读值类型 |
| Demo Begin | 同帧Moving观察、候选方向/速度/加速度历史 | 原Grounded/Standing/Crouching/Air Parent、QuickStop |
| 完整角色提交 | 当前速度、最后有效方向、base身份/旋转 | 下一更新的差分和方向保持 |
| 取消或等待提交 | 原已提交历史不变 | 重试使用同一输入和历史 |

## 已取得的验证

产物在 `artifacts/tests/refactored-locomotion-history/`。

- Core新旧22项通过，含三频率420帧候选/retry、初始朝向/停止/低速、pending/极小delta、平台同身份/切换/脱离、可配置旋转/独立yaw继承、微小模拟输入与历史身份拒绝。首轮测试使用Math.PI遇到项目Math命名空间遮蔽，改为System.Math.PI；没有更改生产算法以通过测试。
- Import相关39项通过，含独立/共享Standing原生六组、Locomotion宿主和真实足部反馈。原参考及误差门槛未变；不是本批新增完整角色UE oracle。
- 最终Optimize构建0警告0错误；后续把GetSafeNormal容差明确为原float常量后，Core22项再通过。
- 普通Demo 30/60/120 Hz分别850/1700/3400帧通过。均4次Rest实际补步、Details mask63，Moving与MovingSmooth差异帧7/14/28。本批各3次Pivot，上批各4次；方向保持/精度变化会改变候选触发，未硬保留旧计数。完整原生轨迹尚待，不能由这组覆盖断言证明新计数原生等价。
- 新 `--platform-motion` 在既有真实AnimatableBody3D上同时以0.15 m/s平移、0.1 rad/s旋转；十角色single/parallel各3621帧，3101帧relative、3081帧同base旋转，2次取消、1次提交等待，931接触/991脚趾接触。逐帧检查独立速度差分、停止方向保持，以及取消/提交等待期间历史不变。姿态 `BB83ED5C4D3DB611`、根运动 `46D6E1BA1FED127A`、结果 `D879B1B3D65ADDBE` 三摘要完全一致。该测试证明此运动平台场景的接线与事务确定性，不是全平台滑移/碰撞/离台行为的人工或UE轨迹验收。
- 相机/Ragdoll/Get-up 60 Hz、480帧通过；实际渲染60 Hz、1700帧退出0，35张截图。检查485/495的方向切换姿态、1140的补步与1560的蹲姿；所检查区域没有姿态爆散，蹲姿瞄准仍裁腿，不能据此通过完整脚部观感。所有最终Godot日志无错误/警告/异常；没有全量测试、十分钟性能或人工签收。

## UE 资产证据

沿用UE构建诊断skill，完整Editor目标0actions，ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp四插件审计通过；日志前缀 `Saved/Logs/PluginBuild/20260926T093708104Z-48f3c475fa524f12a47655b69f074c2d`。BuildId `2192dbcd-0924-430b-9a3d-1daff6c15a77`，fingerprint `4CE25A651115C8ABA1D5AA81FCFC0A97C638C958D714D73DABA212A75B25F83E`。

最终冷导PID37984、普通Editor PID45276均退出0，JSON哈希同为 `BDDC0B0117F859D810672FFA195FE8538F1395DAA27AD2FB48B3293D386D4AAC`；只导出IgnoreBaseRotation的中间两次另保留。普通Editor两旧Condition failed和五旧警告仍在。只修改Python只读导出，没有改UE C++/插件/配置、保存资产或执行DataValidation/打包。

## 后续

源Notify/NotifyState仍依赖旧移动时钟，下一批要把真实原播放器的notify tick上下文接到共享队列及类型化消费者，再移除兼容更新。完整Character→Parent→Crouching/Grounded/Locomotion原生连续对照、上身/Overlay/最终脚部、Mantle/Root Motion/物理恢复/相机矩阵、十分钟性能与可复现交付仍待。音频、道具物理、头颈专项继续暂缓。
