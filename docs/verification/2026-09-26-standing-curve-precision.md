# Standing 曲线精度修正与严格对照

本批在主目录 main（基线 `1ef1b3c`）实施，完成 ROADMAP R1 的既有 Standing 原生门禁。没有创建项目副本，没有改动导出资源、原生参考轨迹或验收阈值。本批尚未接入普通 Demo。

## 原因与实现

原三频率对照失败可稳定复现。新增上游首次差异记录后，30 Hz 第 60 帧、60 Hz 第 120 帧、120 Hz 第 240 帧的步幅值均为 `0.572190523147583`，原生为 `0.5721905827522278`，相差一个 float ULP。随后播放速率分别为 `0.9986684322357178` 与 `0.9986683130264282`，时钟逐帧累积偏差。原 Parent/clock 的局部容差尚未超限，最终脚部位置和 FootPlanted 已超限。

核对本机 UE `CurveEvaluation.cpp::EvalForTwoKeys`、`CurveEvaluation.h::BezierInterp` 和实际 Engine DLL 后发现：当前原生构建将嵌套 Lerp 的加法重新结合，保留单精度中间量；原 C# 的六次独立 Lerp 虽数学等价，但舍入顺序不同。此外，`AlsMantlingCurveSource`（也用于 Refactored 序列及 Montage）此前使用通用双精度 Hermite/线性采样，导致原地转身曲线的另一类误差。

新增不可变 `AlsNativeRichCurve`，按原生 float 中间量与运算次序实现无权重 RichCurve；支持单键、Constant/Linear/Cubic、常量外推、输入校验。原生移动设置曲线和 Refactored 动画曲线共同使用此采样器。通用 `AlsCurveRuntime` 保留其原有契约；原设置的 cycle-with-offset 仍由外层负责。加权切线及未支持的外推仍由导入门禁拒绝。

未调整混合权重、状态转移、同步、原始骨骼键或 119/118 惯性化算法。完整链路结果证实本次超限可由上游两类曲线修正关闭，不声称骨骼输出逐位一致。

只读二进制核对：Engine BuildId `b4127720-ddfd-475f-a955-59a24fb7ace6`，`EvalForTwoKeys` RVA `0xe8d150`，其 float Bezier 实现 RVA `0xe4ebf0`。反汇编保存在 ignored `artifacts/tests/standing-precision/ue-richcurve-disassembly.txt`、`ue-bezier-disassembly.txt`；未编译、修改或启动 UE。本实现匹配当前已导出的 UE 构建，未来换引擎/编译配置须重跑原生参考。

## 严格结果

复用 `refactored_standing_host_trace.json`，资源哈希门禁照常执行；原生轨迹未重新生成。总计 2310 帧、140067 次骨骼求值、五种 Standing 状态、12 次 QuickStop，包含 update-only 与真实 Turn/Transition 实例。

| Hz | 骨骼求值次数 | 最大位置差（cm） | 最大曲线差 | 最大 Parent/clock 差 |
|---|---:|---:|---:|---:|
| 30 | 19908 | 9.1195543e-6 | 3.5762787e-7 | 0 / 0 |
| 60 | 40053 | 9.7340897e-6 | 3.5762787e-7 | 0 / 0 |
| 120 | 80106 | 9.6676729e-6 | 2.3841858e-7 | 0 / 0 |

位置预算仍为 `2e-5 cm`，旋转/缩放/曲线仍为 `2e-6`。播放权重最大差 `5.9604645e-8`。这里 Parent 指测试已比较的字段，并不代表未来完整角色所有 Parent 状态均已覆盖。

1238 个移动设置原生曲线样本最大差从 `1.1920929e-7` 降为 0，专项断言加强为精确相等。新增 Core 回归保护真实速度 200 的步幅值、分段边界、单键、不可变资源、非法输入、溢出拒绝及零分配。

## 回归与失败保留

- 基线 `standing-baseline.trx`：三项严格失败；上游诊断 `standing-upstream.trx`、`standing-stride-diagnostic.trx` 保留首次分歧。
- 修复后 `standing-native-richcurve.trx`：16 通过、0 失败、0 跳过（13 设置用例及三频率原生连续）。
- Core `native-richcurve-core.trx`：35 通过、0 失败、0 跳过。
- Import 扩展 `standing-precision-related.trx`：279 项执行，276 通过、3 项旧 LayerGraph 断言失败、0 跳过；包含 Standing 的三频率整帧取消重试、重复求值、稀疏/完整求值状态对照、重入和故障恢复，以及 Movement/SourcePlayer/BlendPose/Rest/QuickStop/Mantling。
- 修正该测试断言后仅重跑受影响类，`layer-slots-final.trx`：13 通过、0 失败、0 跳过。上述扩展集合的 279 项至此均有当前实现的通过证据；未冒充一次全绿全量运行。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

TRX 均位于 ignored `artifacts/tests/standing-precision/`。测试使用 Release、`DOTNET_TieredCompilation=0`。

共享采样回归：Mantle 序列 726 个曲线值、六个 Montage 的 726 个时刻/7502 个曲线值均与原生参考精确相等。未运行整个 solution 全量测试。最终 `git diff --check` 通过，导出 JSON 无改动。

扩展回归复现了已有的三频率 LayerGraph 覆盖断言失败（期望 8160、实际 4064）。原测试以 `AllMask & ~31` 推断七个区域 Slot，新增 Transition（bit12）后错误地把它纳入 Layering 图。已改为显式列出原图 Head/Spine/ArmLeft/ArmRight/Pelvis/Legs/Curves 七个 Slot，保留全图采样、故障取消重试及完整覆盖断言。没有将 Transition 偷加到上半身图，也没有修改生产 Slot 实现。

## 下一步与限制

下一阶段 R2：角色共享资源 ID/bank/queue、Standing 可组合子图与真实外层 Transition Slot，再推进 Crouching、完整主移动图、上半身/脚部与普通 Godot 入口。普通 Demo 仍使用已有链路；本批没有 Godot 运行、多帧画面、人工或十分钟性能验收。Ragdoll/Get-up/Pose Recovery 等旧目标保留，音频、道具物理和头颈专项继续按用户要求暂缓。
