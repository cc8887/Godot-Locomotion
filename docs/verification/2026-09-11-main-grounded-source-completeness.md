# Main Grounded 正式来源补完

日期：2026-09-11。完整性修复第四十三批，沿用现有未提交工作区，不 commit/revert。

## 结论与边界

新增 Main Grounded 的三个原生动画来源已进入正式编译、来源布局、P5 主快照和
Godot 实际资产库。两个站蹲转换可使用既有共享 Sync/Notify 候选事务，From Roll
保留显式采样身份，不产生时间推进或通知。未新增独立时钟，也未改变既有编号。

本批没有接通完整 Main 姿势图：实际 Standing 上游 Main 权重仍是既有固定值，
蹲姿仍不是完整原生子图，From Roll 的 FootLock 覆盖、QuickFeet 骨骼混合、
Main 状态通知/玩法反馈和最终曲线尚待生产接线。它不代表 Roll/Root Motion 已完成，
也不是起步滑步或上身问题的新视觉验收。

## 原生证据与实现

导出器新增显式 `-IncludeMain`，要求恰好两个 SequencePlayer、一个 SequenceEvaluator。
不改变旧 `-IncludeCycle` 行为。正式表新增 mainSourceSchemaVersion=1 与对应 scope。
导出 92 图/35 动画资产；与旧正式表逐项比较，原 69 图、32 资产和全部六台编译状态表
语义完全一致，仅增加 Main 内容图及三个资产元数据。旧表已保存在
`artifacts/main-grounded-source-graph-before.json`。

| 原生状态 | 播放器 ID / 编译节点 | 样本 ID | 实际来源行为 |
| --- | --- | --- | --- |
| (N)->(CLF) Transition | 40 / 223 | 62 | ALS_N_to_CLF，非循环，PlayRate 引脚 1.2 |
| (CLF)->(N) Transition | 41 / 225 | 63 | ALS_CLF_to_N，非循环，PlayRate 引脚 1.2 |
| From Roll | 42 / 229 | 64 | ALS_N_LandRoll_F，ExplicitTime 引脚 1.5 秒，Teleport |

两个转换节点结构中的 playRate=1、恢复节点结构中的 explicitTime=0 不是执行值；
严格编译器读取暴露引脚，验证归属、同步、生命周期、速率变换、资产时长及 evaluator 模式。
原有 0..39 播放器和 0..61 样本编号不变。Core 新增 MainGrounded 来源分类；
Godot 固定候选缓冲同步扩到 43 播放器/65 样本，所有时间仍归现有来源身份管理。

正式布局现为 101 项，其中 SourceSample 51、SourceEvaluator 14。
原生图 233 个资产节点仍有 190 个未映射到此来源表，这不是功能完成率。
同步资产 35、标记仍 40；通知元数据 55 条，其中新增转换脚步通知 4 条和恢复动画的
源通知 3 条。恢复 evaluator 不进入 timed timeline，也不会因固定采样触发这 3 条通知。
脚步音频仍暂缓；保留通知元数据不等同于开启声音播放。

## 验证

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema | 1848/1848 |
| Import 全套 Debug，最终复跑 | 851/851 |
| Main 来源反例/语义 Debug | 22/22 |
| 来源快照/布局/通知及 Main 相关 Release | 104/104 |
| Godot 项目构建 | 0 警告、0 错误 |
| 实际 Standing/Stop/Rotate Controller | 30/60/120 Hz，5040 帧及重试通过 |
| 实际 Pivot Controller | 5040 帧及回滚通过 |
| 实际 Detail / Sprint / Turn Slot | 1890 / 1260 / 1890 帧，既有门禁通过 |
| Cycle Worker 单/多线程 | 各 180 帧，10 个来源事件，摘要一致 |
| late_source_event / late_transaction | 两种模式均通过，无事件泄漏 |
| 旧 P4 全验证脚本 | 通过，主动姿势与曲线零分配门禁保留 |

