# 四个 Main 地面根的共同 UE 原生对照

2026-10-01，主目录直接推进。使用安装版 UE 5.8.1 和 Godot 4.7.2 Mono，沿用 ALS skin68 / raw69 / logical81。此记录关闭四根共同执行的组件门禁；完整 Lyra 移植仍未完成。

## 真实捕获范围

在同一 Character、Main 与注册的 ItemAnimLayers Provider 中，执行原 Start10、Cycle14、Stop18、Pivot20 StateResult 回调及原子图。Pivot 使用 Main23 ApplyAdditive、Linked21、Lean22，Provider 内仍为原 PivotSM59、两套 Orientation/Stride 与 HipFire。其余三根保持此前原路径。

四根按给定顺序更新后，只执行一次原 UE Sync 与 PostUpdate，再分别捕获根姿态。三种 Provider 为 Unarmed/Pistol/Rifle，三种频率为30/60/120 Hz；24种根顺序、独立隐藏和重入，共3780帧、8400姿态，1680帧四根同时活跃、420帧全部隐藏。使用同一实际运动观察与 CharacterMovement 数据，不另算另一份 Main。

插入式 Tap 只在原 Main Pivot 根回调之后、原 Provider 之前读取字段，仍转发原 Initialize/Cache/Update/Evaluate。原对象链接在探针结束时恢复。没有替换原回调、机器或源算法。

**宏观 current、上一权重、根顺序与相关性仍由捕获请求提供。**实际执行的是原四根，不是由完整原 LocomotionSM 自己选边和混合后的最终姿态。通知队列、完整惯性路径和角色最终输出不属于本门禁。

## 资源与可复现捕获

合并150个序列地址，实际瞬态 ALS81 资源的 Marker 顺序在捕获前后读取并保持；距离 codec 和压缩根定义保持原合同。共同宿主使用这份真实联合清单。独立旧清单和资产文件不改字节。

首份 `main_ground_scope_requests/native.json` 已完成四根姿态和移动源检查，保留为初次证据。补齐 Cycle HipFire Marker 捕获后，使用新文件 `main_ground_scope_v2_requests/native.json`，没有覆盖首份结果。最终采集检查508个UE资产包和623份之前JSON（包含首份四根捕获），以及五个探针文件在源码、外部构建输入和实际插件包中的SHA256。

C++ 修改位于本项目外部 exporter，GASP58 资产没有保存。外部插件 BuildPlugin 成功；本批未构建完整 GASP58 Editor target 或执行四插件审计。两次最终采集分别正常退出0，内容一致。

## Godot 对照

`LyraMainGroundNativeSmoke` 使用同一 `LyraMainSourceScope` 和一次共同 Sync。每帧 prepare/cancel/retry、求值后取消、重新求值、坏epoch晚期提交拒绝，以及整帧预校验后一次发布；比较内容快照，确保取消不发布任一根的历史。

- Main观察和Tail、根回调顺序、Start方向、RootYaw模式、Pivot根相关性/方向/timer与Provider共享字段。
- 18900份移动源时钟/Marker检查，包含隐藏源保留历史；四根HipFire的15120份时钟/Marker、资产选择和混合权重检查。
- 三份Lean共11340时钟和22134样本检查，使用原float逐位比较。
- 活跃根动态Orientation/Stride参数、四根81骨姿态、曲线存在性、整数属性和typed RootMotion。位置门槛仍为1e-8 cm、quaternion 1e-10、scale 1e-12。
- 完整Marker集合不同的31帧共同同步，包括单脚/双脚名称交集；使用实际UE联合结果验证上一批Core交集实现。

最终结果与日志见本记录末尾的验收结果；旧三根原生回归仍为3780帧/9105姿态/737505骨，Main方向与模式反馈通过。Core生产代码本批未再修改，沿用上一批76项全通过结果，不将其写成本批新全量测试。

## 实现边界与下一步

本结果支持复用 ALS 原模型和骨架；额外控制骨及虚拟骨在81骨运行时参与求值，最终仍应只发布原68蒙皮骨。人体比例、全部遮罩和最终脚部仍需生产场景视觉验收。

四根是同一组Provider的执行入口，不能各建一份独立 Main 或 Sync。后续沿现有共同宿主扩展14个typed Interface入口，保持输入姿态、double参数、节点身份、共享Provider历史与Update/Evaluate分工。当前资源选择接口并不等同于完整14入口图执行。

下一步接完整原 LocomotionSM 的选边、真实权重、Idle/Air 与最终状态混合，再接统一Notify/Montage、原图惯性和最终足部及普通Demo。没有新增渲染、人工、多角色或性能验收；ALS R2–R7、暂停项和整个Lyra目标仍开放。

## 验收结果

加强后的Godot对照已正常退出0：四根共8400姿态/680400骨，最大位置差1.0442802858320781e-13 cm，quaternion 9.773746735086136e-16、scale0，Main vector最大差8.526512829121202e-14 cm；原门槛未变。18900移动源与15120 HipFire记录、11340 Lean时钟/22134样本逐位同；144个实际移动资产被选中，完整资源清单另含6个HipFire，共150。3360次坏epoch晚期提交拒绝，逐帧取消重试通过。

证据：`ground-native-v2-godot-final.log`、`ground-native-regression-state-history.log`、`ground-native-v2-debug-build.log`、`ground-native-v2-optimize-build.log`；两种.NET构建各0错误0警告。UE BuildPlugin38 actions正常退出0，日志 `ground-native-v2-ue-build.log`。

首轮.NET误用不存在的 `cycle.Tick.Player` 已改为实际 `cycle.Player`，失败日志 `ground-native-godot-build.log` 保留。新增HipFire捕获使用独立v2文件；加强比较没有发生算法差异或门槛放宽。UE日志仍有既有Footstep无效GameplayTag、Transient压缩依赖、无Editor工具与BP2FP映射等警告，不称UE零警告。

最终复采日志为 `ground-native-v2-ue-export.log` / `ground-native-v2-ue-export-second.log`。自动审计 `python tools/verify_lyra_main_ground_native.py` 已退出0，输出 `LYRA_MAIN_GROUND_NATIVE_FINAL_VERIFIED`；报告为 `artifacts/lyra-analysis/ground-native-final-verification.json`，508包/623此前JSON与源码/构建输入/实际包的五个探针文件哈希一致。
