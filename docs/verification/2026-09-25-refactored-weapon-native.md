# 四武器原生连续状态机对照

## 范围

承接 `6f7e57d` 的连续运行时，在实际 Bow、PistolOneHanded、PistolTwoHanded、Rifle Linked AnimGraph 上运行，未替换 transition rules 或给定预期状态。临时父实例只提供受控 RotationMode/Gait/LocomotionMode、Moving、TransitionsAllowed、PoseState 和 ViewState。

导出器扩展 `ExportRefactoredDefaultOverlayTrace`，按原生成类类型寻找唯一状态机。每帧保存：当前状态及时间、三个状态权重、完整活动过渡栈和原始 edge index、更新状态顺序、生成类通知局部索引；同时保留最终姿态/曲线与 SequencePlayer 时间/权重，供后续姿态与源时钟移植使用。

生成类实际通知为 index 0 `RelaxedToReady`、index 1 `ReadyToRelaxed`。队列按真实 Notify 对象身份匹配，未用状态端点或通知名字推断 index；通知 EventGraph 未执行，动作请求消费未验收。

## 运行结果

四图各有 30/60/120 Hz 三条轨迹，每条 `132 + hz` 帧，合计 **2,424 帧**。覆盖首帧直接进入瞄准、恰好 3 秒的多次切换、不同退回路径、冲刺/空中门控、零 delta、中断栈、显式重新初始化及 Aim_Out 尾段完成。每条轨迹六条过渡边均被走到，704 帧有多重活动过渡，72 个生成类通知顺序一致。

逐帧状态、活动栈长度/端点/边身份、更新顺序和通知队列一致：

- 最大状态/过渡时间或有效时长差：`2.9802322e-8` 秒。
- 最大过渡 alpha 差：`2.9802322e-7`。
- 最大状态权重差：`1.4901161e-7`。
- 各浮点字段预算保持 `2e-6`，未放宽。无需修改 C# 生产算法。

新增原生 12 项测试通过，相关 Import 回归 **82 通过、0 失败、0 跳过**。Godot Optimize 构建 **0 警告、0 错误**。本批无新编译/测试失败。

这是完整原始 Linked Graph 驱动下的状态机对照，尚未比较导出的武器骨骼姿态、曲线和 SequencePlayer 时钟，也未覆盖非默认动作隐藏分支/实际父实例 Refresh。不能作为普通角色或完整武器整图验收。Overlay 完整姿态证据仍为 9/13。

## UE 构建与冷/热加载

按 `ue-diagnosing-plugin-build-load` 执行完整项目 Editor target 构建，4 actions 成功；ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 全部审计通过。

- BuildId：`4cd31a69-ae92-41ab-8118-ffba44340a1a`。
- 输入指纹：`E2E1C13976DADBC138D835FCE2E125798A7E05A3A21F2EB33E7A0057E47DCA3A`。
- 构建/审计日志前缀：`20260924T223754871Z-c05a3a9c3ef146baaf5f944274f1d6d1`。
- 冷命令行导出实际 exit 0；普通 Editor PID 26488 实际 exit 0，四份输出逐字节一致。
- 普通 Editor 仍有两条既有 `Condition failed` 和旧 PawnActionsComponent、NavMesh、材质、MotionVectorSimulation、Crowd 警告，未宣称日志无错误。
- DataValidation 实际 exit 0，报告 0 errors / 3 既有 warnings。
- 原 Default 图重导实际 exit 0，输出仍为 `B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003`，共享导出路径未改变旧证据。

新参考文件位于 `assets/config/refactored_weapon_trace_{kind}.json`：

| Kind | 字节数 | SHA256 |
| --- | ---: | --- |
| Bow | 10469604 | C62FB4D8B4483344FC0FB2CA347F17FB5EFA8E432A410DF2C4F7222F8E8BC1D7 |
| PistolOneHanded | 10207687 | 392C2DF1127E772F7345564BAC7A3B4956971C8C4DD4648BA664300B393091D6 |
| PistolTwoHanded | 10251891 | CB7E63902F7E7EF49B52762CFB9332B0A23A27C84EC96D6F9F2C4CFEE4EC37C5 |
| Rifle | 10293768 | 7434BD48926A4B2DE63C9C19EBCD287055C55F1ACA9ACD908CBB718F9526AF4F |

日志、请求、重导和 TRX 在 `artifacts/refactored-weapon-native/`。未启动 Godot 场景、运行全量测试/十分钟预算或打包；项目尚无配置好的打包验证流程。

## 下一步

绑定生成类通知与原 EventGraph/父实例的过渡动画消费，然后完成四种武器的 state 子图资源播放器更新、整图姿态以及隐藏/重入对照。继续实际移动状态机、统一宿主与所有旧缺口；普通 Demo 尚未切换，Ragdoll/Flail/Get-up 尚未整体验收。用户修改和暂缓的道具物理、音频、头颈诊断均保留。
