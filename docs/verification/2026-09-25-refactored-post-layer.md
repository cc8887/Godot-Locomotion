# Refactored 分层与 Head 组合阶段

新增 `AlsRefactoredPostLayerRuntime`，将父 View/Spine、真实 Refactored Layering 图和 Head 放入同一个候选帧生命周期。`AlsRefactoredPostLayerCompiler` 绑定已有 graph/inventory/base/head 输入编译器，并校验两端完整逻辑骨名与父级一致；每个角色创建独立 Look sampler 和运行时缓冲。

## 宿主边界

1. 宿主提供同一个 `AlsAnimationGraphFrame`、角色输入和已经冻结的 linked-input/Slot provider。运行时保留原生厘米和完整逻辑骨架，不混用 V4 FBX 布局。
2. ViewBlock、PoseAiming 和所有 LayeringState 属性来自组合阶段上次提交的曲线。输入 record 中当帧的两个同名值被替换，避免用当前求值结果反过来驱动同一帧更新。
3. Prepare 先更新父 View/Spine，再执行 Layering 上游遍历和 Head 回调；Evaluate 先求分层姿态，再转换到精确姿态类型执行 Head。此处浮点分层结果转换为 double，不恢复已经损失的精度。
4. 只有成功求值或者显式整图未访问的候选可提交。提交前同时验证 Layering 和 Head；提交一起发布 View/Spine/Head/curve feedback。任何上游或 Head 采样失败自动取消组合候选。
5. 外部 Locomotion、Overlay、Montage 和 Notify 仍属于宿主：宿主必须先验证所有 owner，再一起提交。这个类不会独立推进播放时钟，也不替外部 provider 回滚其状态。已有区域 Slot decorator 可以作为 provider 使用，但本批测试不包含活跃 Montage。

## 验证

实际 94 节点分层图、原始 Stand/Crouch 数据、真实 Look 采样用于三频率各两秒共 420 个提交帧。两个独立 owner 比较：一个直接执行，另一个逐帧丢弃重试。所有最终姿态、曲线及父/Head 状态相同。覆盖 .2/.4/.6 等分层权重、ViewBlock 0/.5/1 的跨帧反馈、整图隐藏恢复。

每个频率两次在 Head 采样后注入异常，验证已提交父状态和 Head 均不改变，重试输出与无异常 owner 一致。另有上游采样异常、过早提交、错误角色身份、旧帧重复和缺少反馈通道拒绝检查。输入中故意传入 ViewBlock/PoseAiming=999，验证 Head alpha 始终由上一提交帧曲线驱动。

新增 4 项测试通过，最终 Import 定向共 64 项、Core View 定向 12 项通过，Godot Optimize 构建 0 error / 0 warning。首轮测试夹具因局部函数捕获 Span 编译失败，改为显式传 Span 的方法后通过；生产运行时未因测试放宽策略。日志在 `artifacts/refactored-post-layer`。

没有新增 UE 导出或修改插件，没有全量回归、Godot 场景验证或组合链的新增原生 oracle。各子图既有原生对照随 Import 回归通过，但不将其当作整个组合链的原生等价证明。

## 下一步

普通 Demo 仍使用 V4 分层宿主，尚未切换。继续接入实际 Refactored Locomotion/Overlay 输入、共享 Montage/Notify 帧、完整骨架与视觉适配，再进行场景回归及普通 Mantle 接入。全部既有 Ragdoll、相机、完整性能验收缺项仍保留。用户暂缓的头颈拉伸调查、道具物理与音频不在本批恢复。
