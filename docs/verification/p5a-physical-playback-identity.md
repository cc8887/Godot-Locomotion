# P5A 物理播放身份验证

日期：2026-09-09。基于 `feature/p5a-events-actions` 的 `35ef623`，实施已批准的布局 v2 修订。

## 范围

- Base22、Turn16、Rotate8、Transition1、ActionMontage1、ActionSequence1，共 49 个 handle。
- Core binding 结构版本仍为 2；布局版本升级为 2，旧版本拒绝。
- Base Sync 仍为 17 项；脚部曲线仍为 34 份动画资源；authority 仍为 4 个。
- Import 展开每个物理 handle 的事件定义；Godot 适配器发布 46 个 Base/Turn/Rotate 描述符和不重复的节点名称。
- Oracle 事件投影使用 `EventId + RequiredOccurrenceHandleId + BoundaryOrdinal`，不再假设 EventId 在物理副本之间唯一。
- 原 P4 图、输入、相机、motor、动画导出资产未修改。Task 15/16/17 的图构建、epoch 提交及生产链集成仍按后续任务推进。

## 已执行

- [x] 新增 Import/Core 双 bank 测试在旧实现下失败，分别报旧版本/无效布局头。
- [x] Core 布局合同测试 49 项通过，含零分配校验和拒绝重复物理身份。
- [x] Import 全量 522 项通过。两个真实 Turn/Rotate 测试验证同动画、同 epoch、不同时间和权重，双 cursor 独立且仅权限获胜者发布事件；错误复用 handle 原子拒绝。
- [x] Core Release 选定回归 1372 项通过，过滤器 `FullyQualifiedName!~AlsP5aGoldenTests&FullyQualifiedName!~TraceSchema`，包含 P3/P4 golden。
- [x] Godot Debug 构建 0 错误/0 警告；真实 `p5a_runtime_binding_smoke.tscn` 返回 `P5A_RUNTIME_BINDING_OK`。
- [x] 完成 Godot `--import` 后，`verify-p4-pose.ps1` 完整通过。图摘要 `B7898554C2D7546B`，Aim `F3573F254CA93E48`，Turn `B9F62333553247F0`，Rotate `F1E8200C0D0B8C10`；单/并行 foot placement 和回滚通过，warm/active allocation 均为 0B。
- [x] 完整 UE Editor 目标构建和插件审计通过，使用引擎自带 .NET 10。重新编译 Trace 模块时仍有原有 P4 movement-base API 的 C4996 弃用警告，无新增依赖警告。
- [x] Pester 4.10.1 原生源码合同 `P5a13cRed`：13/13 通过。
- [x] Oracle 当前 Core 投影来源检查通过。
- [x] 事件投影修正后，golden `Family14_ComparerEnforcesToleranceDiscreteIdentityAndSameEngineByteRules` 通过（94 秒），包括 374 帧跨实现比较及负向校验。
- [x] 真实 UE 双次采样、canonical 转换和原子 golden 发布，返回 `P5A_GOLDEN_GENERATION_OK cases=8`。374 帧原生观测数据与旧 fixture 相同，仅布局版本和相关摘要更新。
- [x] 使用独立、空的临时 staging 目录对已发布 fixture 执行 Oracle `--verify-fixture`，退出 0；layout、bindings、graph、plan 摘要均匹配。

## 诊断记录

首次 UBT 启动因系统没有 .NET 10 失败，改用引擎自带 runtime 后通过；未安装系统软件。生成器先后拒绝旧 Oracle build manifest、未同步的部署测试脚本、早于脚本的 Trace DLL，以及 C++ ReadyCheck 的旧绑定摘要。均修正输入或真实重建后再执行，没有修改 DLL 时间戳或放宽证据门禁。

P4 曾数次退出 -1 且无异常栈；主目录旧构建作为只读对照运行正常。执行当前工作区的完整 Godot import 后，完整 P4 验证通过。没有据此断言最初退出的根因已确定。

Pester 默认选到 6.1.0 时，旧套件的 BeforeAll 作用域失败；按原工程使用的 4.10.1 重跑通过。未改写套件以适配新 Pester。

首次 canonical pair 转换报 `P5A event definition is ambiguous`。同错误由 golden Family14 回归独立复现后，修正完整事件查找键；重复的完整 key 仍然拒绝。

## 摘要与保护

- layout：`f2336240d749284b`
- binding：`40f33e59692dfd38`
- graph：`44403c2869d8f615`
- native plan：986747 bytes，SHA256 `7909b939803f60a657adf19867f379cb6fbc93c8001df1c37e05f3a603aa6f0b`
- 已发布 native fixture SHA256：`7B8B953E5E4E9E7DB41B069B0777B87DAD8F9AA2879DFE73BAE52FB7CE3DCC84`
- 用户 main 目录 P4 计划 SHA256 保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
- 相机、输入、export lock 均与修改前 SHA256 相同。

未宣称运行全部 Pester/Schema 对抗矩阵、人工 Demo 验收或十分钟性能认证。未合并 main，未推送远端。