新增测试在 30/60/120 Hz 各推进 2 秒，改变 StandingPlayRate 仍按 1.2 倍推进
站蹲转换，末尾钳制，两个来源共 4 条通知，候选重试一致。From Roll 无 timed timeline。
这些是来源执行链测试，不是完整 Main 状态切换的 Demo 覆盖或完整 UE AnimBP 逐帧对照。

实际 Worker 摘要与上批一致：result=A9DF0647AFC3574C，
full_pose=04D4A5651B87E0E4，pose=2DED5435A66BCAEC，root=309E8D0E0BEEB2CB。
本批未改相机、输入、UE 原始期望或数值阈值，未新增视觉截图，不宣称视觉改善。

失败证据全部保留在 `artifacts/test-results/main-grounded-sources/`：

- import-first.trx：827 通过、两处辅助检查仍使用旧数量；按正式新增身份更新。
- import.trx：848 通过、三个新测试错误传入全零 group history；按既有 batch 合同
  首帧使用空历史，修正测试，未更改生产同步算法。
- import-final.trx：850 通过，既有快照视图零分配断言测到 3968 B；隔离复测
  allocation-isolated.trx 通过，最终 import-repeat.trx 全套 851/851。
  原因未定位，不将复跑通过解释为该间歇性风险已修复，也未放宽零分配断言。

## UE 构建和加载

按 ue-diagnosing-plugin-build-load 技能，完整 Editor 构建/全插件审计先于导出。
首次 UBT 运行未进入 C++ 编译：系统 .NET 缺少 .NET 10。
首错日志保留在 UE Saved/Logs/PluginBuild 的
`20260910T204534127Z-fb008160e32d41e89486b80ce7424586-ubt.log`。
随后仅对构建子进程设置 DOTNET_ROOT_X64/DOTNET_ROOT 指向引擎自带 10.0/win-x64，
未修改系统 .NET 安装。指定的额外调试/验证技能当前未提供，采用日志、审计与测试核验。

- 完整 Editor 构建和全插件审计退出零；构建指纹
  `9B6B4C94AC36931FCCE2D1ABDB5552FD910594DA0C7F75CA97BFFC4FC782DC5B`。
- 独立 BuildPlugin 退出零；包位于
  `artifacts/unreal/AlsMainGroundedSourcesPluginValidation-20260911`，未向项目复制 DLL。
- 打包后审计通过，项目本地插件与选定引擎身份一致。
- 两次冷导出和正式 JSON 的 SHA256 均为
  `D839633353E989817F118C3BF9342A47431F02C1013198F3E44F55C7EA14FF98`。
- 仓库及项目部署源码 SHA256 一致：
  `55618FA58CE07F72EC3BDBDC808959963C9A69D5B5649A2349F3498B4E0EECAF`。
- DataValidation 688 资产，退出零，0 错误/3 条既有资源警告。
- 普通 Editor PID 35852 冷启动、插件加载和初始化成功，CloseMainWindow 后正常
  关闭，日志结束于 Exiting；本次未取得数值退出码。仍有与前批相同两条
  AutomationTest Condition failed，不能称为完全无错误冷启动。

证据日志前缀 `artifacts/main-grounded-`。所有本轮测试/Editor 进程已退出。

## 下一步

1. 完整 Main 内容链：Standing/Crouching 缓存与曲线覆盖、站蹲转换、From Roll
   恢复姿势、QuickFeet 骨骼 profile，结合真实状态权重、相关性/重入与候选回滚。
   蹲姿原生图缺口需要继续导出和实现，不能把旧近似蹲姿包装成等价 Main。
2. 接 Main/Slot 的 P5 状态/动作反馈，RotationScale、YawOffset 及全图最终曲线，
   再落实动态 Layering/Add/LS、Lean 和手部 IK。保持已有键鼠方向行为。
3. 用同输入/速度/朝向的 UE 与 Godot 整图状态、源时间、曲线和骨骼轨迹对照，
   连续截图和人工验收后才关闭滑步、交错步与上身项。
4. 按原 P5B/P5C/P6/P7 继续 Overlay/道具、Mantle/Roll/Root Motion、
   Ragdoll/Get-up/Pose Recovery/完整相机，最后执行十分钟 Release 性能预算。音频暂缓。
