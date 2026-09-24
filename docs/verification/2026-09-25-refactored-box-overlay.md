# Refactored Box Overlay 动作图

本批在主目录 `D:/GodotALS` 的 `main` 接通原版 `AB_Als_Box` 17 节点图，包括 Default、Mantling、GettingUp、Rolling 四分支及分层曲线修改。普通 Demo 未切换，不是完整 Get-up 或 Ragdoll 验收。

## 实现

- Core `AlsOverlayActionBlend` 使用四通道值类型历史，对应原生 StandardBlend / Linear / Default child update mode。标签精确匹配；空或未列出的标签落回 Default，不能把子标签当父标签匹配。
- 原图四个目标混合时间为 `0.5 / 0.2 / 0 / 0.2` 秒。首次激活立即切到目标；再次改向按目标剩余权重缩短时长，保留中断中的其余通道。零时长切换返回旧分支的零权重更新请求。
- 编译器对照实际 catalog，验证原始图 pose 连线、编译节点、父实例属性绑定、同步组、固定帧、标签顺序、混合时间、曲线名称与修改值。BlendTime、CurveValues 的 authored struct 默认值与执行值不同；验证使用真实引脚默认值和 compiled runtime 值，不把 struct 默认值当执行配置。
- Default 使用 Box_Poses 帧 0/1 的 Walking 混合，再与帧 2 做 Standing/Crouching 归一化混合，叠加 0.75 Idle。无空中预测分支（符合原 Box 图）。
- Mantling 用帧 0，修改 `LayerHeadAdditive/LayerSpineAdditive` 为 0.5；GettingUp 用帧 2、Rolling 用帧 0，二者修改 `LayerArmLeft/LayerArmRight` 为 3。保留曲线存在性与原始数值，不擅自钳制到 1。
- Box runtime 生成可为空的共享 source-player 更新列表。被动作分支完全覆盖时不推进 Idle；从 Default 瞬切 GettingUp 仍更新一次权重为 0 的 Idle。全图重置发生在隐藏分支时保存 pending reset，直到 Idle 再次更新才向共享 owner 发出重置；取消候选不能消耗它。未更新 Idle 时，source owner 的旧 committed clock 不是隐藏原生节点内部初始化时间的镜像；再次成为可观察的更新/输出节点时已对齐。
- Evaluate 即使不需要 Idle pose，也校验共享 source batch 的帧身份。错误帧会 fault 并禁止提交；允许下游覆盖 Evaluate 的 update-only 提交。宿主仍须统一 ValidateCommit/Commit 或 Cancel 所有参与者。

## 原生连续证据

扩展上一批原生导出器，按 request 的明确白名单选择 Default 或 Box 原始生成类。实际 AnimGraph → LinkedAnimLayer → Overlay → GameplayTagsBlend 全链执行，父实例设置真实 GameplayTag；只观察原生四分支权重、Idle 时钟和最终 pose/curve，不把这些结果回填 C# 输入。

30/60/120 Hz 各四秒，共 **840 帧、66,360 骨样本**。覆盖动作中途换向、Default→Get-up 零时间切换、普通淡入淡出、零 delta、Get-up 隐藏 Idle 期间重新初始化再返回 Default、姿势权重变化及全零 stance。每个 C# 候选都取消并重算。

| 频率 | 帧数 | Idle 不更新帧 | 零权重 Idle 更新 |
|---|---:|---:|---:|
| 30 Hz | 120 | 46 | 1 |
| 60 Hz | 240 | 87 | 1 |
| 120 Hz | 480 | 173 | 1 |

最终最大误差：位置 `4.792872787800221e-14 cm`，旋转分量 `4.440892098500626e-16`，缩放/curve/四通道权重/有效更新 Idle 时间及权重差均为 0。

首轮 pose 预算内通过仍存在约 `7.14e-6 cm` 和 `8.56e-8` 旋转分量差。检查当前 UE `AnimNode_BlendListBase.cpp` 后补齐两个真实快速路径：单个满权重 pose 直接透传（不额外 normalize）；两个有效权重且和近似 1 时调用双姿势 in-place 混合，以第一权重的 float 补数取第二权重，并用曲线 Lerp。修正后达到上述误差，将 pose 回归门槛从预先的 `1e-3 cm/1e-5/1e-6` 收紧到 `1e-10 cm/1e-12/1e-12` 并通过，未放宽任何门槛。原始日志/TRX 保留。

首轮原生序列没有实际触发零权重 Idle（只在 Core 单测覆盖），因此调整动作输入序列，最终三个频率均实际观测一次，并加入必须大于 0 的断言。初版数据与最终版分开保留，正式参考使用最终版。

## 验证与加载

- 新增 6 项测试：实际图及 7 类变更拒绝、精确标签与瞬切、三 Hz 原生连续对照、隐藏分支帧身份/fault/update-only/pending-reset 取消重试。相关 Import **23 通过、0 失败**。
- Optimize 构建 **0 错误、0 警告**；Python 语法检查通过。
- 开发初始 C# 编译出现 `Math` 命名空间冲突，改为 `System.Math`；图校验两次识别 CurveValues/BlendTime 引脚覆盖 struct 默认值后修正。无 UE 编译失败。
- 完整 Editor 构建 4 actions 成功，插件审计通过。BuildId `4cd31a69-ae92-41ab-8118-ffba44340a1a`；fingerprint `EC038D96116B35596084809D63864057A3D971A92FB8DB2E80E4B0308B92B05B`。
- 最终冷启动与普通 Editor PID 35136 均真实退出 0，参考逐字节一致：`1D63798296D82168D836EEE162666B710518BE3235BF69725C4B748B27EBD015`。
- 正式参考 `assets/config/refactored_box_overlay_trace.json`：13,850,377 bytes，附 catalog/sync 文件哈希。
- 旧 Default 导出回归退出 0，与已提交参考逐字节相同，SHA `B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003`。
- DataValidation 退出 0，0 errors、3 条旧 AI/navigation 资产警告。普通 Editor 仍有两条旧 `Condition failed` 和五类旧警告，未称其已修复。
- 产物在 `artifacts/refactored-box-overlay/`，UE 构建日志在项目 `Saved/Logs/PluginBuild/`。未跑全量、Godot 场景、十分钟性能或打包。

## 后续边界

当前真正接入并有整图连续对照的是 Default 和 Box 两种。Feminine/Masculine 虽与 Default 拓扑相似，但姿势资源和 Idle alpha 不同，尚未接入；HandsTied/Injured/Barrel 涉及动作标签与缓存姿势，Torch/Binoculars 涉及 mesh additive，四个武器图还有状态机，不能宣称它们已完成。

继续其余 Overlay、实际 Locomotion/Standing/Crouching 状态机与回调、统一 source/Notify/root-motion/角色事务/宿主，再做普通 Demo 整链验收。Ragdoll/Flail 旧失败、Get-up/Pose Recovery、Mantle gameplay、相机复杂碰撞与最终性能预算仍保留；音频、道具物理和头颈诊断仍暂缓。用户原有修改不纳入提交。
