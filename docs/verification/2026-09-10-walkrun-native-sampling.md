# WalkRun 原生采样补完

日期：2026-09-10。接续方向完整性修正，保留已有未提交改动，未 commit。
范围是原计划 A 的 BlendSpace 轴滤波与权重，不代表基础移动或后续阶段全部完成。

## 源行为与落地

只读导出六套 `ALS_N_WalkRun_F/B/FL/BL/FR/BR` 的原生属性，确认：

- `bInterpolateUsingGrid=true`，两轴范围均 0..1，GridNum=1。
- Stride 轴 `BSIT_Cubic`，InterpolationTime=0.5848035216331482 秒。
- Walk/Run 轴 `BSIT_Cubic`，InterpolationTime=0.7368062734603882 秒。
- TargetWeightInterpolationSpeedPerSec=0，不使用额外样本权重速度限制。
- 原生网格四角分别为 WalkPose、Walk、RunPose、Run；不是三角插值。

`AlsWalkRunBlendSpace` 使用时间窗内历史样本的 `1 - (age/window)^3` 系数归一化。
它不是 Cubic 状态切换曲线或指数平滑。首次有效输入不补虚构的零历史；delta<=1e-4
保持上一输出。网格权重是双线性的，中心四个样本均为 0.25。

滤波状态使用调用方拥有的 256 项定长缓冲，随 PreparedApply 候选值复制/提交/回滚，
不在图对象中维护可泄漏的可变历史。容量不足明确失败，不丢弃仍在时间窗内的样本。
当前资源窗口和 30/60/120 Hz 有余量；这不是任意超高帧率的无界存储实现。

Godot 初始化读取 `assets/config/v4_walkrun_sampling.json`，检查六方向顺序、网格、
轴模式、时间窗相同且有限、样本名称与已编译动画闭包一致。六个方向当前共享相同输入
与同步激活的滤波历史；后续原生状态激活/重置语义若使它们分离，需按实际播放身份拆分。
运行时不读取原生 trace，不新增 UE 依赖。

## 原生对照

新增 `UAlsBlendSpaceTraceCommandlet`，直接调用当前 UE 的
`UBlendSpace::FilterInput` 与 `UpdateBlendSamples`。不是用 C# 结果生成预期值。
六资产 × 30/60/120 Hz × 4 秒，共 5040 帧，包括 Stride 和 Walk/Run 阶跃与回切。

- 原生输出：`tests/Als.Core.Tests/Fixtures/P3/v4_walkrun_native.json`。
- SHA256：`816EECB220D05798E8359988A01C3C2E78A5FBEA3F721FCDF34822C5E8A5396B`。
- 该文件中全部滤波输出与四个样本权重，C# 对比误差均不超过 1e-5。
- `scripts/compile-walkrun-sampling-profile.ps1` 从原生输出生成运行时设置，并记录 trace hash。

依据 `ue-diagnosing-plugin-build-load`，两次完整 Editor target 构建/审计均通过。
为编译并加载新命令，在 UE 源项目显式启用已有 AlsGodotExporter，并只部署新增的
commandlet 源文件；没有覆盖既有插件源文件，没有保存动画、蓝图或模型资产。
最终审计包含 AlsGodotExporter、AutoTestTools、BlueprintLisp。命令退出 0：
`ALS_BLENDSPACE_NATIVE_OK assets=6 rates=3 frames=5040 assets_saved=0`。

这不是 UE 插件发行认证：未执行打包构建、完整资产 DataValidation 或人工 Editor 验收。

## Godot 与逻辑验证

- 最终 build：0 warning / 0 error。
- 方向与新采样测试：42 passed，其中原生对照覆盖全部 5040 帧。
- Core Release 广泛回归：1316 passed（排除 Golden/TraceSchema）；之后增加的一项
  非有限值保护测试已纳入上述最终 42 项定向测试，没有冒充重跑全量。
- StandingCycle：30/60/120 Hz × 3 起始相位；9 次换髋、222 等待帧；候选回滚后重试
  姿势一致；稳态 0 B。独立浮点曲线 603 样本检查仍通过。
- `verify-p4-pose.ps1` 通过：旧图、姿态、single/parallel 地形脚部修正与晚期回滚。
- 新 Cycle 图额外跑生产 FrameOrder：single/parallel 各 180 帧，包含跳跃、落地、
  generation 替换。结果 digest 都为 `B65427ED34BB7A6F`，full pose 都为
  `9F13F4E651E0BC68`，无 lag/stale，两个线程模式均观察到正确 affinity。
- 新图额外跑两种模式 BeforePublish 故障：Cycle 状态/滤波输出与已提交值一致，
  runtime/result/controller/P4 banks/pose/root 回滚通过，无结果泄漏。

这些是单角色生产路径检查，不是新图的 10 角色性能矩阵，也不是 P5A 完整事务认证。

## 画面与未解决问题

| 回放 | 帧/截图 | 最大单帧脚旋转 |
| --- | --- | --- |
| `artifacts/native-sampling-strafe` | 720 / 120 | 14.122° |
| `artifacts/native-sampling-run` | 720 / 120 | 11.509° |
| `artifacts/native-sampling-rapid` | 720 / 120 | 12.514° |

查看了横移多帧接触图，模型与动画非空，换向前后姿势连续；仍不能据此证明 UE
最终姿势等价。跑步 turn=0，Turn 覆盖由另外两组和 StandingCycle smoke 提供。
镜头与键鼠源码 hash 与已确认版本一致，没有修改控制手感。

横移起步低位脚水平位移峰值 6.823568 → 5.156082 cm/帧，仍未消除。
换髋开始 frame 292/499，等待 53 帧；`StartupAccepted=false` 保持未验收。
该指标仍是低位脚代理，不是 UE 支撑脚接触的完整对照。

尚未完成：ShouldMove/Stop/Pivot、状态进入/重置/转换中断、动态 Leader/Marker 时钟、
Pose 端参与的原生动画时长与轴缩放补偿、多样本最终姿势/曲线合成、P5A 生产接线，
以及 P5B/P5C/P6/P7。不能把 5040 帧权重对照当作完整角色行为的跨引擎验收。

## 下一入口

`artifacts/locomotion-sampling-audit/_N__Locomotion_States.t3d` 已导出完整起停图。
当前核实：Idle→Moving 使用 ShouldMove，0.3 秒；Moving→Stop 要求 !ShouldMove
且 Moving 状态权重=1，0 秒；Stop→Idle 要求 Stop 状态权重=1，0.3 秒；
另有优先级 2 的 !ShouldMove QuickStop。接下来必须连同 Stop 内的 Foot Up/Down、
Lock/Plant、Pivot/StopTransition 通知与图内曲线一起重建，不能仅把 0.2 改成 0.3。
