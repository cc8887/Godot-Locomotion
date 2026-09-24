# Default Overlay 原生连续对照

本批接续 `24a558c`，运行原版 `AB_Als_Default` 的完整 AnimGraph（主 Root → LinkedAnimLayer → Overlay），对照上一批生产 runtime，而不是手写一套 UE 侧替代混合公式。普通 Godot Demo 尚未接此图。

## 参考与修正

新增 UE 工具创建临时组件、原始生成类实例和受控父实例。通过原生 property access 读取 PoseState/InAirState，正常执行 PreUpdate、UpdateAnimation（含 Sync tick）、PostUpdate 和 ParallelEvaluateAnimation；使用原始 raw animation 数据及 79 骨布局，不编译或保存资产。

30/60/120 Hz 各三秒，共 630 帧、49,770 骨样本。场景包含 Walking 端点及中间权重、Standing/Crouching 混合、两者均零、空中及地空混合、持续变化 GroundPrediction、隐藏/再相关、零 delta、整体重新初始化。读取预测节点内部 alpha 和 Idle 实际时间/权重作为观测。C# 仅消费请求输入，自主计算所有历史与 pose；每帧还取消再准备、重新采样，检查候选结果相同。

首次 native 对照在预先设置的 `P<=1e-3 cm / Q<=1e-5 / Scale<=1e-6` 范围内通过，但最大位置仍差 `4.896516936617939e-6 cm`、旋转分量 `1.2294861795325573e-9`、缩放 `4.768371653085524e-8`。继续检查引擎发现确切原因：

`FAnimNode_TwoWayBlend` 把 `1.f - InternalBlendAlpha` 传给 `BlendTwoPosesTogetherInPlace`，该函数再次单精度计算 `1.f - WeightOfPoseOne`。原 C# 直接使用 Alpha 作为 B 权重，数学等价但浮点顺序不同。只修正 Default Overlay 的 TwoWay 混合与 curve alpha，没有修改其他用途的通用 Blend helper。

修正后最大差值：

| 指标 | 630 帧最大差 |
|---|---:|
| 骨骼位置，厘米 | 9.506413903366459e-14 |
| 旋转分量，四元数同号处理 | 4.440892098500626e-16 |
| 缩放 | 0 |
| 曲线值及存在性 | 0 / 全部一致 |
| 预测 alpha | 0 |
| Idle 时钟与更新权重 | 0 |

随后将姿势断言收紧至 `P<=1e-10 cm / Q<=1e-12 / Scale<=1e-12`，三 Hz 重新通过，足以捕获原先的简化错误。其余 alpha/time/curve 预算保持不变；未放宽阈值，也不宣称整个 ALS 逐位一致。

## 构建、加载与验证

- 首次完整 Editor 目标构建失败：Unity compilation 下新 helper `TransformJson` 与既有文件重名。重命名后再次运行完整构建，4 actions 成功；未启动失败构建的 Editor，未复制 DLL 或修改 BuildId。
- 项目插件审计通过。BuildId：`4cd31a69-ae92-41ab-8118-ffba44340a1a`；状态 fingerprint：`D086CE40DD5E8B6C012FF096B8001A74F806AAAAA1BC90FEA9A60A836BBC81D9`。
- 冷启动 Python commandlet 退出 0，630 帧导出完成，日志无 Error/Warning。
- 普通 Editor PID 42428 重启，真实进程退出 0，630 帧重复导出与冷启动逐字节相同。SHA256：`B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003`。
- 普通 Editor 仍有前批已记录的两条 `Condition failed` 和五类旧警告（V4 AI/navigation、引擎材质、console、crowd）；本批没有把这些问题标记为修复。
- DataValidation 退出 0，汇总 0 errors、3 条旧警告；内容同前批 AI/navigation 资产警告。
- 新 native 测试 3 项；最终相关 Import 测试 17 通过、0 失败；收紧姿势阈值后单独重跑 native 3 项通过。
- Godot Optimize 构建 0 错误、0 警告；Python 脚本语法检查通过。
- 参考 `assets/config/refactored_default_overlay_trace.json`（9,354,980 bytes）包含 catalog/sync resource hashes。开发、构建、导出、普通重启、资产验证和 TRX 日志在 `artifacts/refactored-default-overlay/` 及 UE `Saved/Logs/PluginBuild/` 保留。

## 覆盖边界与下一步

这是单个实际 Default Overlay 图、受控父状态、raw 数据的连续证据。不是所有 Overlay、完整 Locomotion→后处理→Ragdoll 的集成证明；本批没有启动 Godot 场景、做视觉验收或十分钟性能测试，也未做打包（仓库无本批要求的打包流水线）。

下一步继续其余 Overlay 与真实 Locomotion/Standing/Crouching 状态机/回调、统一 source/Notify/root-motion/整角色事务，再接普通 Demo。Ragdoll/Flail 旧失败、Get-up/Pose Recovery、Mantle gameplay、相机复杂碰撞和最终性能等旧缺口仍保留；头颈诊断、道具物理、音频仍按用户要求暂缓。用户原有修改保持原样。
