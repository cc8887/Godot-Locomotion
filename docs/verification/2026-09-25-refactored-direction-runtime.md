# 原版方向状态机连续更新

在主目录 main 接入前批两套 DirectionResources。共享 Grounded 状态执行器新增独立 RefactoredStandingDirection / RefactoredCrouchingDirection 规则域；旧 V4 条件不变，旧输入不能驱动新机器，新方向条件也不能混入旧机器。保留原六状态顺序、每帧最多三次转换、初帧跳过混合/抑制 TransitionStarted、原出口优先级、过渡叠加和目标已有贡献时缩短过渡时长。

新的 Import owner 维护 Prepare/Commit/Cancel、显式初始化、遍历计数重入、候选状态/初始化列表/更新列表/类内通知。MoveDirectionChange 每骨骼贡献按有序 stack 计算，不把权重直接用于一次性旋转混合。此贡献仅是诊断/后续 pose 输入，完整姿态求值未在本批接入。

依据本机 UE `AnimInstanceProxy.cpp`，GetInstanceStateWeight 读取 GetRecordedStateWeight；不是当前更新中刚添加过渡的即时权重。本批在规则阶段使用上一轮 stack 对应的已记录状态权重，全部规则检查完再推进本帧混合。重置时 getter 贡献清零。站立 ActivatePivot 的类内通知仍仅是候选输出，尚未消费或派发到 Parent；不会套用旧 V4 的 Delay 逻辑。实际 Refactored C++ ActivatePivot 是速度小于 Standing.PivotActivationSpeedThreshold，后续须绑定真实设置和 EventGraph 消费。

## 验证

新增十项测试：两种 stance 的前后反向叠加、目标仍有贡献时不重新初始化、局部 ActivatePivot 索引/初帧抑制；两种 stance 的换髋等待来源状态满权重；六个连续频率场景。

30/60/120 Hz × 两 stance × 三秒，共 1260 提交帧。逐帧取消重试，与独立干净实例核对状态、活动边、通知和 79 骨×六状态贡献；累计 597240 个骨骼状态贡献，归一化误差预算 2e-6。包含零 delta、连续换向、多条未完成过渡、显式重置、signed counter 回绕、宿主 serial 间隔而 counter 连续，以及非法输入失败后无候选泄漏。

换髋针对性用例确认：曲线已解锁但来源状态未混合完成时保持方向；推进到满权重的当帧仍保持，到下一轮读取已记录满权重才发生转换。当前证据为源代码/资源结构和移植侧测试，不是 UE 连续方向 oracle。

`artifacts/refactored-direction-runtime/runtime.trx` 初八项通过；最终 `related.trx` Import 75 项通过，包含新增十项与旧方向图/规则回归；`core.trx` 共享状态机和过渡栈 38 项通过。`dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet` 0 warning / 0 error，未出现本批编译或测试失败。

无本批 UE 启动/导出、Godot 场景、全量测试、十分钟性能或打包。

## 未完成工作

下一步增加原生连续方向机器对照，核对状态/权重/stack/局部通知，再连接 state pose、缓存姿态及 SetHipsDirection / ActivatePivot 的真实 Parent 消费。其余 Movement Details、Standing/Crouching、Stop 状态图与统一宿主仍待完成。普通 Demo 未切完整 Refactored 链，不能把本批测试当作普通 Demo 交错步已修好。Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera、性能等所有旧缺口仍在目标中；用户修改保留，音频、道具物理及头颈诊断仍暂缓。
