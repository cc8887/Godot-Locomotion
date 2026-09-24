# 原生 Slot 资产闭包与共享组绑定

## 原项目事实与下一步修正

只读扫描 `/ALS` Asset Registry 中全部 AnimMontage，并导出各轨道动画引用及原 Skeleton 文本，得到 **9 个 Montage、1 个 Skeleton**。两套 Get-up、六套 Mantle、一套 Roll 全部使用 `PostLocomotion`。没有使用 Head/Spine/ArmLeft/ArmRight/Pelvis/Legs/Curves 的 authored Montage，因此不能把“七套区域 Montage 导出”作为待完成资产要求。

原 Skeleton 的四个组为：

| 原生索引 | 组 | Slot |
| --- | --- | --- |
| 0 | DefaultGroup | DefaultSlot |
| 1 | Grounded | Transition、TurnInPlaceStanding、TurnInPlaceCrouching |
| 2 | Locomotion | PostLocomotion |
| 3 | Layer | Head、ArmLeft、ArmRight、Spine、Pelvis、Legs、Curves |

七个区域身份独立，但属于同一个播放组。同组播放新 Montage 会使旧实例淡出，不等于七个可独立并行播放的组。此前十二个独立组及完整图七区域共存测试只证明通道容量/图混合，不是原生资产配置。Core 通用 group 仲裁本来已支持同组替换；本批补资源绑定，避免未来宿主错误配置。

新增 `AlsRefactoredRegionSlotBindings`：从导出清单读取唯一 Layer 组，验证七个区域完整且没有跨组重复，并与原生 Skeleton 文本交叉检查。区域 sequence 绑定强制共用一个宿主 group id；该编号由合并资源 owner 分配，不能将原生索引 3 直接当全局宿主编号。

普通 Refactored 动作的重点仍是 PostLocomotion → Layering → Head 的完整链路。下一步继续 Head/View 的真实 BlendSpace、回调状态与宿主布局，再普通 Mantle/恢复整合。区域 Slot 原生受控覆盖对照仍可补充，但不是寻找不存在资产的前置阻塞。

## 验证

- 新增 5 项：9 个实际 Montage 的 Slot 闭包；同一 Layer 组 Head→ArmRight 替换、PostLocomotion 独立组不受影响、冻结帧不变及 discard 重试一致；缺失/重复/跨组/原文不一致拒绝。
- Import Release RefactoredLayer/RefactoredRegionSlot/Mantling 定向 131 通过、0 失败。新增 5 项首轮也单独通过，数量重叠。
- Godot Optimize 构建 0 warning、0 error。本批未运行 Godot 场景或全量 Core/Import，没有新的原生姿态覆盖轨迹。
- 完整 UE Editor 构建及技能审计通过，0 build actions，fingerprint `C1E13C8BE16C15A0E4427F4D37F21FB14262557178C0FCAC14AC46DF96161D8C`，BuildId `7fb8adce-a7f2-4be3-9d02-f8b2ae766ac2`。日志前缀 `20260924T175211663Z-b864eea9bcc24da395348ba272e4d8bb`。未改插件/项目配置，无新 DataValidation 或打包。
- 冷导出实际退出 0、0 Error/Warning；普通 Editor PID 21700 等待真实进程退出 0，输出与冷导字节一致。普通日志保留两条旧 Condition failed 和五条既有 AI/导航/材质/console/Crowd 警告，不宣称全绿。
- 首次导出正则遗漏省略默认 GroupName 的第 0 组；修正为 DefaultGroup 后重导，再与普通 Editor 比较。首次日志保留。
- `assets/config/refactored_slot_inventory.json` SHA256：`872CBE40ACC2D211E8EA4B99C4A31835A0A2CC0F6347797F7C62FB24D8CFAF21`。
- 日志、重复资产和 TRX：`artifacts/refactored-slot-inventory/`。

普通 Demo 尚未切换新的 Refactored 完整图。物理稳定性、Flail、最终性能/视觉等旧缺口，以及用户暂缓的头颈、道具物理、音频保持原状态。
