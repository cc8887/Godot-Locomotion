# Overlay 专用起身与覆盖 Notify

## 实现

普通 Demo 的 Ragdoll 退出现在按当前已采集 Overlay 和骨盆朝向选择起身 Montage。新增六个 LH/RH/2H Montage 绑定，保留 Default Front/Back ID 与 Roll ID，仍使用同一个 Action/Montage bank。

映射来自本机 V4 `ALS_AnimMan_CharacterBP.GetGetUpAnimation` 的实际函数调用：只读导出器生成 transient actor，逐一设置 13 种 Overlay，并分别以 face-up=false/true 调用，共26结果；不是按文件名猜测武器占手。Refactored C++ 的 SelectGetUpMontage 提供选择扩展点，本批具体资产选择沿用当前使用的 V4 内容。

| Overlay | 起身组（每组均有 Front/Back） |
| --- | --- |
| Default、Masculine、Feminine | Default |
| Injured、Bow、Torch、Barrel | LH |
| HandsTied、Box | 2H |
| Rifle、Pistol1H、Pistol2H、Binoculars | RH |

新 `v4_get_up_selection_inputs.json` 保存实际选择结果及原生选择/Notify 图。`AlsGetUpSelectionCompiler` 绑定26项结果到唯一现存 Get-up action，拒绝遗漏、重复、外来 Overlay、未绑定 Montage 或 Roll。映射是原生函数求值结果，不声称编译了任意 Blueprint 控制流。

六个专用 Montage 共用已有正反面 Sequence，差异包含额外 OverlayOverride Notify。本批发现 production `PrepareFromFrame` 之前总使用默认 override=0，仅换 Montage 不足以得到专用起身姿态，因此补齐消费：

- 导出实际 Notify 实例属性 OverlayOverrideState，六项均为3。
- 编译校验原生 Received_NotifyBegin 执行接口设置并读取实例属性，Received_NotifyEnd 设置0，目标为 AnimInstance。
- 用 ActionDefinitionId + MontageId + EventId 精确绑定，保持 Generic 事件的原有身份与时序；Begin 候选写3、End写0，Tick不改状态。
- 成功提交后发布覆盖值，供下一帧 Overlay 图读取；失败候选不发布，gameplay 停用清零。没有第二个播放时钟。

资源源集合仍为78、75 players、109 samples、1752原生姿态；专用 Montage 无新增 Sequence。重新导出校验所有既有 raw 文件字节不变，source index 仅 bindingDigest 更新为 `D234D173116EB244`。

## UE 导出验证

按 `ue-diagnosing-plugin-build-load` 技能先执行完整 Editor 构建与所有项目插件审计，0 action、ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp全部PASS。日志前缀：`../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260924T080444581Z-0ea73c3949c847928c93b2412170050e`。fingerprint仍为 `1CFC5657A618E30F849577D7EA003062B54DB04B41EF728389B91D449D0DF5E3`。未修改UE插件、配置或原始资产，没有本批普通Editor重启/DataValidation验收。

所有本批最终 commandlet 退出0、assets_saved=0。日志 `get-up-overlay-selection-notify.log`、`get-up-overlay-montages-production.log`、`get-up-overlay-sources.log`。重复导出在 `artifacts/get-up-overlay-repeat`：

| 文件 | 两次字节一致的 SHA256 |
| --- | --- |
| v4_recovery_action_montage_inputs.json | 5E7339D1417DA2B47C22C044D086CF9D1EB7335DA47B588956C551B863C2F0A3 |
| v4_recovery_action_notify_inputs.json | 7A269600FD95692C9392FBF79650303EE92387E37D12070D6A4A0E0A3ABC1074 |
| p5_get_up_actions.json | 2BAEB2AD728DCCC4DB041E520E0780ECA3BED1044577801A6A9C053FCEE29E0E |

选择结果26项也相同。selection整个文件不字节一致：两个 Notify 图各自未连接的 EventReference 引脚在加载时生成不同 GUID；图路径及实际连接/属性无其它差异。保留原始文本，不伪造完全确定性声明。

## Godot 与测试

- Optimize build：0 warning、0 error。
- 定向 Import：7项通过，包括26映射、六个Override Begin/Tick/End、外来Action身份不匹配、非法映射拒绝、九个Montage共有bank的RateScale/Notify检查。
- Import Release全量：2492通过、1既有NormalEditor测试跳过，0失败；`artifacts/get-up-overlay-tests/import.trx`。
- Rifle/RH Parallel60：正反面两循环、空中退出、实际选中Montage断言、override进入3并复位0通过。`get-up-overlay-rifle-notify60.log`。
- Bow/LH Parallel120：正反面、起身中停用恢复、两次后续完整起身和空中退出通过，accepted3/completed2/retired1。`get-up-overlay-bow120.log`。
- HandsTied/2H Single30：正反面、首次Get-up BeforePublish故障重试、两完整循环与空中退出通过，恰好1次预期故障。`get-up-overlay-tied30-final.log`。
- Rifle OpenGL60：6截图，检查01/03/05/06，从仰卧、坐起、跪起到恢复持枪移动。`get-up-overlay-rifle-rendered.log` / `get-up-overlay-rifle-captures`。受控Back初始化不等同自然背摔，也不等同所有13 Overlay视觉验收。
- 旧Montage根运动：11 cases/2540 frames/2540 retries通过，最大位置误差3.46452e-6m。`get-up-overlay-roll-root-motion.log`。
- Default自动Roll离地回归：两自动入口/起身与空中退出，触发帧故障后正常重试，分别1次RuntimeFailure/1次Ragdoll结束Roll；`get-up-overlay-default-regression.log`。

过程失败保留：最初Demo拒绝旧source bindingDigest，重导索引后通过；旧测试仍假设3个动作，更新为9；2H受控Back测试第一次将故障注入晚一帧，实际打断了已接受的Get-up，不能当首次播放重试。修测试在手动ConsumeExit后立即设定首次未提交帧，最终验证通过。没有因此改变生产运行时的故障中断策略。

## 仍待完成

未验证所有13 Overlay的实际动画轨迹、起身中切换Overlay、所有故障/停用交错或UE逐帧参考。道具物理附件和头颈拉伸按用户要求暂缓，不能据持枪起身截图宣称已完成物理附件。

静态物理9/12、Flail0/3旧稳定性目标保留，Mantle、完整Camera、十分钟性能预算等原规划仍未完成。没有本批Core全量或旧物理矩阵重跑，目标保持进行中。

主目录 `.`；用户P4及三份头颈诊断文件保留。P4 SHA256仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
