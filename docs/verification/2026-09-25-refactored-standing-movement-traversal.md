# Standing 移动缓存单遍历与真实 Stop 基底

在 `.` main 接续 Stop 姿态合成。当前接通 Move/Stop 所依赖的移动缓存链，尚非完整 Standing 图或普通 Demo 接入。

## 实现

`AlsRefactoredStandingMovementTraversal` 使用原编译缓存顺序 `[66,67,133,136,138,137,132,134,135]`，将外层六读者、Details 六读者与方向二十六读者放进同一次 Core Drain，共九缓存、三十八读取身份。接受宿主已经生成的 Move/Stop 状态读取，不擅自制造 Idle/Rotate 的更新。

cache66 的最大权重读者决定源更新上下文；相同权重保留先到者。选定后按原顺序执行 GroundedMovement→首次相关 InitializeStandingMovement→StandingMovement，再准备 Details117、生成 Movement67 的延迟读取。入口回调 Leave 在 Source.Update 返回时完成，67 与方向缓存随后在同一次 Drain 中更新。

Parent 的 Gait、Pivot、Direction、播放速度、Forward、Yaw 与 VelocityBlend 使用本帧候选；Grounded amount、未加权 Running、Standing machine weight、FeetCrossing 仍由宿主提供。Stop 局部图在缓存延迟刷新之前读取 Parent，测试保持这一顺序。

`AlsRefactoredMovementTraversal` 新增共享调度模式，避免内部再次 Drain；完成统一 Drain 后才汇集原二十四个独立播放器身份，并允许提交。Core 公开只读 IsFinished 供完成检查，未修改缓存选择算法。

初始化沿用本机 UE 的初始化 counter 行为：没有臆造 SaveCachedPose UpdateCounter 同步。无读者时可以提交初始化请求，pending reset/instance reset 保留到下一次真正更新；失败和取消不推进这些历史。

cache66 的 skipped 上下文单独保留给外层惯性化消费者；67/方向缓存的 skipped 上下文继续交给119。外层118的实际请求消费尚未实现。

Stop sampler 新增直接接收 `AlsRefactoredMovementInertialization` 的入口，校验候选帧身份、角色代际、owner 和骨骼/曲线布局，并要求该帧已 Evaluate；整链测试经此接口读取 node119 的真实输出。原裸缓冲区入口仍保留给低层采样，调用者须自行保证基底身份。

## 验证

记录位于 `artifacts/refactored-standing-movement`；主目录 Release：

- 新增 4 项：一次延迟初始化/同权重选择/源时间未回填拒绝提交/取消重试/外来 reader 拒绝；三频率完整移动缓存与停止姿态集成。
- 30/60/120 Hz 共 1050 候选帧，真实 Parent 设置刷新→方向共享播放器与姿态→Lean/PoseMoving→Details→119惯性化→Stop。每帧取消重试，核对缓存选择、跳过上下文、播放器请求和 Stop 姿态/曲线一致。
- 覆盖 Move 与 Stop 同时读取66、Stop 淡出、重新移动及尾部无人读取缓存；每个缓存和播放器身份每帧最多更新一次。
- `related.trx`：Import 29 项通过，含新4、旧Movement traversal、Stop evaluation及Direction moving native 回归。原 native 误差预算未改。
- `core-cache.trx`：缓存遍历、求值与既有原生参考共41项通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

首次编译无法从 Import 访问 Core 的 internal IsFinished，改为只读 public 属性后通过。首轮运行 `integration-second.trx` 为2通过/1失败（30Hz，最终覆盖断言）；原四秒快速起停序列没有保证所有频率都有无人读取缓存的尾段。测试扩为五秒并增加明确尾部 Idle，拆开覆盖断言，最终相关29通过。初始失败记录保留，没有放宽数值断言或 native 门槛。

本批没有新 UE 导出/连续整图 oracle、Godot运行/渲染、全量或性能验收。因此这些结果证明本地整链连接和回滚一致性，不证明 UE/Godot 完整视觉等价。

## 下一步

补 Idle 与 Rotate 源遍历和姿态、完整 Standing65 状态姿态混合、外层118惯性化，以及 StopQuick/状态回调的实际动作播放；随后导出并比较原 UE Standing/Stop 连续整图，接统一角色宿主和普通 Demo。Lean update-only、其他 stance 与原定 Ragdoll/Get-up、动作、性能目标仍保留。音频、道具物理、头颈专项仍暂缓；用户已有修改没有纳入本批。
