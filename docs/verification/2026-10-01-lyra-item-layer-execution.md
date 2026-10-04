# Lyra 原 Main 图与十个移动 Layer 执行入口

2026-10-01，当前主目录；安装版 UE 5.8.1、Godot 4.7.2 mono。沿用 ALS 模型与 logical81/skin68。**本批完成固定 Provider 的十个移动入口归属和调用绑定；四个后处理入口、换类和普通 Demo 的完整生产接入继续开放。**

## 原资源与完整图

新增只读脚本 `tools/unreal/export_lyra_main_layer_graph.py` 和包装脚本 `scripts/export-lyra-main-layer-graph.ps1`，调用现有已编译的原图读取库，没有改动 UE C++ 或重新构建插件。得到 ignored `main_layer_graph_v1.json`：738217 字节，Main AnimGraph 49 个节点，以及 Unarmed/Pistol/Rifle 各14个闭包，共43个图，含原节点连接、初始化/相关性/更新回调和继承 CDO。

两个独立 UE commandlet 正常退出0；第二次重新读取后的完整 JSON 语义与首次一致，已有文件没有重写。508 个原包及645份旧 JSON 在每次采集前后校验字节 SHA256；采集源码和构建 source/package 镜像哈希一致。没有保存 UE 内容资产。两次 commandlet 汇总均为0错误/692警告，整份日志各有694条实际 Warning 行（包含汇总后的退出提示），主要为原资源 GameplayTag 与 commandlet 注册提示；不能将正常退出写成 UE 零警告。

该文件使用**编译节点索引**。例如 Main 的 Inertialization 是编译75、property27，LocomotionSM 是编译7、property95；不可把旧清单中的 property 索引直接用作调用身份。

实际下游顺序为：

```text
LocomotionSM → LeftHandPose_OverrideState → Locomotion cache
  → UpperBodyAdditive / UpperBody Slot 与按骨分层
  → FullBodyAdditivePreAim Slot → UpperbodyLowerbodySplit cache
  → FullBody_Aiming → AdditiveHitReact Slot
  → ApplyAdditive(FullBodyAdditives, alpha=0.6499999761581421)
  → FullBody Slot → Inertialization → RotateRootBone
  → FullBody_SkeletalControls → ControlRig → Main Root
```

缓存、Slot 和层内分支仍须按各自原 Update/Evaluate 图遍历实现；这个示意不替代实际连接或源访问顺序。特别是不能把 Main 最终惯性化直接搬到 LocomotionSM 后而越过后续层。

三个当前 Provider 的 `EnableLeftHandPoseOverride=false`、`LeftHandPose_Override=null`；这不表示可以删除该调用及其更新回调。原权重读取上一 Main 最终曲线。FullBodyAdditives 原 AirIdentity→LandRecovery 规则仍为 false，不因资源有落地 additive 就启用落地分支。

## 执行与所有权接入

- `LyraMainLayerGraphCatalog` 加载43图，验证四份依赖字节哈希、闭包内部连接、原根及单一14调用点，并保留 CDO 和完整拓扑。Provider 接口签名继续由编译合同校验，包含 Aiming 的 double 参数。
- `LyraItemLayerGraphInstance` 拥有十个移动入口的同一 `LyraLocomotionSourceScope`；Main 通过编译调用节点、Hook、实例引用和 epoch 绑定实际入口。一个组只能绑定一个 Main，多个角色可共享不可变资源但不能共享该实例。
- `LyraMainLocomotionHost` 实际根求值已通过上述入口执行，并从带候选身份的只读视图取得81骨姿态、曲线/存在性/Flags、typed属性和RootMotion。准备与源登记仍由原共同 scope 按真实机器遍历执行，一次共同 Sync，没有增加另一套时钟或骨架 writer。
- 视图的访问验证具体候选；提交、取消或进入下一帧后，读取旧视图会拒绝。正常混合与重复读取不额外推进 source。
- 现有 `LyraItemLayerInstance` 可显式持有同类 `GraphExecution`，构造时拒绝资源算子与执行实例的类不一致。普通独立 Demo 仍使用原简化路径；本批没有将这个可选连接描述为 Demo 已迁移。

这里 Hook 和调用上下文是带类型的执行接口；它不是已经完成全部14个 C# 姿态函数。新执行宿主只开放十个无姿态输入的移动入口，另外四个入口明确拒绝，未用旧的 `ResolveStateClip` 或局部算子冒充完整图。

## 验证

Debug 与 ExportRelease Optimize 均0警告0错误。当前最终构建通过：

| 验证 | 结果 |
| --- | --- |
| 自主 Main / 十根组件回归 | 7560帧、6330姿态、10状态/10根、49065次拒绝 |
| ALS81 连续原生重放 | 三Provider三Hz，11340帧、9762混合姿态、12595根姿态 |
| 类型化根输出调用 | 12595次，全部骨骼及数据通道保持原生门禁 |
| 新所有权 / 调用 / 视图门禁 | 510次拒绝，覆盖重复绑定、错误节点/epoch、同类同编号异角色、异角色候选、四个尚未接入入口及取消重试/提交后的旧视图 |
| 隐藏 / update-only / 晚期重试 | 165 / 1578 / 207 |
| 原包 / 旧 JSON | 508 / 645，字节哈希保持 |

混合位置最大差仍为1.0812455139964926e-13 cm，根位置1.0869213824647059e-13 cm；原门槛与曲线/属性/RootMotion比较未变。本批复用既有连续 UE fixture，**没有重新采集连续姿态 oracle**；新 UE 运行只读取完整图。既有 fixture 的 Marker 零值符号未编码限制保持。

首轮运行将 Main 误送入 Provider 合同查询，报 `Unknown compiled Lyra layer`；已改为单独验证 Main 所有者后重建/复跑通过。失败日志 `item-layer-execution-own-first.log` 保留，不计入最终通过。

证据位于 `artifacts/lyra-analysis/`：`main-layer-graph-ue-export-first/repeat.log`、`item-layer-execution-debug-final.log`、`item-layer-execution-optimize-final.log`、`item-layer-execution-own-final.log`、`item-layer-execution-native-final.log`、`item-layer-execution-verification.log`。`tools/verify_lyra_item_layer_execution.py` 核对完整图、原包、旧 JSON、编译镜像、正常导出与最终运行日志。

## 下一步边界

四个入口按原合同接完整 pose 包：Aiming 的 double 输入及两个 RotationOffset BlendSpace/缓存历史，LeftHand 的上一 Main 曲线与 Update 回调，FullBodyAdditives 的真实机器，SkeletalControls 的 FootPlacement/LegIK/武器缩放及剩余原链。后续源应汇入同一角色 Sync，不能逐 Layer 再建独立播放系统。

按原位置连接上身、Slot、惯性、RootYaw 和最终足部后，把 Linked 的曲线拷贝边界移至实际 Main 最终输出。随后统一 Notify/Montage、换类/BlendIn-Out、普通 Gather/Worker/Commit 与多角色、渲染和性能验收。本批只验证固定 Provider，没有证明新组执行宿主在同类重绑、换类或返回旧类时的完整生命周期。未提交或推送，用户未提交修改保留，整个移植目标保持开放。
