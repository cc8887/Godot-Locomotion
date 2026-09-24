# Refactored 连续同步时间与姿态

主目录 main 扩展现有原生 Tick commandlet，新增 Refactored 资产选择和普通 Editor 可调用入口。实际调用 FAnimSync::TickAssetPlayerInstances，使用六方向 WalkRun、Sprint、Sprint Acceleration和Ground Lean共9原资源；不模拟AnimBP。默认V4资产选择保留，输出新增refactored标识。

## 连续链路

30/60/120Hz，每频率7场景×64帧：单BlendSpace、六方向组、等权重/变动注册顺序、零速/反向/高速/大delta、空帧后重置恢复、混合Sequence/Sprint及重置、独立Lean。共21条trace/1344帧。每场景初始播放时间是显式探针输入，后续C#仅用自己的时间/epoch/filter/cache/group/player/sample历史推进。

C#根据输入坐标运行新原生槽位filter、三角形权重和完整Sync资源表，再调用既有Core Sync。没有把native结果中的filtered值、权重、样本时间、marker状态或leader选择作为驱动；导出的输入rate/weight/reset等仅作为共同实验条件。逐帧比较leader、group ratio/marker位置、player时间/delta/marker、sample顺序/权重/时间/delta/marker，并从旧history再算一次验证重试。

新增 `AlsRefactoredBlendPoseSource.Sampler.SampleTimes`，允许每个样本使用同步后的独立秒数，保持原序列采样顺序、曲线存在性和完整姿态混合；输入先验证，结果全部成功后发布。旧normalized Evaluate复用相同混合函数，仍由原接口自主求权重。

在每条native轨迹的0/16/40/63帧，对所有BlendSpace实际native sample cache附加GetAnimationPose参考，共384组完整79骨姿态。C#使用自身同步输出时间调用SampleTimes作对照，而不是把native sample time传入生产采样器。Native参考侧使用自己的已导出实际cache作原生raw姿态求值；这不是完整AnimBP上游图、Notify执行或物理更新。

## 结果

- 1344帧、13283个BlendSpace样本状态、282次leader切换通过。时钟/filter/weight/group alpha等共同Near比较的最大误差 `2.9802322e-8`，初始预算3e-5；marker索引、symbol、leader和样本身份严格一致。
- 384pose/30336骨，最大位置差 `8.27294216932374e-14 cm`，四元数分量差 `3.3306690738754696e-16`；最大curve差 `5.9604645e-8`。初始预算P=.01cm/Q=1e-4/S=1e-6/curve=1e-4，为连续时钟误差传播预留；首轮通过，未放宽预算。
- 同步核心无需修改。新增1项综合测试；旧normalized pose、Look及相关Import总29项通过。Godot Optimize构建0错误0警告。无全量或Godot运行场景验证。
- UE两次完整目标构建成功，最终5actions、审计fingerprint `47422DC949DEDAB5E1066001556F94CCCAC99C0316D331F3B1EACEB090166BF5`。首轮完整构建还构建了NetCore，构建系统生成新BuildId `4cd31a69-ae92-41ab-8118-ffba44340a1a`；本批未编辑NetCore或BuildId，最终全部插件审计通过。
- 冷导出和普通Editor PID34280实际重启/导出/退出均0，最终参考字节相同，SHA256 `C3AAAC78874F02CD1DE0E2979FEC5917E31F188EB7F9CA1367A1F03B6DFF49CF`。未保存UE资产。
- 普通Editor保留两条旧Condition failed、五条旧警告，未修复。没有打包流水线/打包完成声明。
- 本批DataValidation实际退出0，三项既有加载/导航警告保留。

日志和TRX在 `artifacts/refactored-sync-trace/`，先时钟验证与扩展pose后的记录分别保留；没有测试失败或预算调整。

## 尚未闭环

此次证明现有Core与原资源的连续同步算法及每sample时间采样路径，组合调度目前仍在测试夹具。下一步把这一链路作为生产source-player owner接到实际Locomotion/Overlay图，统一帧提交、资源/播放身份、Notify、root motion及最终原生骨布局适配，再普通宿主。当前普通Demo未切换，也未宣称动作观感问题或Ragdoll完成。完整Mantle gameplay、Ragdoll/Get-up/Pose Recovery、Camera和十分钟性能目标继续保留，用户修改及暂缓项不变。
