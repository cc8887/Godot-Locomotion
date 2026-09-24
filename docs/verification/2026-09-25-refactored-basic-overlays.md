# Feminine / Masculine 原版 Overlay

在主目录 main 扩展原 Default 图编译器和运行时，支持 Feminine、Masculine；没有复制项目，也没有修改普通 Demo 的动画入口。

## 实现范围

读取实际原资源确认两张图均为 14 节点，与 Default 同拓扑。严格校验具体 Blueprint、姿态资源、原始/编译节点、连线、回调、属性绑定、Sync 配置和固定帧政策；没有将图名称或资源内容替换为 Default 后再验证。

新增 BasicOverlayKind，保留原 Default API 默认行为。Feminine 使用 A_Als_Feminine_Poses、Idle 加法 .5；Masculine 使用 A_Als_Masculine_Poses、Idle 加法 1；Default 仍为 .75。共享预测/姿态算法，各运行时仍独立持有候选状态与输出。Idle 播放更新权重与实际加法权重都使用对应 profile。使用原有三固定帧、站立/蹲姿 MultiWayBlend、空中预测和 source-player 提交/取消协议。

## 验证

新增两个图政策测试及六个原生连续对照用例。政策测试拒绝错误资源、错误 Idle alpha、错误种类以及非法 enum，并检查上游权重乘入 Idle 更新权重。

每张新图执行 30/60/120 Hz 各三秒，共 1,260 帧 / 99,540 骨骼样本。UE 原图正常 Root → LinkedLayer → Overlay 更新和求值；C# 自主输入→预测→播放时钟→姿态/曲线，没有以 native 输出驱动。覆盖站立/蹲姿/行走、零 stance、空中预测升降与隐藏、零 delta、重初始化和每帧取消重试。

| 图 | 最大位置差 cm | 最大四元数分量差 |
|---|---:|---:|
| Feminine | 7.665139282548032e-14 | 4.440892098500626e-16 |
| Masculine | 9.237055564881302e-14 | 5.551115123125783e-16 |

两图 scale、曲线、预测、Idle 时钟差均 0，Idle 更新权重逐帧精确相同。沿用 P≤1e-10 cm、Q/S≤1e-12 预算，无放宽。Default 原有三频率对照仍通过，重新导出的 SHA256 仍为 B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003。

最终相关 Import 44 通过、0 失败/跳过；含本批新增 8 项。首次政策测试 9 通过（包含旧用例，非额外新增）。Godot Optimize 构建 0 错误/0 警告，Python 语法检查通过。没有新失败或算法调参。没有执行 Godot 场景、全量测试、性能或打包。

## UE 构建和参考文件

按 ue-diagnosing-plugin-build-load 技能完成完整 Editor target 4 actions 和所有项目插件审计。BuildID 4cd31a69-ae92-41ab-8118-ffba44340a1a；fingerprint 0206D23AEAB999FA9C3D9D1C58ED2D8729B7AF8FFF6428A2485E6A37C7D7D4BE。构建日志前缀 20260924T214206215Z-f262cff5d9ac473d94465509fe62df58 位于 UE Saved/Logs/PluginBuild。无 DLL 复制、Live Coding 或 BuildID 手工修改。

冷启动三图导出实际 exit 0，0 error/0 warning。新参考文件：

| 图 | 字节数 | SHA256 |
|---|---:|---|
| Feminine | 9357481 | 353ABD417D9EA6E635D8263DFC4391F2E2D7AC2BE719480D755D12E7F590E270 |
| Masculine | 9305189 | CFC8E6F2A02931E97F4062EE2ABBA076D77D0E159BDFFF3DDA28E786CC7BD69D |

普通 Editor 重启 Feminine PID 2624、Masculine PID 20360 均实际 exit 0，两份导出与冷启动逐字节一致。各次普通启动仍有两条旧 Condition failed 和五类旧警告（旧 AI/导航、引擎材质、控制台变量、Crowd），未宣称修复。DataValidation 实际 exit 0、0 error/3 个旧警告。详细测试与运行日志在 artifacts/refactored-basic-overlays。

## 后续

原版 Overlay 连续整图对照增至 7/13。剩余 Binoculars 28 节点、Torch 26 节点含双标签混合与网格空间加法；Bow 49、PistolOneHanded 44、PistolTwoHanded 46、Rifle 58 节点包含状态机，须继续按实际资源实现。

普通 Demo 尚未切换；实际 Locomotion/Standing/Crouching、完整角色统一事务/Notify/root motion、最终骨架布局宿主仍待接通。Ragdoll/Flail 旧失败、Get-up/Pose Recovery、Mantle gameplay、相机和十分钟性能/人工验收等旧缺口仍保留。音频、道具物理、头颈诊断暂缓，用户工作区修改未纳入提交。
