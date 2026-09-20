# Montage Root Motion 来源与提取

本批直接在 `.` / `main` 推进 P5C 的前置链路。原先共同 Montage
保留了 `RootMotionEnabled`，但没有读取根轨迹；现在从同一物理播放实例的
推进区间提取位移和旋转，发布到正式 `AlsFrameResult`。没有新增项目副本。

这份结果仍是提议，Motor 尚未消费它；普通 R 仍为原地预览。不能把本批
描述为完整 Roll 玩法、碰撞移动或 Mantle 已完成。

## 原生依据与实现

对照本机 UE 5.9 的 `AnimInstance.cpp`、`AnimMontage.cpp`、`AnimSequence.cpp`
及 `AnimationAsset.h`，使用 `RootMotionFromMontagesOnly`：

- 独立保存 `RootMotionMontageInstance` 对应的物理实例，播放带根运动的
  Montage 时指定；停止该实例时清空，不恢复更旧的淡出实例。
- 先推进物理实例，再执行本帧动作请求。替换请求不改写已经提取的本帧
  区间。自动淡出在 Advance 内清除后续所有者，但该 tick 已选定的来源仍提取。
- 位移不乘 Slot/动作姿势权重。读取原始 root track，不从已 ForceRootLock、
  重定向或分层后的最终姿势反推。应用原生 root reference、归一化尺度策略、
  起终变换相对关系；输出累计运动的尺度固定为一。
- 结果附带帧/角色/generation、物理实例、动画 ID、起止时间。候选丢弃、
  成功提交、生命周期退役沿用同一事务；摘要纳入来源身份，旧默认摘要保持。
  新结果字段附加到结构末尾，保留已有字段偏移。

`AlsMontageRootMotionReader` 输出的是转换到 Godot 轴系的 **mesh-local**
位移，尚非角色/世界位移。实际消费时还必须使用 mesh-to-character 变换；
原生 Roll 的 root track 正向不是直接等于角色世界前方。
支持范围沿用当前单 section、单 segment、非 additive 的 authored Montage；
未宣称支持任意 RootMotionFromEverything 混合、多段跨循环或任意重定向骨架。

## 原生导出

Exporter 增加可选 Root Motion 采样，原有默认调用保留。Python 入口
`tools/unreal/export_montage_root_motion.py` 只读原始资产，不保存 UE 资产。
使用 `ue-diagnosing-plugin-build-load` 的完整 Editor Target 构建及插件闭包
审计，ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 均通过。

构建状态指纹：`70A51FC7B742AA044EC0C051070CDAE119C503A13AADA31BF35B6B48B4B8A27E`。
冷 commandlet 和普通 D3D12 Editor 重启均执行导出并正常退出，JSON 字节一致：
`89852F3641E263D07967289F1A39ADA4600DD035F57D3E1652718C6FE2BD6CC0`。
DataValidation 退出 0，0 error / 3 warning；警告涉及源工程旧 PawnActionsComponent
及 RecastNavMesh 版本，不改写这些无关资产。普通 Editor 启动日志另有两条
`LogAutomationTest: Error: Condition failed`，出现在 Engine 初始化前；导出及
退出成功，但不把该日志宣称为完全无错误。没有发现要求本批执行 UE 打包的仓库规则。

## 验证与证据

证据目录：`artifacts/root-motion-20260920`。

- Godot 构建 0 警告、0 错误。
- 原生 oracle 回放 11 情形、2540 帧，其中 945 帧提取运动；逐帧丢弃重试
  2540 次。包括 30/60/120 Hz、Roll 与 Turn 交叠、同帧替换、不停止 group、
  反向、零速、停止旧实例、停止当前实例。所有者、运动有无逐帧相等；最大
  位移差 `3.46452E-06 m`，旋转 `1 - abs(dot)` 最大 0。
  见 `native-replay-final.log`；原生 Roll 没有非零根旋转，非零根旋转与反向
  变换另由 Core 构造用例覆盖，不能当作原生旋转资产认证。
- Core 新用例覆盖不乘姿势权重、停止/替换/自动淡出、生命周期退役、丢弃
  重试、源参考变换、反向/钳制/子区间、归一化尺度、旋转相对关系、零分配。
  最终 Release 2557 项通过，见 `core-final-isolated.trx`；按现有规则排除独立的
  历史 `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests`，不把它们算成本批已重跑。
- Import Release 2285 通过、1 项既有跳过，见 `import-final.trx`。
- 普通动作输入 60 Hz / 240 帧，3 接受、1 替换、1 取消、1 完成；103 帧
  发布运动，累计提议长度 `3.519633 m`，角色仍无水平位移，见 `action-input-60.log`。
- 十角色 Single / Parallel 各 3621 帧，body pose `CEC4EC705E945A65`、
  character root `E030B6049AEDDCE1`、result `687F8C31B8CE2B4C` 一致。
  身体和角色位置与上一批一致，结果摘要因新增运动内容而更新。
  每帧检查来源身份与角色/帧/generation 一致，无来源时必须为单位运动；
  最终见 `ten-single-final.log` / `ten-parallel-final.log`。
- 连续两次 BeforePublish 失败并替换动作：失败期间保留上一已提交来源与
  位移；成功重试只推进原来源区间一次，保留命令执行前的物理运动，未重复
  积分 Motor。见 `failure-replacement-final.log`。

首轮原生回放只在最后计数断言失败：错误假定运动帧数大于 1000，真实数为
945，逐帧原生比较均通过。已固定准确计数，保留 `native-replay.log`。
首轮 Core 因新增字段未同步布局合同失败（`core-final.trx`），现改为末尾扩展
并更新合同。第二轮有既有 Aim 热路径分配断言偶发 2256 bytes（`core-verified.trx`）；
随后包含该用例及本批新增用例的 57 项专项全部通过（`core-focused-final.trx`）。
最终独立执行完整上述 Core 集合通过，未改写 Aim 实现或放宽分配断言。

## 下一步的消费顺序

当前 Main Gather 已先执行 Motor，随后才运行 Worker 动画；直接在最终 Main
Commit 平移会使此前脚部查询和姿势基准过期。下一批需要将物理 Montage 的
同帧推进与位移提取放到碰撞移动前，保持每帧只推进一次；Main 根据实际
mesh-to-character 变换与碰撞移动得到位姿，再供脚部查询/最终图使用。
必须验证暂停、失败重试和换代不会二次位移，并保持平台/落地状态一致。

然后接入 Refactored Roll 的地面门控、目标朝向、半衰期转向、动作期间蹲姿、
落地自动翻滚等规则。空中中断转 Ragdoll 与后续 Ragdoll 阶段一起闭合，
不以普通取消冒充原生行为。后续 Mantle、Ragdoll/Get-up/Pose Recovery、
完整 Camera、复杂地形/起停/换髋观感和十分钟性能预算仍在原清单内。
