# Lyra 四类 Montage 事件与 ALS 人物路线

2026-10-03。按既定方案继续使用原 ALS 人物、骨架与现有 Godot 主目录；实现依据为本机 UE 5.8 原源码和实际资源，不展开 5.8 / 5.9 差异。最终 Debug / 实际 Optimize 共24次 Godot 运行、200项 Core 与独立审计全部通过。本批关闭下述内核、生产者与 Emote 消费者；完整迁移目标继续 active。

## 人物资源与 Animation Interface / Layer

原目标仍为 `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin`，源人物为 Lyra `SKM_Manny`。可以使用 ALS 模型及其蒙皮骨架。当前模型绑定逐骨校验 68 根蒙皮骨；源动画使用 69 raw，图与控制使用 81 logical，最终仅发布原 68 根蒙皮骨。

69 raw 在原 68 骨之后添加 `weapon_r`，父骨为 `hand_r`。81 logical 再包括原 ALS 十一根虚拟骨及 `VB IK_Hand_L_weaponSpace`。额外骨用于武器空间、采样、曲线反馈和 IK，不改变网格蒙皮。ALS 缺少 Manny 的 `spine_04/05`，所以重定向链、骨遮罩与控制参考必须按 ALS 目标重新绑定。原距离曲线、Marker、Notify、additive 基底、typed 属性和 RootMotion 随动画一起保留。

| UE 概念 | 本项目执行方式 |
| --- | --- |
| 普通 Blueprint Interface | typed 角色服务、输入命令、事件接收器 |
| Animation Layer Interface | 原编译合同生成 C# 参数与 Pose 包装，保留参数精度与输入姿态 |
| Animation Layer 函数 | 由实际图宿主准备、更新与求值；输出骨姿态、曲线存在性、typed 属性和 root |
| Linked Anim Layer 调用节点 | 独立调用身份、权重、相关性、输入姿态、参数与上下文 |
| Layer Group | 实现类与 Group 决定有状态实例；None Group 按调用节点分配 |
| Provider 实例 | 自己的状态机、缓存、source occurrence、历史反馈和本地 Montage bank |
| Sync Group | 实际访问源进入角色共同批次，一次同步后捕获各 owner 的源时钟 |
| Layered Blend / additive | 在 ALS 目标骨遮罩及原混合空间内合成，保留曲线、属性与 root 规则 |
| 最终骨骼发布 | Main 完成合成及最终 Rig 后，角色统一验证、提交，唯一 writer 写入 68 骨 |

Animation Layer 是有输入姿态和参数的图函数；Layered Blend 是按骨遮罩混合姿态的算子。两者均需实现。当前 Unarmed / Pistol / Rifle 的十四入口有真实图宿主，1 / 3 / 4 / 14 实例布局已有原生对照。同类 Link 保留实例，换类使旧实例退休，新实例从原 CDO 初始化。通用 default / self / Unlink / 部分覆盖的实例归属已有原生矩阵，完整图执行与其它 Provider 尚未完成，不能把三个现有 Provider 的验证范围扩大。

## 本批原生时序

安装版 `Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp`：

- `UpdateMontage` 561 行先更新全部实例权重，然后进入 `Montage_Advance`。
- `Montage_Advance` 2275 行开启排队，空 bank 也进入该阶段。
- 四个 `QueueMontage…Event` 在 2452 / 2464 / 2488 / 2500 行按当时阶段选择立即触发或入各自容器。
- `TriggerQueuedMontageEvents` 2576 行先关闭排队，按 BlendingOut → BlendedIn → SectionChanged → Ended 派发。每类容器在轮到自身时复制、清空；回调添加的后一类事件可在当前派发执行，同类新事件可留到下次。
- 每项先执行排队时捕获的单实例 delegate，再读取当前 global multicast 并广播；资源 Notify 位于 Montage 委托之前。

`AnimMontage.cpp` 的 `UpdateWeight` 2039 行先捕获原 `Blend.IsComplete()`，再执行 `Blend.Update`；仅未停止且从未完成变为完成时产生 BlendedIn。首次 `Stop` 1561 行在混合重置和 active lookup 移除后产生 BlendingOut；对已停止实例缩短混合不重复产生该事件。`Terminate` 1740 行捕获实例身份、中断状态及 Ended delegate。

本批实际参考中，BlendedIn 的立即回调出现在 weight 阶段；自动淡出和结束在 Advance 入队，最终由 dispatch 执行。保留 dispatch 的窗口使混合完成进入队列。

## Godot 实现

`AlsMontageEventQueue` 持有四个实际容器与捕获的 delegate。派发逐容器复制清空，支持队列内同步嵌套触发，以及回调重新开启排队后的同类保留、后一类执行。global 绑定在触发时读取。

`AlsMontageRuntime` 分开全部实例的 weight 与 Advance 两遍处理，保留原采样、RootMotion、branching、Notify traversal 和最终混合数值。三个真实生产者分别发出 BlendedIn、BlendingOut、Ended；事件携带物理实例、动作定义、Montage 和中断身份。当前原库都是单 Default Section，没有虚构 SectionChanged 生产者。

