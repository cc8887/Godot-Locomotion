# Fall 与父级 Jump 外层姿势

日期：2026-09-12。完整性修复第七十六批。承接 Falling Lean 专属采样，
原 P3–P7 目标不变，当前 Demo 基础移动视觉验收仍未完成。

## 实现

`AlsAirPoseCompiler` 使用正式 `v4_main_movement_graph.json`、缓存节点清单
及 Falling Lean 数据编译两条外层姿势链。来源摘要和动画定义由嵌套 Jump
编译入口校验；严格解析直接状态图路径，避免误把 Jump 的子状态图一并选中。

- Fall：FallLoop/快速 FallLoop → Flail 混合 → 局部空间 Falling Lean
  附加 → 预测落地混合 → ModifyCurve。
- 父级 Jump：自身嵌套 Jump 状态机 → 独立 Falling Lean 附加 → 独立预测
  落地混合 → ModifyCurve。嵌套机器保留 compiled identity 263，父机器 245。
- 预测落地先 Heavy 后 Light，使用有符号 FallSpeed 的 -1000 至 -500 cm/s
  映射。四个 evaluator 各自保留身份，固定读取零时刻，不使用来源数组里给它的时间。
- 严格校验预测插值 20/5、Flail/Fast 5/5、映射方向与范围、相关性更新/重置
  策略、Float Alpha、加法节点 Alpha=1/LOD=-1、输入变量、自身上下文、
  生命周期回调与姿势连线。支持的固定 V4 映射必须先通过该合同。
- 最后按原生 ModifyCurve Blend 写入 BasePose_N、Weight_InAir 为 1；
  其他曲线按来源、附加与 TwoWayBlend 规则保留存在性，不额外写入脚锁。

`AlsAirPoseGraph` 实际消费导入动画、借入的播放器/样本秒数与独立空中输入，
复用已有嵌套 Jump 姿势和两个独立 Lean 采样器。只执行相关分支：全权预测
可以跳过未初始化的嵌套 Jump 和无效但不活动的 Lean 时间；全权 Flail 不求
快慢下落姿势。内部只有可复用临时姿势缓冲，不拥有或推进来源时钟。

## 修正上批初始化语义

本机 UE `AnimNode_TwoWayBlend.cpp` 的 Initialize 保留 InternalBlendAlpha，
但清除 bAIsRelevant/bBIsRelevant。Evaluate 在 bBIsRelevant=false 时直接
选择 A。因此仅保留 Alpha、在初始化后立即按它求值是不正确的。

`AlsAirBlendInput.PoseAlpha` 现在在尚未 Update 时返回 A 分支权重；Alpha
和插值数值历史仍保留。无插值的预测 Heavy/Light 节点增加独立的更新标志，
同样在重入后首先取 A，直到真正访问该节点。一个状态未访问的输入仍不更新。
新增 Core 测试覆盖旧 Alpha 保留、实际求值退回 A、未访问预测子节点保留
数值，以及随后 Update 恢复正确轻落地分支。

源依据为本机 `AnimNode_TwoWayBlend.cpp`、`AnimNode_ModifyCurve.cpp` 与已有
正式原生图数据。本批未修改或启动 UE 插件，没有重复导出 Lean/动画资产。

## 验证

| 验证项 | 结果 |
| --- | --- |
| Core 空中输入专项 | Debug/Release 各 9/9，其中新增初始化回归 1 项 |
| 新外层图编译专项 | Debug/Release 各 18/18，包括正常绑定、图摘要和变异拒绝 |
| Core 常规 | 1977/1977，沿用排除独立 P5A golden/schema 两组的入口 |
| Import 全套 | 1287/1287 |
| Godot 优化构建 | 零警告、零错误 |
| 外层姿势回放 | 30/60/120 Hz，两状态共 2520 帧、22680 次曲线检查 |
| 分支覆盖 | 全权预测 532 帧、部分预测 1148 帧、基础姿势 840 帧 |
| 边界 | 12 次预测原动画零帧对照、12 次冷启动/重入、12 次无效不活动分支跳过 |
| 故障恢复 | 12 次非法 Alpha 拒绝、6 次已求基础姿势后的非法 Lean 时间拒绝，同帧重试一致 |
| 现有来源回归 | Jump 6684 帧，Landing 3780 帧，Standing/Detail/Pivot/Sprint，Main 六缓存 1680 帧通过 |
| Worker | single/parallel 各 180 帧、10 事件；并行晚期来源事件与整帧事务回滚通过 |

Worker 结果 `21E164D829153157`、完整姿势 `CF9225D4DE9B2C8B` 保持。
测试结果：`artifacts/test-results/air-pose/`。
最终新回放：`artifacts/air-pose-final.log`；现有回归日志前缀 `artifacts/air-`。

新回放明确使用人工给定的合法来源秒数和受控嵌套状态规则，不把它称为真实
Main Movement 时钟整合。它验证实际资源的外层连接和重入/相关性边界；
内部混合组合检查复用已验证的节点运算，12 组预测端点直接采样导入原动画。
没有新增 UE 完整 AnimBP 的逐骨轨迹或人工移动画面，不宣称整图原生等价。
新外层 smoke 没有单独进行零分配或十分钟性能认证。

## 首错记录

- 编译器首次用父状态路径前缀查找直接图，把 Jump 子图也选中。测试首次
  10/18，通过 Fall 拒绝案例但父 Jump 编译失败；改为直接图的精确完整路径。
- 下一次 17/18：篡改嵌套图的案例在来源编译阶段就被拒绝，实际异常类型是
  AlsCompilationException。测试改为检查这一明确的更早拒绝，其他案例仍
  要求 FormatException，没有放宽为任意异常或忽略非法嵌套图。
- Godot 首次构建遇到测试局部函数捕获数组的可空性分析错误。为已经在调用
  前构造的数组标明非空，最终构建通过；失败构建后没有运行旧 Godot 程序集。
- 代码复核时将测试场景的 switch 输入显式括起，保证预期的三个预测分支
  实际覆盖；最终以运行计数断言全权、部分与基础分支均被执行。

## 下一项实施与完整目标

1. 实现外层来源初始化与更新收集：Fall 的 Loop/Fast/Flail/Lean/Heavy/Light
   按 A→B 原生顺序；Jump 先初始化嵌套状态机，再初始化自己的 Lean 和预测
   来源。预测 evaluator 不参与 tick；新旧分支拥有独立 epoch 和输入历史。
2. 按实际分支相关性更新嵌套 Jump，保留祖先状态/惯性化上下文，处理全权
   预测导致的相关性间断和嵌套机器重入。Lean 使用真实网格权重进入共享批次。
3. 统一 Main Movement 候选状态、来源批次、真实 Grounded 缓存与最终姿势，
   再接 Slot/Montage、最终惯性化、状态事件及 Demo 提交。此前测试夹具的
   受控时间/缓存必须替换，不能仅把本组件创建在 Demo 里就算完成接线。
4. 继续最终 YawOffset 朝向反馈、动态 Layering/Add/LS、完整 Foot IK/Lock/
   pelvis/平台，再完成原 P5A–P7 全部功能与门禁。Overlay/道具保留，音频暂缓。

起步滑步、交错步、上身、平台锁定和最终性能仍未完成。本批没有改动已确认
的键鼠体验，没有 commit、revert 或合并分支；现有未提交改动继续保留。
