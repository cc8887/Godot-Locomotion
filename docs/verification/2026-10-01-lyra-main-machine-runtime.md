# Lyra Main LocomotionSM 连续更新宿主

2026-10-01，在主目录继续推进 ALS 模型上的 Lyra 移植。本批关闭 Main 状态选择、权重和更新遍历的连续组件对照；完整 Godot 源宿主、最终混合姿态与普通 Demo 仍开放。

## 实现

`LyraLocomotionMachineHost` 复用既有原规则选择器和 `LyraLocomotionPoseState` 的标准混合栈，维护角色独立的候选/提交历史。真实状态遍历输出包括状态、权重、Active、惯性同步上下文、初始化次数和 relevant-player 权重清除指令。上一记录权重与当前混合栈分开保存，隐藏帧提交上一记录为零；隐藏重入按原规则重新初始化，外层最终惯性历史不随机器初始化清除。

目录消费原 `bOnlyEvaluateWhenActive` 和 `bAlwaysResetOnEntry`。自动剩余时间规则保留原循环跨界判断和 ExplicitTime，允许原 Pistol FallLand 距离匹配的负时间；采样边界与规则观察边界分别处理。帧内取消、重复 Prepare、候选验证和旧候选提交拒绝均不发布历史。

新 UE 探针在原 Manny mesh/Character/Main/ItemAnimLayers 上执行原 LocomotionSM 和全部真实 Linked 子图。转发 Tap 仅登记实际 StateResult 初始化和 Update 上下文，保留子图链接和源；没有强制状态、选边、规则 bool 或替代姿态叶。Main 使用既有完整原线程安全更新，Layer 使用 PropertyAccess 批次。测试输入包含真实装备 Provider 类切换；GroundDistance 为显式地形观察，没有建立真实地形物理模拟。

使用原 Main proxy 的实际 Sync，一次调用当前 `FAnimSync::TickAssetPlayerInstances`。该 Sync 是 private 成员，探针通过 C++ 显式模板实例化取得有类型的成员指针；只用于原生 oracle 仪器，不使用内存偏移，不修改引擎，也不复制独立 Sync 来代替 Main 查询。输出记录实际 relevant player 的公开时间/Delta、原 Pivot 规则查询、上一权重、状态栈、真实源更新上下文和惯性请求。

## 验证

三个 Provider：Unarmed/Pistol/Rifle；30/60/120 Hz，每轨迹 12 秒。每条轨迹均经过全部十个实际状态，两个 conduit 不作为当前状态。

| 项目 | 结果 |
| --- | --- |
| 原生连续帧 | 7,560 |
| 实际子图更新 | 9,924 |
| Godot 选边 | 213，15 条实际边 |
| 原生 before/after 状态变化计数 | 219，包含自动重入导致的状态复位，不等同于选边次数 |
| 自动源剩余时间过渡 | 36 |
| 标准混合栈最大深度 | 3 |
| 未遍历帧 / 遍历但父上下文 inactive | 165 / 63 |
| 自动重入 | 27 |
| Main 惯性选边请求 | 6 |
| 循环公开时间跨界观察 | 4 |
| 原 Main Sync 有效观察 | 5,765 |
| 合法负 ExplicitTime | 84 |
| 真实 Provider 类切换 | 36 |

Godot 对每帧取消、重试、验证后取消和最终提交，与 UE 精确比较状态、elapsed、全部十二状态的当前/上一权重、初始化次数、活动栈端点/时长/elapsed/alpha，以及有序源更新的 weight/active/inertial；float 比较逐位，不放宽阈值。选中惯性边须存在相应原生请求，但尚未对所有子图惯性请求的完整消费顺序验收。

修订后的两次 UE 独立采集均正常退出 0，immutable JSON 相同。508 个原始资产包和 627 份此前 JSON 的 SHA256 不变；其中两份首轮机器 JSON 保留为历史诊断，最终门禁使用 `main_machine_runtime_v2_*`。探针 input/source/package 的四个源码哈希一致。UE 仍有既有 Footstep GameplayTag/导入等警告，最终采集无 Python 错误、assert 或 ensure。

Debug 和 ExportRelease Optimize 构建均为 0 错误、0 警告。本批规则回归为 576 原生 case / 1,536 rule / 480 selection；既有状态姿态栈 840 帧对照和四根共同原生 3,780 帧 / 680,400 骨比较通过。Core 生产代码未改，本批没有新增 Core 全量测试。

证据在 `artifacts/lyra-analysis/main-machine-*`，总门禁为 `tools/verify_lyra_main_machine.py`，摘要为 `main-machine-final-verification.json`。

## 修正与边界

首两轮编译的头文件/Sync scope 构造问题已修，失败日志保留。首轮采集把 GroundDistance 错当 double，改为实际反射数值写入。Godot 首轮拒绝合法负 ExplicitTime，修正后完整比较通过。

另外发现 UE 已弃用的 proxy tick 在当前版本多翻转一次 Sync 缓冲，首轮 7,560 帧全部 Sync invalid。首轮虽有十状态与权重对照，不能算正确 Sync 连续证据；已保留其 JSON/log，另建 v2 使用实际 Sync 原生 tick。最终 v2 中有效 Sync 门禁通过。

Godot 的 relevant source、Sync valid 和 Pivot notify 规则输入仍来自本批真实 UE 捕获。这证明机器选择/权重/遍历宿主，尚未证明 Godot 自己的全部源时钟能生成这些观察。真实 Provider 换类已发生，但本次 Main `LinkedLayerChanged` 字段与 Pivot 命名通知规则未出现 true；不计为这两个正分支的连续验收。四条 Main 惯性边中本次仅覆盖实际触发的一条；其余沿用既有规则组件证据，后续须补完整原生调用生命周期。

本批没有求值 UE 整机姿态，也没有把新机器接独立或普通 Demo。ALS 原 68 skin / 69 raw / 81 logical 资源策略继续沿用既有对照；本批的源机器探针使用原 Manny 资源，不将源端更新比较写成 ALS 整机姿态验收。

下一步把 Idle/Break/Air 的实际 Provider 源与姿态接入已有共同 Source Scope，分清“未遍历”和“遍历但 inactive”；Main 机器在同一候选中消费 Godot 的 relevant-source/Sync/通知历史，按实际初始化/清权重/遍历指令登记源，完成一次共同 Sync，再按原状态栈合成最终姿态。其后处理完整接口调用、命名/资源通知、Montage、外层惯性、足部、Gather/Worker/Commit 和普通 Demo 的视觉/多角色/性能矩阵。
