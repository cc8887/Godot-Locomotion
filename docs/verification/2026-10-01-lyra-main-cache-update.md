# Lyra Main 与 Aiming 的三级缓存更新调度

本批将原 Provider Aiming BasePose → Main UpperbodyLowerbodySplit → Main Locomotion 的更新调度接入 `LyraMainLocomotionHost`，替换此前仅取两条 AO 权重最大值的捷径。使用现有 Core `AlsPoseCacheTraversal`，不新增播放器、时钟或 Sync。只关闭缓存更新调度及共同角色组合；缓存姿态复用、初始化/重入、活动 Montage、主惯性化和完整 Main 仍开放。

## 原节点和顺序

安装版 UE 5.8 `FAnimInstanceProxy::UpdateAnimationNode_WithRoot` 先更新函数根，再按该函数的原 saved-pose queue 执行 `PostGraphUpdate`。Linked Aiming 函数的 queue 在回到 Main 后者的 queue 前完成。

| Owner | 原 cache / readers | 原 queue |
| --- | --- | --- |
| Provider FullBody_Aiming | BasePose78，reader76/75 → LinkedInputPose80 | 78 |
| Main AnimGraph | Split78，reader77 → PreAim Slot2；Locomotion83，reader82/80 → LeftHand1 | 78、83 |

两个 Owner 的78不能当作同一节点。局部调度将 Provider 索引偏移103，得到181→78→83；偏移和两份原 queue 写入新独立 policy，并校验原图链接、cache名称、依赖字节和 Main split 设置。

原 `FAnimNode_SaveCachedPose::PostGraphUpdate` 选完整最大权重上下文，相等时保持先到者；不求和、不按源相关性阈值过滤缓存读请求。保留 Weight、Delta、Active、RootMotion modifier、SharedContext 和惯性请求/跳过路径身份。无 reader 时 GlobalWeight 归零；获选上下文无 shared message 时不创建跳过通知。结果不可变，整帧失败/取消后重新遍历不改变上一提交状态。

Main split 原 upper-before-base，目标 root mask=0。上身路径的 RootMotion modifier=0，base=继承值；默认无 Montage 时两支相等，选择先到上身路径。控制 Slot source 权重下降时可改选 base，必须保持这一完整上下文差别。运行时结果已用于 Main machine / LeftHand / sources 的权重与 Active 选择；modifier 被保留供后续完整 RootMotion 路由，不能称游戏 RootMotion 消费已完成。

## 原生对照

外部 `AlsLyraMainCacheLibrary` 读取原 Main/Unarmed/Pistol/Rifle 类中的真实 Save/UseCachedPose 节点及其 queue，实例 Outer 为独立 transient SkeletalMeshComponent。明确重接的是各 cache 的 source 和已解析 Slot source 上下文边界；没有动画采样、pose Evaluate、初始化、Montage 播放或真实 node75 惯性消费。

UE public header 未导出 `FCachedPoseSkippedUpdateHandler::TypeName`。外部诊断模块按本机原 `AnimNode_SaveCachedPose.cpp` 的相同定义补该名称符号，解析到同一个 interned FName，使用真实 handler 类型，由原 Engine cache 负责分派；未改 Engine 文件或替换原 cache 方法。

三Provider、30/60/120Hz、每组4秒，共2520帧：5931次源更新、2634条跳过路径，含同权重、顺序反转、零/微小权重、部分/缺失 shared context、Active变化、两种RootMotion modifier、Slot source阈值两侧和空读。Godot逐项与原生比较上下文、更新顺序、跳过数和三个GlobalWeight，float逐位一致。全部2520帧重试输出相同，60次异角色上下文拒绝后恢复；3009次Active、366次零RootMotion modifier、240帧空调度均覆盖。

两次独立UE采集均实际退出0、0错误、692警告，fixture语义相同。警告为原资产脚步GameplayTag缺失及现有Editor/DSL插件信息，本批未修复这些资源；无最终ensure/错误。508个资产包和688份旧JSON保持原字节。新ignored文件为 `main_cache_v1_{requests,policy,native}.json`，分别810264 / 369 / 956079字节；没有保存资产。

## Main 共同组合与回归

`LyraMainCompositionScopeSmoke` 真实14入口组合使用新三级调度：11340帧、9762姿态、33525次cache source更新、12257条跳过路径。clean/retry全通道一致，207次晚期取消重试、2604次坏绑定拒绝和84次足部查询故障恢复通过；共同Sync和上一最终反馈门禁保持。该组合仍使用inactive槽、解析地面和受控曲线，不能称完整Main联合原生或生产物理验收。

原 Main ALS LocomotionSM边界连续回归11340帧、9762混合姿态、12595根姿态通过，原位置/旋转/scale阈值不变。新增三场景均实际退出0、无Godot ERROR/WARNING。最终Debug / Optimize ExportRelease各0错误0警告。未修改Core/Import，也未重跑其全量测试。

验证入口：`python tools/verify_lyra_main_cache.py`。新原生/回归日志为 `artifacts/lyra-analysis/main-cache-*`，脚本同时校验旧JSON、资产包、workspace/source/package探针源码、原queue和实际进程结束标记。

## 失败证据和下一步

首次UE编译局部`PI`与Engine宏冲突，第二次链接遇到未导出的消息名称符号；第三次构建成功后，首采集因实例Outer和无shared context时无条件挂消息触发ensure/assert、退出3。已修正且保留前3轮build日志与首采集失败日志；最终第4轮完整外部插件构建和两次采集通过。没有修改缓存选主算法、阈值或跳过失败门禁。

下一步将原cache求值复用和初始化/重入接到完整Main宿主，接入原五个真实Slot/Montage及主惯性接收，再最终ControlRig、完整联合native、Provider换类、统一Notify、Godot地形/平台和普通Demo画面/性能。既有Foot初始存储重复性问题仍开放。本批没有提交或推送，用户改动保留。
