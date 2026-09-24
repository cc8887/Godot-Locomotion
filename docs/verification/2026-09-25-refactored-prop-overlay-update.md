# Binoculars / Torch 嵌套更新与原生轨迹

本批推进望远镜、火把的真实动画图，完成外层动作与内层瞄准混合的更新状态及共享 Idle 调度。尚未实现这两张图的 C# 姿态/曲线组合，因此完整整图连续姿态验证仍为 7/13，不能算作 9/13。普通 Demo 未改变。

## 实现

Core 新 AlsPropOverlayUpdate 复用实际线性四通道动作 BlendList 和 HermiteCubic 二通道瞄准 BlendList。动作目标时长 .5/.2/0/.2，瞄准退出/进入 .75/.2，Idle local additive .5。

只有外层 Default 分支被更新时，内层瞄准及 Idle 才更新；包含瞬时切换旧 Default 分支的一次零权重更新。其他隐藏帧保持瞄准历史，重初始化则清除该历史，并把 Idle 重置保存至下次实际更新。不是把 delta 乘以动作权重。

Import profile 从两张原图校验更新路径、实际标签/时长/Hermite、原始 ActiveTag 绑定、Root→动作→Additive→瞄准/Idle 连线、唯一 Idle 播放策略、无缓存/更新回调。它明确只验证更新路径，不是完整 pose 图编译器。runtime 提供 Prepare/Commit/Cancel 和 SourceInputs，所有参与者仍由未来角色宿主统一提交。

## 原生验证

导出器现在可加载实际 Binoculars 28 节点 / Torch 26 节点，设置父 LocomotionAction、RotationMode、PoseState、ViewState，正常执行原 Root→LinkedLayer→Overlay。记录实际动作/瞄准权重、Idle 时钟权重及完整姿态曲线。

两图各 30/60/120 Hz 四秒，共 1,680 帧。C# 从输入独立更新，逐帧候选取消重试，覆盖频繁瞄准切换、动作打断、零 delta、隐藏中 reset。每图隐藏帧 306、旧分支零权重更新 3 次、瞄准双权重有效帧 318。

动作权重、瞄准权重、有效 Idle 时钟最大差均 0；有效 Idle 更新权重逐帧相同。预先设置状态预算 2e-6 未调整。隐藏期间不要求已经重初始化的 UE Idle 内部时间与尚未消费 pending reset 的 C# 播放器旧时间相同；恢复更新时对齐。没有比较本批导出的姿态/曲线，这些参考供下一步 pose 实现使用。

新增 8 项测试（2 政策、6 native）；相关 Import 47 通过，0 失败/跳过。第一次政策 2 项也通过。Godot Optimize 构建 0 错误/0 警告，Python 语法通过。无算法调参/新失败，无 Godot 场景、全量、性能或打包验证。

## 构建与导出

按 ue-diagnosing-plugin-build-load 完整构建 Editor target（4 actions）并通过所有项目插件审计。BuildID 4cd31a69-ae92-41ab-8118-ffba44340a1a，fingerprint 7B77EF5C4F1072CB20D57DE94700D2CAAE4897BFE4A6C33CD8033CBA95B6E84F；日志前缀 20260924T215150278Z-e2b67d221a934b5c9747426198e46dd8。

冷启动及普通 Editor PID 43268 均实际 exit 0，两份导出逐字节一致：

| 图 | 字节数 | SHA256 |
|---|---:|---|
| Binoculars | 14319411 | E40F3837CE8D4331FF2C4B8E9EF695F91A352E5A586F4C81607AC09AC282A6CC |
| Torch | 14322067 | 7DDEB2ECF968D0CC8D154D0D94CB7D8C4A99DF5C24D6C690C313C6AE8A5A9C78 |

普通 Editor 仍有两条旧 Condition failed 及五类旧警告；DataValidation 实际 exit 0、0 error/3 旧警告。未声明修复这些旧问题。证据位于 artifacts/refactored-prop-overlays。

旧 Default / Box 重导出均实际 exit 0、0 error/0 warning，哈希仍为 B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003 / 1D63798296D82168D836EEE162666B710518BE3235BF69725C4B748B27EBD015。

## 下一步和边界

继续编译完整两图姿态路径：PitchAmount 驱动两种 Aim mesh additive；站立/蹲姿、行走分支；望远镜额外 Sprinting 混合及 .04166699945926666/.05833299830555916 显式秒数采样；两种道具不同 Get-up/Roll 曲线修改。Torch 未瞄准行走分支两个 evaluator 实际都为 frame 0，应忠实保留原资产，而非凭猜测改成 frame 1。

随后完成四种武器状态机、真实 Locomotion/Standing/Crouching、共享事务/Notify/root motion/骨架适配与普通宿主，再 Ragdoll/Flail/Get-up/Pose Recovery、Mantle gameplay、相机和十分钟性能/人工视觉等全部旧缺口。音频、道具物理、头颈诊断暂缓；用户工作区修改未纳入。
