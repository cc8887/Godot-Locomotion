# Grounded 过渡资产通知与统一帧提交

日期：2026-09-13。第一百二十二批。承接原 P5A 前置依赖，继续补齐
P3/P4 最终上身所需执行链；本批不是完整 Demo 视觉验收。

## 实际接入

- 从本地 ALS V4 原资产补采 Transition L/R 的播放倍率、长度和各两条
  Footstep_AnimNotify，保留对象身份、触发偏移、权重门槛和原生过滤策略。
  新数据为 `assets/config/v4_transition_notify_inputs.json`。RateScale=1，
  长度约 2.333333 秒，通知触发约 0.795465/1.331635 秒，权重门槛 0.3。
- 通用 Montage 通知编译器追加两个范围、四条正式绑定；原有 Turn/Roll
  范围和身份前缀保持。缺失数据、倍率/长度不符、身份冲突会拒绝编译。
  最终共 12 个范围、34 条通知。事件沿用已有正式 payload，不新增音频
  或脚步玩法消费者，不能把“通知已派发”写成脚步效果已经完成。
- BaseLayer 帧所有者负责 Grounded Slot 的物理实例推进、冻结求值、姿势、
  通知以及提交/取消。受控组合场景移除了手动 Begin/Commit 蒙太奇的接线；
  正式映射帧也使用同一个 Grounded Slot。Overlay 请求在本帧求值后进入
  候选，下帧参与采样，失败不会泄漏已提交状态。
- `PlayOverlayTransitions` 校验帧身份、请求顺序和正式绑定，再向共同物理
  所有者播放。新增通知与原来源事件经过同一事务，不另开通知或播放时钟。

## 验证

- UE 完整项目构建和插件审计通过；冷启动与普通 Editor 的两次导出退出 0，
  数据字节一致。SHA256：
  `FC248FC2E5D5F160E587A7A6836056CCEF4D2DB861374FB5D13A12D5C4F8D146`。
  只新增 Python 导出脚本，未改 UE 插件 C++ 或配置。
- 最终 Godot 优化 Debug 构建 0 警告、0 错误。通知编译专项 23 项通过，
  包含新增 10 项：精确绑定、三档频率下可见/隐藏通知、物理身份及取消重试、
  元数据缺失或不一致的拒绝。没有降低原始通知门槛。
- 最终组合回放 30/60/120 Hz 共 1,260 帧，1,260 次同帧重试通过。
  3 次状态通知、3 次实际过渡命令、284 帧加法播放，事件从原 24 增至 30，
  其中 6 次来自过渡资产。26 次晚期失败包含这 6 个有通知帧，验证已提交
  物理实例/通知状态不变，重试的姿势、曲线、事件及身份一致。
  日志：`artifacts/transition-notify-shared-rollback-final.log`。
- BaseLayer 回归 3,360 帧通过。原键鼠 Worker 单线程/并行各 600 帧通过，
  各 28 个事件、lag/stale=0，摘要一致：result=`EAAF62E4D0A80A76`，
  fullPose=`EE519FBE375F4A2B`，root=`A4F6C26CBAB8A0E7`。
  日志：`artifacts/transition-notify-base-layer.log` 和
  `artifacts/transition-notify-worker-single.log` / `parallel.log`。

首次 Import 全量为 1,959 通过、1 失败、1 既有跳过。失败为未修改的
`SnapshotViewsAreImmutableAndDoNotAllocateAfterWarmup`，分配实测 5,792
字节而期望 0；单独运行随后通过。保留首次失败日志
`artifacts/transition-notify-import-regression.log`，不放宽零分配断言。

随后完整复跑仍为 1,959 通过、1 失败、1 既有跳过，但原失败项通过，
改为未修改的 `CombinedStateAndCacheCandidateIsAllocationFree` 测得
512 字节。记录在 `artifacts/test-results/transition-notify-import-repeat.trx`。
这表明尚有需排查的分配测试稳定性问题，不能据此直接断言生产代码
无分配，也不能把不同位置失败归因于本次通知代码。
两项随后隔离合跑均通过，结果保留在
`artifacts/test-results/transition-notify-allocation-isolation.trx`；这次通过
不覆盖或撤销完整运行的失败结果，根因仍待定位。

## 完整性边界与下一步

通知接入和 BaseLayer 所有权已完成；新增 Grounded 加法 Slot 的独立 UE
混合/中断数值对照仍未完成。现有原生通知导出不能替代这个对照。

下一项先补该原生对照，再完成最终图初始化、缓存更新遍历和
Aim/Overlay/BasePoses/LayerBlending 组合，把最终曲线反馈到角色旋转及
Foot IK/Foot Lock/pelvis。之后做同输入、同脚相位的 UE/Godot 多帧对照，
检查上臂/胸髋、A→D 与 D→A 换髋等待和支撑脚滑移，并进行人工验收。

默认 Demo 仍输出基础移动图，尚未启用最终上身；本次 223/257 来源组合
属于受控联调。不能声称双臂、侧身、交错步或起步滑步已最终修复。
P5A 剩余通用动作、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能全部保留。
既有全 Core 23 项失败、p95 2.559 ms 超过 2.5 ms 预算未在本批关闭。
音频继续暂缓；本批没有 commit、revert 或合并。
