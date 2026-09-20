# 完整基础移动图接入真实 Worker/Demo（第 100 批）

日期：2026-09-12。工作区：`D:/GodotALS-p5a-events-actions`。
承接用户要求：按移植完整性修复，并继续原计划中的缺失项。未 commit、revert
或清理既有工作区修改；音频仍暂缓。

## 已交付的运行边界

默认 P4 Demo 使用 `p4_cycle_locomotion_profile.json`。其 Worker 通过
`AlsProductionMovementRuntime` 独占完整 BaseLayer/Main Movement，来源绑定
共 75 个 player、109 个 sample。Standing、Crouching、Jump/Fall/Land 及物理
Montage 的姿势、同步、曲线、事件进入现有晚期提交；旧 AnimationTree 与独立
Turn/Rotate 播放时钟不再推进这个生产入口。旧配置的兼容路径仍保留。

Core 新增 CompleteMovementGraph 时序策略：所有姿态/移动状态先等待实际来源
求值，再一次性回填公开 Stride/PlayRate/Phase 摘要。摘要不反向驱动来源时间。
真实 Motor 输入、实际缓存权重与图求值计数进入同一候选；上一已提交基础图
曲线按名称/presence 反馈。脚部现有消费者直接读取该图的 Enable_FootIK 和
FootLock，缺失返回零，不另取旧片段时钟的曲线。图内部 presence 传播仍有
未完成部分，不能因此宣称完整脚部策略已还原。

实际 Skeleton 姿势在现有 Aim/脚部后处理之前应用。应用、后处理、事件准备
任一晚期失败会恢复骨骼、输入历史、曲线、同步、随机状态与播放候选；成功
才提交并向主线程发布。请求结果与真实来源事件由共同所有者输出。

静止角色旋转读取实际基础图 RotationAmount，采用原 Character Blueprint 的
`RotationAmount * DeltaTime / (1 / 30)`，按 UE Z 到 Godot Y 坐标转换反号，
原死区为 0.001。mesh 瞬移阈值由原始 300 cm 转换为 3 m。静止 Turn 的公开
诊断选择最大求值权重实例，将物理秒数/倍率投影为归一化 Phase/PlayRate，
方向与 90/180 度来自实际资产配置，不新增播放时钟。

## 原生来源与导出核查

`tools/unreal/export_character_animation_bridge.py` 使用原生 T3D 导出实际
ALS_Base_CharacterBP 的 12 个具名 authored 图、CDO 和 mesh 模板，正式配置
为 `assets/config/v4_character_animation_bridge.json`。严格编译器沿实际连接
验证 RotationAmount、绝对值/死区比较、WorldDeltaSeconds、30 Hz 换算及
AddActorWorldRotation 的 yaw 输入，不以节点显示名相似代替连接验证。

完整 Editor 构建及插件审计通过，记录前缀：
`20260912T035113435Z-1111a540f8f94b46a2ca1f1ca2b172b0`。
输入指纹 `8F83BB343C52E314287C7FD84B32F8AEE442C0323DD0E88E49C23D43FF4442D6`；
BuildId `369675c0-434c-4633-b2aa-532acf57bb8f`。
构建索引见 `artifacts/full-movement-ue-build.log`，原记录在 UE 项目 Saved 下。
全部 Editor 会话结束后的复核审计通过，日志为
`artifacts/full-movement-ue-audit-final.log`，三个项目插件产物与 receipt 一致。
本批未修改原生插件/项目配置，未新跑 DataValidation 或插件打包。

commandlet 正式导出与普通 Editor 重复导出都退出 0；普通 Editor 文件为
`artifacts/character-bridge-authored-editor-repeat.json`，实际消费的旋转公式与
mesh 阈值编译结果一致。原图文本因编辑器重建有差异，不声称 JSON 字节一致，
也未声称所有 12 个 Character 函数已完成语义对照。

导出首次错误来自自动枚举出的生成 BPI 图；改为具名 authored 图后通过。
保留 `character-bridge-export.log` 首错，正式冷导出日志为
`character-bridge-authored-export.log`/`character-bridge-authored-native.log`，
普通 Editor 日志为 `character-bridge-authored-editor.log`。

## 验证结果

日志均在 `artifacts/`。以下按实际覆盖区分，不把组件测试当成完整 Demo 对照。

