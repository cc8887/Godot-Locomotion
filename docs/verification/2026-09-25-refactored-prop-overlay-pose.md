# Binoculars / Torch 完整姿态求值

在主目录 main 完成两张原版 Overlay 的姿态/曲线求值，并与上一批实际 UE 原图连续输出比较。完整 Overlay 连续姿态对照由 7/13 增至 9/13；普通 Demo 尚未切换，不代表普通角色视觉或 Ragdoll 验收。

## 实现

- 新完整图 compiler 复用已验证的嵌套 update profile，继续校验 28/26 节点原始及编译连线、节点类型、属性绑定、无回调、literal、固定帧、显式秒数、网格空间加法、ModifyCurve 和入口政策。
- 冻结各图真实 Poses、Aim、Aim_Crouch 与 Idle 资源，统一原 79 骨 native cm 布局和曲线名称；角色独占 Aim sampler、scratch、候选状态及输出。
- 两张图分别执行站立/蹲姿 mesh additive（PitchAmount 直接作为显式秒数），再组合 stance、瞄准 Hermite、Idle local additive 和外层动作分支。
- Binoculars 保留 .041667f/.058333f 两处实际显式秒数与 Sprinting 混合；Torch 未瞄准 walking 两节点都是原资产 frame 0。没有按直觉更改资源。
- Get-up：望远镜右臂、火把左臂曲线设 3；Roll：望远镜双臂设 3，火把左臂设 3、左臂 additive 设 0。曲线存在性与权重沿用原生混合路径。
- Prepare/Evaluate/Commit/Cancel；重复 Evaluate 验证同一播放 owner，故障后阻止提交及读取结果；允许下游完全覆盖的 update-only 帧。与共享 source-player 的原子提交仍由外部角色宿主协调。

Core 原 Default Two/TwoCurve helper 仅开放调用权限，计算顺序未修改。没有插件/导出器变化，没有新资源快照。

## 原生对照

复用 a2bc14e 导出的实际原图参考，每图 30/60/120 Hz 各四秒，共 1,680 帧 / 132,720 骨骼样本。C# 独立从请求输入更新状态并采样；没有用 native 权重/姿态作为输入。逐帧覆盖取消重试、重复求值、动作/瞄准切换、站蹲混合、零 stance、俯仰、行走/冲刺、零 delta、隐藏和重初始化。

| 图 | 最大位置差 cm | 最大四元数分量差 | 最大 Scale 差 | 最大曲线差 |
|---|---:|---:|---:|---:|
| Binoculars | 5.684341886080802e-14 | 3.3306690738754696e-16 | 2.220446049250313e-16 | 0 |
| Torch | 6.23498720201505e-14 | 4.440892098500626e-16 | 2.220446049250313e-16 | 5.960464477539063e-8 |

预先设置 P≤1e-10 cm、Q/S≤1e-12、curve≤2e-6 未改变。曲线 presence 与名称集合逐帧一致。Torch 曲线不是逐位相同；没有放宽预算或为该差异修改原有混合算法。

新增 9 项：2 图政策、6 原生姿态、1 source owner 故障/恢复/NaN/过期帧/update-only 用例。最终相关 Import 60 通过、0 失败/跳过；Godot Optimize 构建 0 错误/0 警告。相关测试同时重跑上一批两图更新状态、Default/Feminine/Masculine、Box、缓存 Overlay、source-player 和 PostLayer。

本批未重新启动 UE/Godot 场景、未执行全量/性能/打包。原生冷/普通导出、完整 UE 构建审计和 DataValidation 证据属于上一批，见 2026-09-25-refactored-prop-overlay-update.md，不能说是本批新执行的检查。

## 失败记录

初次编译 MeshApply 参数顺序写错已纠正。首次测试 4 过/4 失败：通用 JSON 比较 helper 将 float literal 重序列化为短十进制后按 double 比较，导致实际相同 float 的 explicitTime 被误拒绝；改为 GetSingle 精确比较，无预算放宽。prop-pose-initial.trx 保留。

后续补强 literal 校验时，误把隐藏的 mesh Alpha 当作必定存在的暴露引脚，相关测试 51 过/9 失败；改为暴露时核对引脚，否则使用已检查的 authored/compiled alpha=1。prop-pose-related.trx 保留，最终结果见 prop-pose-related-final.trx。全部位于 artifacts/refactored-prop-overlays。

## 剩余范围

接下来四种武器 Bow、PistolOneHanded、PistolTwoHanded、Rifle 的实际状态机/播放路径，再真实 Locomotion/Standing/Crouching、统一角色事务/Notify/root motion 和最终骨架适配/普通宿主接入。普通 Demo 未切，Ragdoll/Flail 旧失败、Get-up/Pose Recovery、Mantle gameplay、相机复杂碰撞与十分钟性能/人工视觉验收等未关闭。

音频、道具物理、头颈诊断继续暂缓；用户未提交项目配置/规划/LayerBlendingRuntime 及诊断产物保留。
