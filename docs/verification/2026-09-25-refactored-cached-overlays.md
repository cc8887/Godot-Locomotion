# HandsTied、Injured、Barrel 原版 Overlay 缓存图

本批直接在 D:/GodotALS 的 main 实现三张原版动画图。普通 Demo 尚未切换到这些图；本记录不是 Ragdoll、Get-up 或画面整体验收。

## 实现

- 编译原资源的 authored/compiled 拓扑、属性绑定、固定帧、动作标签及延迟缓存更新顺序。HandsTied/Injured 各 22 节点，Barrel 18 节点。
- 四个动作消费者共享一个 SaveCachedPose。按 UE 延迟更新选取最大消费者权重，Idle 只更新一次，不能把消费者权重相加。Idle 加法比例分别为 .5、.5、.25。
- HandsTied/Injured 保留空中预测 20/5 插值；Barrel 无空中分支。Get-up/Roll 在缓存曲线副本上修改手臂曲线，不污染其他消费者。
- 复用动作线性混合状态，时长 .3/.1/0/.1；新 evaluation helper 保留单满权重透传、双姿态补数和多姿态有序累加路径。
- Prepare/Evaluate/Commit/Cancel 隔离候选状态；同帧重复求值复用结果但校验播放对象身份。失败阻止提交与读取姿态；允许下游完全覆盖时仅更新、不求值。整个角色的统一提交仍须未来宿主协调。

## 原生证据与测试

原生导出执行未改动资产的 Root → LinkedLayer → Overlay，通过实际 SaveCachedPose 与 FAnimSync 获取参考。C# 只使用输入驱动状态，不以原生权重或时钟驱动输出。

三张图各 30/60/120 Hz、各四秒，总计 2,520 帧、199,080 个骨骼样本。每张图有 402 帧多个消费者有效，其中 98 帧三个消费者有效。覆盖动作打断、零 delta、重初始化、零 stance、空中预测、每帧取消重试、重复求值及曲线 presence。

| 指标 | 本机最大差 |
|---|---:|
| 位置（cm） | 7.393745684638675e-14 |
| 四元数分量（符号对齐） | 5.551115123125783e-16 |
| Scale | 2.220446049250313e-16 |
| 曲线、动作权重、Idle 时钟、预测值 | 0 |

缓存最大权重和 Idle 更新权重逐帧精确相同。预先设置的姿态预算 P≤1e-10 cm、Q/S≤1e-12 保持不变。

最终相关 Import 测试 36 通过、0 失败、0 跳过（含本批 13 项）；Optimize Godot 构建 0 错误/0 警告；导出 Python 语法检查通过。没有执行全量测试、Godot 场景、性能预算或打包。

初期图政策测试发现：authored cachePoseName 为 None，实际名称来自编辑节点及编译节点；Barrel authored explicitFrame 为默认 0，实际值由引脚覆盖。已修正为交叉校验真实生效字段，未修改资源或放宽姿态预算。失败日志 cached-policies.trx、cached-policies-final.trx 保留。

## UE 构建与回归

按 ue-diagnosing-plugin-build-load 技能完成整个 Editor target 构建及插件闭包审计（4 actions）。BuildID 为 4cd31a69-ae92-41ab-8118-ffba44340a1a，输入 fingerprint 为 2C9B5BEA75EF6802111F9E1EC4B02439843E4D54BD4AFEC98E755F6A95CEF592。没有复制 DLL、修改 BuildID 或使用 Live Coding。

冷启动及普通 Editor（PID 7140）实际退出码均 0，三份结果逐字节相同：

| 图 | 字节数 | SHA256 |
|---|---:|---|
| HandsTied | 14289409 | BB2609B67763F00A65439DD288910DA00ACA05CA3763DB455445C69AD351D070 |
| Injured | 14241751 | 12BBF6C43FC3D8C2AC733924B29581EE3C0C5F0163A7664B89C541FCCDF4163B |
| Barrel | 14897001 | C9EE2CD592B9E84D4CA099D5D048CF8D2A2DF74A7D8A6B84167816CF5DE2504A |

共享导出器的旧 Default/Box 重新导出均退出 0、0 error/0 warning，结果仍分别为 B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003 / 1D63798296D82168D836EEE162666B710518BE3235BF69725C4B748B27EBD015。

DataValidation 实际退出 0，0 error/3 个旧警告（V4 AI 缺少 PawnActionsComponent、旧导航及资产加载警告）。普通 Editor 仍有两条旧 Condition failed 和五类旧警告，本批没有修复或将其视作干净启动。证据位于 artifacts/refactored-cached-overlays；构建日志前缀 20260924T212530581Z-e9c8ace75b3c45d9a93273cb1c439b22 位于 UE 项目的 Saved/Logs/PluginBuild。

## 尚待完成

原版 Overlay 目前 5/13 张有实际连续整图对照；其余 Feminine、Masculine、Binoculars、Torch、Bow、PistolOneHanded、PistolTwoHanded、Rifle 仍待实现。后续继续原 Locomotion/Standing/Crouching 状态机与回调、跨图共享播放身份和 Notify/root motion、统一角色事务、最终骨架布局适配及普通宿主接入。

Ragdoll/Flail 旧失败、Get-up/Pose Recovery、Mantle gameplay、相机复杂碰撞和十分钟性能/人工视觉验收仍未关闭。音频、道具物理及头颈拉伸诊断维持暂缓。用户已有的项目配置、规划、LayerBlendingRuntime 和诊断文件未纳入本批提交。
