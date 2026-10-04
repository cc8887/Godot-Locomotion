# ALS 人物上的 Lyra 骨控制初始化与骨引用缓存

继续主目录的完整 Lyra 移植路线，使用既有 ALS skin68/raw69/logical81 和十四接口入口。完整目标仍 active。本批关闭范围仅限原固定 ALS81 配置的八类节点实际初始化与 CacheBones 接线。

## 本批实现

三个 Provider 的真实 FullBody_SkeletalControls 阶段回调，现已连接 HandIKRetargeting 103、CopyBone 102、Root ModifyBone 104、右/左 TwoBoneIK 110/109、FootPlacement 105、LegIK 107、Weapon ModifyBone 106。Initialize 先访问 ComponentPose 子节点，再执行原 SkeletalControlBase 和派生节点的重置；CacheBones 在子节点前绑定本节点骨引用。阶段遍历本身不运行源 Update、共同 Sync、姿态求值或通知。

初始化只清除 AlphaBoolBlend 和 AlphaScaleBiasClamp 的 initialized 标记，保留嵌入混合值、ClampValue 与 ActualAlpha。Foot 清除定义明确的 pelvis/leg 插值弹簧并置 FirstUpdate；DeltaTime、短计数、character/root 数据保持。Leg 缓存在固定 FK 骨名下保留已有 BendDirection 历史。实际更新继续进入现有候选/取消/提交事务，没有改动 IK 求解公式或误差门槛。

实际 Provider 构造时延迟骨绑定，八次真实 CacheBones 回调分别建立原骨引用与 Foot 参考长度。缓存未完成时，求值在任何姿态写入前拒绝。重复缓存不改变现有 Foot/Leg 历史。当前目标骨架不可变；没有宣称支持动态 RequiredBones 或 LOD。

两个独立组合夹具没有外层 Main 阶段 owner，现显式执行真实 Provider 骨控制初始化与骨缓存后才运行组件。MainPose 的独立参考通过完整 MainPose owner 执行原全部启动，仍独立使用既有合成运算求值。普通生产 MainPose 已拥有阶段生命周期。没有给生产路径添加自动 eager fallback。

## 原 UE 证据

临时 LyraSkeletalInitializeOracle 加载原 Main、三 Provider 和 Manny carrier，在原代理中使用既有 ALS81 投影。逐节点设置两轮非零 alpha/bool/clamp、Foot 定义明确的插值与保留字段、Leg 链历史，再调用原 Initialize_AnyThread 与 CacheBones_AnyThread。未重写引擎初始化算法、修改引擎或保存原资产。

最终 skeletal-initialize-v2 与 -repeat 两个 UE 进程的 requests/native/closure 逐字一致：三 Provider、24 节点、48 种子记录。Godot 比较 162 项骨引用、原 Foot 四参考长度和 ALS81 参考姿态；长度精确相同，姿态沿既有严格门槛比较。六次重复缓存、十二次 pending 拒绝、六次隐藏取消重试、24 次缺失缓存拒绝均包含在初始化场景。

骨架投影沿原 Python unreal.Quat 的实际 MakeQuat(float) 边界。第一版 probe 直接保留 double 输入导致 weapon/VB 参考姿态偏差；最终调用原 MakeQuat 的 float 边界修正。未放宽参考姿态门槛。

原 Foot 使用 SetNumUninitialized，未定义的其余 Plant/InputPose 存储不能当作初始化后的确定字节读取。本批仅比较已定义的插值及保留字段；完整 Foot/Leg 私有存储和完整 FGraphTraversalCounter 全局帧历史仍开放。

## 验证与保护

最终 Debug 与实际 ExportRelease Optimize 均0错误0警告，关联 Core163项通过。每构建45个主矩阵场景加四布局完整 Main，共49个 Godot 进程；两构建98个进程全部退出0且无 Godot ERROR/WARNING。初始化24节点/48种子、162骨引用及真实缓存门禁通过。骨控制更新、FootPlacement、LegIK、组合骨控制各3780帧连续参考；Main Skeletal/Composition/两个 MainPose 各11340帧，Main Rig7560帧。Sequence/BlendSpace/原阶段、初始 self/Link/Unlink、普通十角色/Emote及四布局最终 Rig 均通过。

四布局每构建4320帧及同数 retry 对照既有完整 Main 原生参考，字段34/47与多组32/47比较范围保持。初始/解绑计数及普通十角色/Emote完整 JSON 同 Debug/Optimize 和 source-initialize-v2 基线。独立审计通过24份冻结源、其余1474条基线文件、870资源 JSON、710原包、9宿主/配置、23逐字原 UE 源码副本及三轮 Optimize 六文件恢复；Optimize 主 DLL 与 Debug 不同。原生最终两进程各744条既有加载/GameplayTag Warning，无 Error/Fatal/Ensure。最终运行证据 skeletal-initialize-v6，原生参考 v2/v2-repeat，probe package-v4。

保留两次 probe 构建失败的包和日志：错误 Root 头文件路径、局部变量遮蔽与 protected 调用均已修正。早期 native v1/v1-repeat 初始化采集有效，但其骨架投影精度不用于最终 ALS81 对照；v2 运行首次参考骨 68 失败证据和20源/12程序集保留。

最终扩展矩阵 v4 的 MainSkeletal 旧断言在隐藏帧要求传播当前 Aiming 参数而失败。实际 Linked 函数未访问 Update 时保留参数；v5 将断言改为分别精确检查访问时传播与隐藏时保留，生产参数逻辑不变。v4 的24源、Debug/ExportRelease十二程序集和失败报告已归档，全部比较阈值保持。

v5 的 MainPose 首帧提交状态比较失败，诊断快照只在四个未访问的 HipFire ResetPending 上存在差异：参考只执行骨控制启动，完整 MainPose 执行全部 Provider 启动。v6 对参考执行完整原启动，保留独立合成运算与完整状态/取消/隔离断言。v5 的24源、十二程序集、失败报告以及 v6 诊断快照/日志保留；最终运行使用 v6。

## 保持开放

完整 Provider 阶段调度与 Update/Evaluate cache 统一、后续完整图重初始化、动态 RequiredBones/LOD、Rig Construction、所有 Source/Foot/Leg 私有存储和非零 Aiming scalar 原生传播仍需推进。self scalar、部分绑定、重复调用、其它 Provider、Linked 字段34/47与多组32/47的原比较范围保持。新 ALS 默认最终整链原生验收、UE/Jolt 314/1680物理差异、近景握持、复杂地形及性能不因本批关闭。

下一批先追查 Main SaveCachedPose 78/83 和 Provider SaveCachedPose 78 的真实 owner 与生命周期，将阶段 counter、Update 最大权重上下文和 Evaluate 姿态缓存归入同一角色事务。保持原遍历与调用顺序，并以隐藏/重复调用/取消重试和连续原生对照验收；不能用另一份影子缓存替代实际节点历史。

音频、道具物理和头颈继续暂缓。本批没有新增 GPU 观感、全量 managed、十分钟或性能验收，没有保存重导原资产、提交或推送。完整目标仍 active。
