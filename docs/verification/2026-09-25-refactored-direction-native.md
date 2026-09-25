# 原版方向状态机连续原生对照

本批在 UE 临时 Standing/Crouching 实例上，将 FAnimNode_Root 的 Result 通过引擎 SetLinkNode 接到原始 Movement States 节点。保留其真实状态图、属性访问、状态回调、缓存姿态、Sync tick 与求值；输入由 Parent 的方向布尔、换髋锁定、交错步和播放参数控制。未编译或保存资产，未修改 UE 引擎源码。

这是方向子图的原生对照，绕过外层 stance 状态和控制入口，不是完整角色、移动输入或普通 Demo 测试。原生输出保留完整 pose/curves/player 供后续使用；本批 C# 只比较状态、过渡、时钟、状态权重、更新顺序及生成通知，未比较 pose/curves 或实际 Parent 通知消费。

## 覆盖与结果

最初三频率共 1734 帧全部通过，但每场景只覆盖 14/24 条过渡。保留 cold/editor 与 native.trx 作为初轮记录。随后增加六个满权重起始方向 × 五种方向请求（含无请求）× 三种髋锁定的组合，每场景额外 180 帧。

最终 Standing/Crouching × 30/60/120 Hz：每套分别 309/429/669 帧，合计 2814 帧。每场景均覆盖六状态及全部 24 条过渡，1618 个多条过渡叠加帧，Standing 85 次 ActivatePivot 局部通知，Crouching 无通知。全部状态、活动边身份/次序、更新顺序和通知顺序一致；状态/过渡时间、Alpha、六状态权重最大差值均为 0，既定容差 2e-6 未放宽。每 17 帧取消重试后再次核对状态与通知计数。

包括前后反向、目标尚有贡献时再次转入、曲线解锁后等待已记录来源满权重、零 delta、显式实例重置、Cubic/QuadraticInOut 原始时长。本批没有修改移植侧方向算法。

## 构建与加载

遵循 ue-diagnosing-plugin-build-load 技能，完整项目 Editor 目标构建。初次访问 private RootNode 编译失败，修为公开姿态链接接口后完整四 actions 通过；失败日志仍在 Saved/Logs/PluginBuild/20260925T014744009Z-37a575be87de4e2e9633c1cf8fec69fa-*。

最终构建日志：Saved/Logs/PluginBuild/20260925T015050919Z-a09c0233f66d4c9f84bc34e36c214d23-*。四项目插件审计通过，BuildId c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5；指纹 2ABF6BC7DC1071FB813D09DAFB9C09F75A37BDEAF0F721A50A9FA46B7686BEA1。没有复制 DLL、修改 BuildId 或 Live Coding。

首轮冷/普通 Editor PID 3680、最终覆盖冷/普通 Editor PID 11700 均实际退出 0；对应两次导出均字节一致。最终纳入资产：

| 资产 | 字节数 | SHA256 |
| --- | ---: | --- |
| refactored_direction_trace_Standing.json | 26346999 | 7C40C5ED5D07C009CF5BF291C48A36064EAA8F10B84B09F5522BA87524B6A5DA |
| refactored_direction_trace_Crouching.json | 25782822 | 2F91F1F58C842FBF5644EA6ACCE29B7E2A1EA62FE639DFDDE40D5AE71998AB6C |

Default Overlay 冷重导退出 0，与既有产物字节相同，SHA256 B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003。DataValidation 退出 0，0 error / 3 旧 warning。普通 Editor 仍报告两条旧 Condition failed 及五类旧 warning，不能宣称这些历史错误消失。

## 测试

`artifacts/refactored-direction-native/native-coverage.trx` 新增六项全通过；`related.trx` 最终 Import 47 项通过。Godot Optimize 构建 0 warning / 0 error。新 C++ 导出需要的冷启动、普通重启及数据验证均执行；无 Godot 场景、全量、十分钟性能或打包验证，仓库无既定 UE 打包流水线。

## 下一步

接原方向 state pose 与缓存，以及实际 SetHipsDirection / ActivatePivot Parent 消费，再补其余 stance 状态机、统一宿主。当前连续状态证据不能替代完整姿态与普通 Demo 验收。Ragdoll/Get-up/Pose Recovery、Mantle、Camera、性能等旧缺口仍待做，用户修改未纳入本批；音频、道具物理、头颈诊断继续暂缓。
