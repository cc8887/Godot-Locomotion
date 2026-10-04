# 完整 Main 的生产 Provider 重绑

2026-10-02，主目录 `.`。本批没有启动、修改或导出 UE，依据安装版5.8源代码、既有真实绑定采集和实际Godot运行推进。按用户要求不展开5.8/5.9差异。

## 实现与结果

普通Lyra入口已经支持Q循环切换Unarmed/Pistol/Rifle。更换实现类时构造新的14入口共同实例，保留原Main、角色及模型；同类重绑立即复用，不增加epoch或重置历史。完整Main原有观察、主机器/过渡栈、RootYaw及春簧、Turn/Pivot、直接Lean、两个同步组缓冲、Main75惯性、最终Rig和物理Montage bank均持续存在。

Debug/Optimize各三初始Provider×30/60/120Hz共9组5040帧，通过54次换类、54次同类复用和逐帧取消重试。另各有三Hz1680帧非零Main Rig曲线保留验证。实际GPU60Hz480帧生成7图，并抽查瞄准、蹲伏和空中3张原图。关闭范围为当前三类生产换层和以下实际运行/身份边界；整个Main连续UE联合姿态对照、多角色和通知/root物理等总目标保持开放。

运行方式沿用普通入口：

```powershell
& ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --locomotion=lyra --lyra-profile=unarmed
```

Q切换装备动画；WASD移动、Ctrl蹲伏、Space跳跃、右键瞄准。当前仍没有武器网格挂接，因此握持姿态为空手。

## 原生依据与归属

安装版 `Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp` 的grouped path按组创建一个Linked实例，类相同则跳过更换；对新目标调用 `InitializeAndCacheBonesForLinkedRoot`。`AnimNode_LinkedAnimGraph.cpp` 的 `InitializeSubGraph_AnyThread` 只初始化目标root，外部InputPoses的初始化另在 `Initialize_AnyThread` 中处理。换类不等于重建Main。

`AnimNode_LinkedAnimGraph.cpp` 的 `RequestBlend/Update_AnyThread` 在目标变化后保留旧BlendOut和新BlendIn，实际访问该调用节点时向惯性接收器请求。Godot在真实访问处登记请求；隐藏入口的旧BlendOut保留至后续访问，取消不消费，成功提交才清除。原14入口的blend配置仍来自原编译合同；现有三类相关时间都是0.15000000596046448，LeftHand/SkeletalControls为-1。

既有 `linked_layer_binding_native.json` 采集真正 `UAnimInstance.LinkAnimClassLayers` 八步：Unarmed→Unarmed→Pistol→Pistol→Rifle→Rifle→Unarmed→Unarmed。每步14节点同一owner、一个活跃Linked实例，owner为0/0/1/1/2/2/3/3。当前生产Unarmed轨迹的初始构造加前七次重绑，与此类顺序/实例代际逐步相符。Pistol/Rifle起始轨迹另实际运行同样的循环语义；本批没有新采集其连续UE动画轨迹，也不以此旧绑定oracle代替Main姿态/曲线/惯性的整链对照。

另外，`SkeletalMeshComponent.cpp` PostEvaluation先更新Main曲线，再对当前Linked实例调用 `CopyCurveValues`；新Linked实例的曲线副本和Main上一最终曲线归属不同。原Main73的启用绑定是 `DisableLegIK <= 0 && !UseFootPlacement`。本批将最终Rig读取的曲线反馈保留在Main宿主，避免重绑清空新Linked副本时错误重新启用Rig。

## 代码变化

| 文件 | 行为 |
| --- | --- |
| `LyraMainSourceScope.cs` / `LyraLocomotionSourceScope.cs` | 新scope承接实际Main宏/直接Lean/图历史及Turn反馈；旧scope撤销写Main的资格，旧Cancel不影响新帧；Linked初始化与Main初始化分开 |
| `LyraMainUpdateHost.cs` | 明确未完成Main宏候选，移交时拒绝这种状态 |
| `LyraItemLayerGraphInstance.cs` | 退休旧组，拒绝其调用和旧epoch/旧实例参数 |
| `LyraMainLocomotionHost.cs` | 构造并校验新完整目标、资源布局、相关源库存和缓存合同；保留Main机器/Sync，替换Provider相关源及局部权重；旧BlendOut只按访问/提交消费 |
| `LyraMainPoseHost.cs` | 保留Main惯性/最终Rig/模型帧历史；预构造新姿态算子/无独立时钟的Slot采样器，成功后替换；Main曲线反馈自持事务 |
| `LyraCharacterAnimation.cs` | 在空闲主物理帧边界重绑，保持原角色Montage bank/碰撞/skin发布器；新Layer使用独立epoch和source ID区间，同类快速返回 |
| `LyraLocomotionDemo.cs` | 正式Q输入接上述路径；增加实际重绑/候选门禁/曲线保留验证和HUD配置显示 |

所有C#文件位于 `src/Als.Godot/Animation/Lyra/`，最后一项位于 `src/Als.Godot/Locomotion/`。本批未改Core算法、资源JSON、默认ALS项目设置或UE资产，也未提交/推送。原主目录已有修改保留。