| 检查 | 结果 | 日志 |
| --- | --- | --- |
| 最终 Godot C# 构建 | 0 警告、0 错误 | full-movement-build-final.log |
| Core 来源时序/输入反馈专项 | 36 通过 | full-movement-core.log |
| Character 原生连接编译专项 | 4 通过 | character-bridge-import.log |
| 原生 Editor 消费公式重复核对 | 旋转与 mesh 参数一致 | full-movement-actions.log |
| 真实 Worker single/parallel | 各 600 帧，直接坠落、跳跃、落地、蹲伏 | full-movement-single-final.log、full-movement-parallel-final.log |
| 晚期姿势/事务失败 | 候选不发布，姿势与历史恢复 | full-movement-late-transaction.log |
| 有通知的晚期失败 | 候选事件 1，泄漏回调 0，状态/随机/身份恢复 | full-movement-late-source-event.log |
| 原 BaseLayer 映射回归 | 3360 帧、12 次晚期故障 | full-movement-mapped.log |
| 八资产转身/通知回归 | 2400 帧、58 次晚期故障 | full-movement-turn.log |
| 真实 Roll 组件回归 | 1050 帧/重试、28 次晚期故障 | full-movement-actions.log |
| 渲染左右横移 | 720 帧、12 张截图、56 帧转身 | full-movement-visual-fixed.log |
| 渲染转身中起步 | 720 帧、120 张截图、44 帧播放/移动重叠 | full-movement-turn-start-observed.log |

真实 Worker 两种模式结果摘要均为 `28AB43F512A415CD`，完整骨骼
`6EB88CDE787EA584`，根变换 `F5AF8D1C32D37A57`，来源事件 28，lag/stale 为零。
原 180 帧跳跃夹具不能覆盖直接坠落；新增 `--full-movement-coverage` 以高处
出生和 600 帧真实 Motor 输入覆盖 Fall、蹲伏，而不是删除缺失路径检查。

两次渲染检查最大相邻帧脚旋转变化均为 14.348 度，低于保留的 30 度突跳
检查。最终 120 张截图与逐帧数据位于 `full-movement-turn-start-observed/`；
`movement-contact-sheet.png` 包含 186–360 帧的 30 张局部连续图，已逐排检查
横移和反向过程。双臂保持较收拢姿态，上身动态分层仍需实现；截图未与相同
输入的 UE 最终输出逐帧对齐，不能证明交错步与换髋已经等价。脚旋转阈值也
不衡量接触期间滑移，起步滑步仍需真实支撑窗口和平台坐标测量。

视觉记录器首轮在帧 720 序列化 MontageFrame 的 ReadOnlySpan 时报错。修复
为采样时复制求值数组与身份，避免把复用的可变播放 bank 留到最后序列化。
保留 `full-movement-visual.log` 首错。最终 720 个样本中有 56 个物理 Montage
帧，身份均等于采样帧，位置逐帧不同，归一化转身摘要检查通过。

旧 `--turn-start` 固定第 646 帧移动，但新图到第 665 帧才开始转身，所以未
覆盖交接，失败日志保留为 `full-movement-turn-start-visual.log`。改为观测已
提交的真实 Slot 权重大于 0.5 后起步，仍要求实际移动与播放同时存在；最终
覆盖 44 帧。没有放宽脚部突跳或重叠断言。

## 下一项与未完成范围

1. 继续原 P3/P4：闭合最终 YawOffset、Character 旋转门控和更新顺序，包括
   LimitRotation/空中策略。目前仅接静止 RotationAmount，现有 Motor 时序
   与移动旋转仍在，不能声称整个 Character Blueprint 已逐帧照搬。
2. 完成动态 LayerBlending/Add/LS、Aim/Lean、Overlay 掩码与上身 IK，再将
   后续层修改后的真正最终曲线反馈回下一帧；目前反馈止于基础图输出。
3. 补地面/蹲伏曲线 presence 的正确传播与脚锁、pelvis、平台最终消费者，
   用相位、状态权重和真实支撑窗口验收交错步与滑步，不加固定等待掩盖问题。
4. P5A 仍需完整 ActionPlayback 摘要、GroundedEntry 类型化通知、通用多片段/
   section 等。Roll 的请求/播放/通知接线不等于 Root Motion 或玩法完成。
5. 原 P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/
   Pose Recovery/完整 Camera、P7 十分钟性能预算保持，音频暂缓。

当前标记是“生产基础图接入及上述回放通过”，不是“ALS 完整复刻通过”或
“用户报告的上身/滑步/换髋全部修复”。
