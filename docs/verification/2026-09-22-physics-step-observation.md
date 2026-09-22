# 原生世界积分边界观测

在主目录 main 增加只读 PreIntegrate、PostIntegrate、PreSolve 观测。两套模型前三步共 114 个动态身体样本，从原生独立输入调用 Core 积分后，COM 位置、四元数分量、线速度及角速度最大差均为 0。当前 Core 首步求解输入的姿态、速度和求解器质量也与原生一致；仍有惯量修正差异，尚不能声称完整求解或轨迹等价。

## 观测范围

沿用 PhysicsWorldContactStart/Frames 诊断窗口，在隔离世界的 evolution 回调中只读身体状态。记录 actor/COM 初态与预测态、速度、力输入、阻尼、质量和 conditioned inverse inertia。回调按 RAII 清除；数量达到原生并行积分门槛时拒绝观测，不修改调度 CVar。当前两场景 34/32 个身体，低于 40 的门槛。

独立冷导出及复导出均成功且字节一致，SHA256 `11E6254DD71EEA8DEF1BBA922341F642A5D1A81DCAA4C2D5649A18EBDDF5AD7C`。与上一批原生世界比较，48040 个身体样本的姿态、速度、awake 及每帧 awake 数量均未改变。

新增冻结窗口 assets/config/v4_physics_world_step_window.json，含两模型 0..3 步和 1..3 步的三个阶段，758148 字节，SHA256 `A74A703EF76B440511FE02A391590AA1035031459A0841F909B2A75D18603EE5`。旧 golden 未修改。

## 首步结果与后续定位

Compare-WorldStepInputs.ps1 使用已有 physics-step-time-20260922/captures，将原生完成步与 Core 零起始 frame 对齐，严格检查 mesh、身体身份和实际 dt。System.Text.Json 的 float 短字面量必须先恢复为 float；直接作为 double 相减会制造假差异。inverseMass 同样按原生 FSolverBody 的 float 存储边界比较。报告共 12 行，后两步会继承此前求解的轨迹差异。

首步 PreSolve 的 initial/predicted COM 姿态、V/W、solver inverseMass 均逐值相等。真实 inverseInertia 差异为：AnimMan 最大 4.896755583719634e-8（lowerarm_l），Mannequin 最大 1.862645149230957e-9（hand_l），单位 inverse kg*cm²。

UE 在 PostIntegrate 与 PreSolve 之间更新惯量修正，不能要求两个阶段的 conditionedInverseInertia 相同。首次测试误作此断言而失败，已改为仅检查积分状态不变；首轮日志保留。初版工具直接相减 float JSON 的假残差报告也保留，最终以 input-differences-stored.json 为准。

进一步源码核对发现 USkeletalMeshComponent::InstantiatePhysicsAsset_Internal 会用 BodyInstance.Scale3D / (DefaultInstance.Scale3D * componentScale) 修正 Pos1/Pos2，当前导入的 GetRefFrame 是未经过这一步的资产坐标。AnimMan hand_l 的 parent connector X 原始 26.975143432617188 cm，运行时为 26.97513217771087 cm。现有 nativeBodyComponent 不保留 BodyInstance.Scale3D，下一步需独立导出创建缩放与实际连接帧，严格绑定资产后重建，不能直接复制惯量修正系数绕过计算。

## 验证

产物均在 artifacts/physics-step-observation-20260922/。

- Import 新观测及已有初态/步长 4 项在 .NET 8.0.28、9.0.17 全过，114 个积分样本四类最大差均 0。
- 完整 UE Editor 目标构建和插件审计通过，fingerprint `35BE9C418CEA9FFD5E3C3C0CD790041D08E23CBF8457E0A8EE0D7A6BEC74F67C`。主仓库与项目插件源 SHA256 均为 `C2C2F41C0A16F68DC8AAB454849B77AF05B574CD6E6F8F9CE5344B7CB3B3463C`。
- DataValidation 成功，0 error / 3 旧 warning。普通 Editor PID 26932 出现类加载标记并原生退出 0；两条旧 Condition failed 与历史间歇退出 AV 未修复，不能凭本次成功宣称关闭。
- 本批没有生产 C# 修改，不重跑全量和矩阵；最近全量 Core 2872 / Import 2453+1 旧 skip，最近完整矩阵 9/12，三个 30 Hz 休眠失败保留，其中原生基线本身也未达门槛。

普通 demo 尚未接 Core 刚体后端。后续继续连接帧/惯量差异、稳定性与普通 Ragdoll/Get-up/Pose Recovery，再其余 Mantle、完整相机和十分钟性能目标。用户 P4 文档修改保持不变。
