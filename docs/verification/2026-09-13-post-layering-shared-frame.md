# Post Layering 三路真实姿势组合

日期：2026-09-13。第一百二十四批。继续原 P4 上身组合及 P5A 前置依赖。

## 原图关系与本批实现

正式 `v4_layering_inputs.json` 的 AnimGraph 将 BaseLayer、OverlayLayer、
BasePoses 三路送入 LayerBlending，结果保存为 `Post Layering`。外层 Aim
以此缓存为 Base 施加 Mesh Space Additive；Aim 不是分层图的第四路输入。
编译器现在校验上述父图接线、层名称及缓存名称，四种接错变体必须被拒绝。

UE `AnimInstanceProxy.cpp:126` 的 `UpdateAnimationNode_WithRoot` 在更新层根后
处理该层名称对应的 SavedPoseQueue；`AnimNode_LinkedAnimGraph.cpp:106`
将动态层名称传入此入口。不能先各自推进所有来源，再补写层权重。

新增 `AlsLayerBlendingFrameStage`，复用正式的 80 节点 LayerBlending 与
BasePoses 运行时，使用原始关键帧、重定向后的 79 根逻辑骨及按名称映射的
完整 BaseLayer 曲线。源姿势来自真实 BaseLayer、Overlay 和两份 BasePoses
资产，不使用参考姿势代替相关的输入。

受控组合入口为 `overlay_shared_frame_smoke.tscn -- --layer-blending`：

- LayerBlending 的实际缓存访问提供三路输入的相关性及更新权重。
- BasePoses 相关时顺序为 BasePoses、BaseLayer、Overlay；不相关时只有
  BaseLayer、Overlay。初始化与本帧 Update 是不同事件。
- BaseLayer 完成来源收集后才准备 Overlay，两者随后进入一个共享采样批次。
  BasePoses 是固定时间 evaluator，不另外推进移动/Overlay 的时钟。
- 上一已提交 Post Layering 曲线供下一帧分层属性和 Overlay 使用；冷启动
  不预填 Weight_Gait、BasePose_N 或 Enable_Transition。
- 分层输出、BasePoses 历史、移动/Overlay 姿势与事件在所有候选验证后提交；
  分层求值失败会取消，不能提前发布曲线或 BasePoses 状态。

Core 两个运行时暴露 `ValidateCommit`，以便组合调用者先检查全部候选再提交。

## 组合测试中的两次错误假设

首次要求每帧更新三路输入不符合原图：加法层不相关时，BasePoses 不收到
Update。测试改为检查原图允许的两种访问顺序，并确认两种情况都有覆盖。

原 6 秒测试先静止、再瞄准、然后移动、最后换武器。旧模式手工预填步态
曲线为 2，静止期间就能访问步枪 Relaxed 的运行手臂来源；真实反馈冷启动
没有该曲线。瞄准结束又进入 Ready，原图回 Relaxed 的边带有 3 秒及曲线/
移动条件，测试在返回前已经换武器，因此 `mixed=0` 是场景覆盖不足。
这里的 Ready 等待不是交错步/换髋等待，不能混为一条通用延迟规则。

新组合增加瞄准前 0.05–0.3 秒移动，保留原状态条件和真实曲线。最初过短
的移动窗口只使 Weight_Gait 升到 0.834，尚未使运行手臂分支相关；诊断见
`artifacts/post-layering-start-diagnostic.log`。最终另检查来源本帧权重相关、
时钟相对已提交值确实改变，避免仅以保留的非零时间宣称正在同步。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Debug 优化构建 | 0 警告、0 错误 |
| Import 父图编译与 BasePoses 专项 | 94 通过 |
| Import LayerBlending/共享绑定运行时 | 16 通过，1 个既有普通 Editor 重复检查跳过 |
| Core 分层骨骼、BasePoses、LayeringInput | 60 通过 |
| 三路真实组合，30/60/120 Hz | 1,260 帧及 1,260 次取消重试通过 |
| 晚期失败 | 26 次 Overlay、11 次 LayerBlending；其中 6 次含过渡资产通知 |
| 实际事件 | 33 个，其中 6 个过渡资产事件；3 个状态机通知与 3 个播放请求 |
| BasePoses 条件访问 | 1,170 帧更新，90 帧跳过 |
| 移动与步枪来源 | 18 次相关来源实际推进；966 次保留时间/移动 leader 观测不能当作推进帧数 |
| 原组合模式回归 | 1,260 帧、30 个事件、26 次失败重试通过；168 次相关 follower 推进 |
| 原生产 Worker 回归 | single/parallel 各 600 帧通过；28 个事件，lag/stale 均为 0 |

最终组合日志：`artifacts/post-layering-shared-final.log`。
原组合回归：`artifacts/post-layering-shared-baseline.log`。
TRX：`artifacts/test-results/post-layering-import.trx`、
`post-layering-runtime.trx`、`post-layering-core.trx`。

Worker 两模式 result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
root=`A4F6C26CBAB8A0E7`，与上一批一致。日志为
`artifacts/post-layering-worker-single.log` 和 `post-layering-worker-parallel.log`。
此回归验证原生产 BaseLayer 未退化，不代表新组合已经在 Worker 启用。

## 完成边界与下一项

本批接通的是受控组合中的 Post Layering 中间输出。默认 Demo 仍使用
75/109 的 BaseLayer 入口，尚未切换到完整最终上身；不能宣布双臂、换髋、
交错步或起步滑步已解决。本批没有新增独立 UE Post Layering 逐骨骼输出
对照，也没有做默认 Demo 的截图/人工验收。

BasePoses 已转发实际初始化/骨骼缓存访问；BaseLayer/Overlay 的全局冷启动、
重入访问仍需要最终图所有者统一。当前阶段使用固定初始化/骨骼缓存计数，
不支持最终根切换或骨架重建。外部 skipped-update handler 尚未接入，遇到
该回调会明确失败。七个上身 Slot 当前没有注册物理播放资产，仅覆盖直通。
测试中的共享来源延期回调不是生产 Worker 的最终帧所有者。

下一步依次完成：最终根遍历与缓存所有权、外层 Aim/脊柱/手部约束、共同
最终姿势/曲线发布及 Worker/Demo 接入，再完成 FootIK/FootLock/pelvis 与
平台约束。基础上身和脚步问题在 P3/P4 内验收，不推迟到 Mantle/Ragdoll。
随后继续 P5A 通用动作剩余项、P5B Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 Ragdoll/Get-up/Pose Recovery/Camera，最后 P7 十分钟预算。

既有全 Core 的 23 项失败、Import 零分配测试不稳定、p95 2.559ms 超过
2.5ms 的预算记录仍未关闭。本批专项通过不等于全量通过。音频继续暂缓。
