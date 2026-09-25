# Refactored 起步与 Pivot 状态资源

## 已完成的范围

从现有、绑定 catalog 哈希的 `refactored_stance_machines.json` 和原 AB_Als_Standing 完整蓝图编译 Movement Details States。没有修改 UE 资产，也没有重新导出或修改插件。

保留六个状态的原始顺序：Walk、Run Start From Walk、Run、First Pivot、Second Pivot、Run Start；十一条边、各状态出口优先级、四个自动退出状态各自的四个播放器顺序。状态根与播放器均使用原 property index，不与运行时新 player ID 混淆。机器 property 117；规则查询的外层 Standing 机器 property 65。

这台机器每帧最多一次转换，首帧允许转换，初始状态 Walk。原 authored `stateMachineIndexInClass` 为默认 0，编译后的 runtime 为 1；不能要求二者相等。首次测试因此两项失败，已分别验证这两个字段，保留 `initial.trx`。

## 原规则

| 来源→目标 | 条件 | 转换 |
| --- | --- | --- |
| Walk→Run Start | Running/Sprinting，GroundedAmount >= 1，外层 Standing 权重 < 1 | .1 s 惯性化 |
| Walk→Run Start From Walk | Running/Sprinting，GroundedAmount >= 1，外层 Standing 权重 >= 1 | .1 s 惯性化 |
| Walk→Run | Running/Sprinting，GroundedAmount < 1 | .2 s 普通混合 |
| Run Start From Walk→Run | 相关播放器剩余时间，自动触发值 0 | .1 s 普通混合 |
| Run→First Pivot | bPivotActive | .1 s 惯性化 |
| Run→Walk | Walking 或空 Gait，UnweightedGaitRunningAmount < .2 | .1 s 普通混合 |
| First Pivot→Second Pivot | bPivotActive | .1 s 惯性化 |
| First Pivot→Run | 相关播放器剩余时间，自动触发值 0 | .2 s 惯性化 |
| Second Pivot→Run | 相关播放器剩余时间，自动触发值 0 | .2 s 惯性化 |
| Second Pivot→First Pivot | bPivotActive | .1 s 惯性化 |
| Run Start→Run | 相关播放器剩余时间，自动触发值 0 | .2 s 普通混合 |

共七条惯性化边、四条自动时间边；Second Pivot 优先检查返回 Run，再检查另一次 Pivot。普通混合采用 HermiteCubic；没有自定义曲线、每骨配置或边通知。

编译器沿原 native 图的实际连线解析函数、属性、字面值及 Standing 机器引用；验证最终表达式及节点全部消费，而非只按状态名字推断规则。Gait 使用精确 tag 相等；子 tag 不自动匹配。比较保留原 double 运算含义；.2 阈值由原图 `0.200000` 确认。

自动规则保留其类型、触发值和候选播放器，禁止把它当普通布尔规则调用。尚未实现这台机器的连续运行、相关播放器查询或惯性化请求消费；没有用状态持续时间代替原动画时间。

## 验证

- `artifacts/refactored-movement-details/rules.trx`：首次修正后 3 项通过。
- `related.trx`：新增 4、方向资源 7、移动播放器 5、stance 回调 4，共 20 项通过。
- 最终资源门禁复测 `final.trx` 4 项通过：覆盖十三项资源变异及六项原图输入／阈值／机器身份变异，另覆盖小于／等于／大于边界、空与子 Gait、非有限输入拒绝（与相关回归重叠，不累加为独立用例数）。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 成功，0 warnings / 0 errors；最终追加门禁后构建结果同。

没有新增 UE 连续 oracle、Godot 场景、全量或性能测试。此批证明资源与规则编译，不证明连续起步、Pivot 或视觉效果已完成。

## 下一步与保留项

把资源接到共享状态引擎，补齐基于上一轮相关播放器权重／时间的自动转换与候选惯性化请求；再接原起步／Pivot 源、回调、外层缓存统一遍历和原生连续对照。Standing/Crouching 主状态机及 Stop States 仍未完整移植。

普通 Demo 未切换完整 Refactored 链；实际 Parent、Notify、统一宿主、Ragdoll/Get-up/Pose Recovery、Mantle、Camera 和最终性能等既有缺口均保留。保留用户修改；音频、道具物理和头颈诊断暂缓。
