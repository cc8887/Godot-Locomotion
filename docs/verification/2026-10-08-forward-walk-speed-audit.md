# 前向速度与起步位移复核

**结论：前向物理速度尚未降低。** 2026-10-08 对普通输入复测当前激活 v8，并用原版资源作为同场景基线。两者稳定前进均为 `1.7500018 m/s`，120 个采样帧内胶囊位移约 `3.47 m`；HUD/运动输入的期望速度为 `1.75 m/s`。配置来源是 [p3_locomotion_settings.json](../../assets/config/p3_locomotion_settings.json)，其中 `walkForwardSpeed=1.75`，`animatedWalkSpeed=1.5`。

逐帧记录显示 `rootEnabled=false`、`sampleRoot=false`、`rootSource=false`、`consumedRoot=false`。因此此次测得的速度来自角色运动配置，不是动画根骨骼位移，也没有因切换到 Kimodo 动画而增加。G 候选前向四相位测试的胶囊速度同样是 `1.75 m/s`；该轮门禁只比较足底滑移，没有比较绝对移动速度或原版/候选的胶囊速度差。

当前 v8 候选前向运行以 541 个 `Body detached during start, held input or release` 违规失败。其稳定步行足底滑移中位数为 `56.19 cm/s`，且有一侧没有足够的近地采样。这个结果说明 v8 的前向动画质量仍不可接受，但它与胶囊速度是两个独立指标。

## 起步位移

前一条答复漏读了从静止到移动的骨盆轨迹，只引用了稳态胶囊速度。此次数据原本包含从第 1 帧开始的逐帧采样，输入在第 31 帧开始；问题是我没有把启动段纳入结论。

当前 v8 在第 31 帧胶囊只移动 `0.56 cm`，但最终显示骨架的 pelvis 相对 root 水平偏移随后快速增大：第 34 帧 `54.9 cm`、第 36 帧 `111.2 cm`、第 40 帧 `219.3 cm`，第 55 帧达到 `248.2 cm`。同段胶囊到第 36 帧总共只移动 `11.25 cm`。全程 `rootEnabled=false`、`sampleRoot=false`、`consumedRoot=false`，而 root 骨骼保持原点。因此这是动画里的 pelvis/Hips 位移，不是胶囊移动或 Skeleton Root Motion。

对照下，原版同段 pelvis-root 水平偏移最大 `3.8 cm`；未激活的 Kimodo G 候选最大 `6.3 cm`。G 的起步数据已消除这次大位移，但其余方向和完整 71 项矩阵仍未通过，因此默认 v8 指针保持不变。诊断使用最终显示骨架 `GetBoneGlobalPose`，并在 pelvis-root 水平偏移超过 `0.5 m` 时失败；v8 从第 34 帧开始触发该门禁。

静止、起步和对照帧截图：[v8 第 35 帧](../../artifacts/tests/2026-10-08-forward-speed-audit-v8/candidate-walk-scheduled/forward-35.png)、[原版第 35 帧](../../artifacts/tests/2026-10-08-forward-speed-audit-v8/original-walk-scheduled/forward-35.png)、[G 候选第 35 帧](../../artifacts/tests/2026-10-08-periodic-repair-v1/right-lock-g/candidate-walk-right-lock/forward-35.png)。逐帧骨骼与胶囊数据在各自 JSON 中。

完整逐帧数据、日志和截图：[本轮目录](../../artifacts/tests/2026-10-08-forward-speed-audit-v8/)，摘要见 [summary.json](../../artifacts/tests/2026-10-08-forward-speed-audit-v8/summary.json)。本轮没有修改 `walkForwardSpeed`。若产品目标要求低于 ALS 原始配置，需要先确定新的目标速度，再调整运动设置并以绝对速度和足底滑移分别验收。
