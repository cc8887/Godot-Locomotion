# 四武器状态姿态叶节点

本批接在 `a29f051` 源更新之后，工作在主目录 main。新增 `AlsRefactoredWeaponEvaluators`，使用同一 SourceProfile 对应的状态节点集合，不创建动画时钟。

## 实现范围

- 实际嵌套图中的 Bow 13、Rifle 16、PistolOneHanded 11、PistolTwoHanded 12，共 52 个 SequenceEvaluator。每图两个 PitchAmount 驱动的 Aim，其余为固定帧。
- 校验 catalog 版本、实际源路径、完整嵌套图、runtime/authored 同步及 teleport/reinitialization 政策、无回调、帧号字面量、唯一允许的 ExplicitTime/GetParent/ViewState/PitchAmount 绑定。连接表达式和未知资源拒绝。
- 固定帧使用原帧率转秒后 float 的 UE 时间语义，预采样原 79 骨厘米姿态；Aim 使用原 mesh-space additive 编译器。曲线按显式名称合并，未出现的曲线保留 absence。
- 不与 SequencePlayer 共享可变采样器；每个 worker 独立 sampler 和暂存。非法节点/输入/输出布局在写输出前拒绝，Aim 在内部采样完成后发布。

## 验证

`artifacts/refactored-weapon-evaluators/evaluators.trx` 首轮 3 通过、1 失败：测试对 Rifle 数量误写 17，原导出实际 16；纠正测试，未改变生产资源或放宽比较。

`related.trx` 最终 100 通过、0 失败：过滤 `AlsRefactoredWeapon|AlsRefactoredSourcePlayer|AlsRefactoredAdditive`，包含新增四个武器用例以及已有原生时钟/权重、additive 源原生对照。

新增测试在三个独立 worker 中对全部 52 个节点按五个 pitch 采样，共 780 次，与直接资源采样逐值比较姿态及映射曲线；验证无效输入不修改输出。每图还拒绝四种 runtime 政策/源/帧变异及 PitchAmount→YawAmount 绑定变异。直接资源比较用于确认节点路由和时间/曲线映射，不等价于完整武器图的 UE 姿态对照。

`dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet`：0 warning、0 error。

无 UE 插件修改、重新导出、UE/Godot 场景运行、全量测试、十分钟性能或打包验证。

## 尚未完成

下一步将 evaluator 和已验证的 SourcePlayer 输出接入状态内 TwoWay/MultiWay/local/mesh additive 求值，复用更新候选中的 alpha 与相关性，不另建插值历史；随后完成每骨 QuickFeet 过渡、外层 Action 生命周期和完整四武器原生姿态对照。

完整 Overlay 连续姿态仍为 9/13，普通 Demo 尚未切到 Refactored 全链。本批不代表已修复普通 Demo 上身表现或完成 Ragdoll。真实 Locomotion、统一宿主、完整 Turn/dynamic transition 调用端、Mantle gameplay、Ragdoll/Get-up/Pose Recovery、完整相机和最终性能验收仍待推进；音频、道具物理及头颈诊断保持暂缓。用户已有文件改动保持不动。