## 实际门禁

每次调用前记录Main观察、tail、机器状态/elapsed/完整过渡栈、Main图方向/Pivot、直接Lean、Turn反馈、Sync groups/players/samples/write index、Rig状态及启用曲线、惯性和模型身份/发布次数；重绑当刻要求全部精确保持。换类要求新epoch与共同14入口、退休旧调用/禁止旧scope写Main；同类要求完全复用。之后继续真实运动和完整求值，不只检查类名。

每组在移动Start、瞄准Stop、蹲姿Cycle、空中及反向Pivot切换，六次换类/六次同类。旧scope在新候选存在期间执行Cancel不得取消新Main；帧中尝试重绑必须拒绝；全部最终logical81→skin68局部发布值与转换结果一致，模型/Rig世界位置原门槛1e-4米不变。

| 门禁 | 最终结果 |
| --- | --- |
| Debug/Optimize构建 | 两配置均0警告/0错误 |
| 普通入口重绑9组/构建 | 各5040帧/5040retry、54换类/54复用、51帧中重绑拒绝、54晚期组件失效及恢复；十个真实Main state覆盖 |
| 非零Main Rig反馈三Hz/构建 | 各1680帧，18换类/18复用；三次明确将上一反馈设为DisableLegIK=1后换类，新Layer曲线为0，Main仍为1，真实Rig候选Blend.Target仍为0 |
| 实际GPU运行 | 60Hz480帧、6换类/6复用，7张1280×720图；抽查瞄准/蹲姿/跳跃时HUD配置与姿态可见 |
| 原LocomotionSM联合native回归 | 两配置各11340帧/9762姿态/246过渡；位置最大1.13687e-13cm，属于该边界固定Provider，非换层整Main oracle |
| 原Main含Rig宿主回归 | 两配置各7560帧/7296姿态，2097部分alpha、1479关闭、264隐藏、63故障恢复；地面为原解析夹具 |
| 原Main75/Slot联合native回归 | 两配置各40320帧/11250姿态、40320retry，RootMotion P/Q/S差0；保持原精度门槛 |
| 原普通ALS入口 | Optimize60Hz1700帧通过，3Pivot/4动态补步；无最终ERROR/WARNING |
| 固定Provider入口回归 | Optimize Rifle60Hz480帧通过，原skin位置/旋转发布差0 |
| 资源保护与恢复 | 818JSON/669UE包SHA256保持，六Debug文件从本批新备份逐项SHA256恢复 |

诊断反馈场景只在指定帧显式覆盖最终反馈中的DisableLegIK，验证归属与取消；正式玩法不注入此覆盖。模型发布值始终来自实际完整姿态。重绑改变源的实际历史和状态路径，例如空中重绑后的姿态来源；本批不把所有运行与未换层基准逐帧等同，也不声称UE/Jolt物理等价。

## 证据和复跑

- 最终Debug：`artifacts/lyra-analysis/main-rebind-guard-debug-{profile}-{hz}.{log,json}`；每组实际退出0。
- 非零反馈：`main-rebind-feedback-debug-{hz}.{log,json}`、`main-rebind-feedback-optimize-{hz}.json`。
- 原边界回归：`main-rebind-native-regressions.log`；已验证Main曲线归属改动，之后增加的退休/未完成宏门禁只作用于重绑，最终完整重绑矩阵再次复跑。
- Optimize：`main-rebind-godot-optimize.log`、`main-rebind-optimize-{profile}-{hz}.json`、`main-rebind-optimize-fixed.json`。
- 实际渲染：`main-rebind-render.{log,json,stderr.log}` 和 `main-rebind-render/*.png`。
- 自动汇总：`tools/verify_lyra_main_rebind.py` → `main-rebind-verification.log` / `lyra-main-rebind-verification.json`。
- 优化运行与恢复：`scripts/verify-lyra-main-rebind-optimize.ps1`；要求停止本目录Godot进程、干净Optimize构建和新的证据路径。

首个验证代码用`==`比较带InlineArray的transition stack，构建失败，日志 `main-rebind-build-demo.log`。随后一条命令误加载旧程序集，只有旧MainModel成功标记；`main-rebind-debug-first.log`追加明确INVALID标记，未纳入任何通过门禁。改为逐活动entry/Latest快照后重建通过，保留失败证据。最终曲线归属/退休门禁及全部最终运行另有完整证据。

上一批旧300帧直接P4夹具的AimOffset覆盖失败仍开放，本批没有修改或放宽该测试；当前普通ALS入口回归的1700帧成功证据独立保存。核验报告明确 `legacyP4SmokeAccepted=false`、`nativeWholeMainParity=false`、`productionAccepted=false`。

## 后续范围

当前关闭三类生产换层的上述边界，下一步多角色真正独立身份/事务及共享不可变资源验证；随后统一源Notify/Montage/typed消费者、RootMotion碰撞消费、武器挂点、地形/平台/完整视觉及性能。运行中换层的新整Main连续native仍需补采集。通用多Layer Group/self-layer/Unlink/部分实现/持久实例策略也仍需独立语义门禁。整个Lyra移植目标保持进行中，原ALS所有目标与暂缓项保留。