队列使用两个复用缓冲，候选随 bank 验证、提交或丢弃。Lyra catalog 显式启用事件捕获；旧 ALS owner 仍走其已验证的 action outcome / 通知消费者，不额外积累无人派发的委托队列。旧四项零分配门禁保留原零字节要求。新 delegate 派发复制快照仍有分配，未宣称该新路径零分配或完成性能验收。

Emote 删除从 Candidate / Traversal 推断淡出、结束和中断的兼容逻辑，改用真实事件载荷与物理实例身份。立即事件仅在真实胶囊移动成功后发布，候选准备和物理前失败不执行玩法回调。已消费载荷成为动画重试凭据，重试逐项核对并确认，不能重复派发或伪造另一载荷。排队事件在角色最终发布后、Main 资源通知之后执行。原自然淡出后中断不结束 active Ability 的规则保持。

立即事件目前服务原 Emote，允许对已经成功的物理实例发布受控本地效果。任意立即回调修改尚在 weight / Advance 中的 bank、回调解绑/销毁导致原循环变化及异常恢复，不属于本批已完成范围。

## 原生参考与验证数据

新增可选 `LyraMontageDelegateOracle`，内部模块沿用原名，复制前批探针并新增独立函数及 listener。原 WholeMain / Linked 探针源码和图执行字节保持；原 `.uasset`、项目描述和 Config 不改。通过临时 GamePreview 世界、原 Manny、原 Montage、真实 `UAnimInstance` 执行，资源 Notify 副作用主动排除。

两个独立 UE 进程得到完全相同的请求与 trace。每次共21条：三个实际容器案例，以及六种模式 × 30/60/120Hz；8823行，其中实际播放8820帧。容器案例覆盖四类顺序、captured delegate / 当前 global、同步嵌套和重新 Advance。实际 Montage 模式为自然结束、替换、零秒停止、再次缩短请求、反向和保留派发。再次缩短是对原 asset lookup 的请求：首次停止后 lookup 已移除，后续请求被原 API 忽略，不把它称为实际物理实例缩短的原生证明。

真实播放共72个实例事件：BlendingOut24、BlendedIn24、Ended24；原生每项还有随后 global 回调。三个容器案例共23次 callback。保留阶段105帧。Godot 每配置对照95次 callback / 855字段、8820次取消重试与完整候选载荷/物理状态；参考只用于断言，不作为执行输入。

最终检查项如下，24次运行全部实际退出0，无 Godot ERROR/WARNING；`artifacts/lyra-analysis/montage-delegates-v1-integrity.json` 的 `auditPassed=true`：

| 检查 | 每构建范围 |
| --- | --- |
| 新事件参考 | 21轨迹 / 8823行 / 8820 retry |
| 原 Emote Ability | 54轨迹 / 27720帧 / 同数retry / 1774080比较 |
| 真实 Emote 物理 | 30/60/120Hz六角色，共10080移动及两类retry |
| 原 Warp / Root | 60Hz六角色各2880移动及retry |
| 普通十角色与 E | 十四独立实例、换类、武器动作与角色统一取消提交 |
| 原 ALS 普通入口 | 60Hz / 1700帧，原 Pivot / Rest 与姿态链 |
| 原完整 Main 最终 Rig | 三Provider × single/per-call，2160参考帧及同数retry |
| Core 定向门禁 | 200通过，含原数值及零分配、新容器与凭据拒绝 |

Debug 与实际 ExportRelease / Optimize 使用各自真实程序集，两次最终构建均0错误0警告，优化两轮运行后按六文件 SHA 恢复 Debug。源码清单70份，既有869 JSON、710 UE包及9项目配置字节不变。三频 Emote 物理报告、普通十角色与 E 输入完整报告在两构建完全相同；普通两报告还与前批逐项相同。运行、程序集、报告、原探针不变和证据摘要由独立审计核验。没有本批 GPU、资源重导、全量 managed、十分钟或性能验收。

首次创建队列每帧复制导致四项零分配测试失败，改为复用缓冲并明确 adapter 捕获范围后200项全过，原失败TRX保留。可选探针首次 `TSharedRef` 序列化引用错误、Godot测试的成员名遮蔽/错误路径常量编译错误均修复留档。首UE进程执行成功且退出0，但 launcher 缺 stdout 参数，成功标记门禁失败；第二次保留完整 stdout，成功标记与独立 trace 一并验证，第一轮不冒充通过的 launcher。

## 继续开放

本批关闭四容器内核、指定单Section生产者与原 Emote 的真实事件消费者。完整通用 Montage delegates 尚未完成：任意 bank 修改/再播放/停止/重绑、弱对象存活及销毁、真实 Section 跳转和循环、Ended 对 active NotifyState 的结束顺序还需实现和原生参考。

Linked 私有字段明确范围仍34/47；余下十三配置/引擎字段、共享 Main Montage 模式变化、非空左手 Sequence、default/self/Unlink/部分覆盖、同函数多调用节点和其它图仍开放。原完整 Chaos/Jolt同输入运动314/1680帧差异未在本批复测。近景握持与脊柱变形、复杂地形、独立导出、跨平台、GPU专项、十分钟与全量/性能均未验收。音频、道具物理、头颈专项保持暂缓。
