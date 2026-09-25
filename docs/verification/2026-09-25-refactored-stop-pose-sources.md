# Refactored Stop 姿态接线与源更新

本批在 `.` main 接续 Stop53 实现。完成原图资源编译和源更新遍历，尚未完成最终停止骨骼姿态合成，也没有切换普通 Demo。

## 原图编译

`AlsRefactoredStopPoseGraph` 覆盖五个状态内全部 34 个节点，交叉校验 runtime 与 authored 连线、输入绑定、节点所属图以及原始属性身份；拒绝未消费节点和额外动态输入。

- Entry 直接读取 Movement Details66；其余四状态在输出设置对应 FootLeftLock/FootRightLock 为 1。
- 五个缓存读者分别为 51、49、46、35、22，保持独立读取身份。
- 两个 Plant 状态读取上一批的十二个原始固定帧 evaluator。Forward、Backward、Left、Right 四通道使用原 VelocityBlend 和归一化 MultiWay。
- 横向通道使用 EAlsHipsDirection 的原枚举映射：LeftBackward 选择左侧第二姿态，RightBackward 选择右侧第二姿态。
- 左侧切入/退出时间均为 0；右侧进入第二姿态为 0.1 秒、退回为 0，Linear。首次更新立即选择目标。读取原 runtime 和可见 BlendTime 引脚，不误用模板中均为 0.1 的默认值。
- 原 LayeredBoneBlend 使用 mesh-space rotation、local scale、Override 曲线、层先于 base 更新。每侧的 thigh、ik_foot、VB foot 三个 branch 深度为 0；从原 79 骨层级构造遮罩并逐骨核对导出的 native perBoneBlendWeights，root 权重为 0。

## 更新遍历

`AlsRefactoredStopSourceRuntime` 接收候选 Stop 机器和包含角色/frame/counter 的上下文：

- 按实际状态初始化列表重置该状态的 selector，并保留每一次 cache 初始化读取；未被更新的 lateral selector 保留历史。
- 按状态更新序列、MultiWay 通道序列、BlendList 子节点序列生成固定 evaluator 更新，保留立即切换时旧子节点的一次零权重更新。
- 保留外层状态祖先、Inactive、惯性化 requester/skipped handler 及状态消息；腿部 layer 的 root-motion 权重为 0，base cache 读取保留原值和完整状态权重。
- 发出五个原身份的 cache66 读取，尚不在此处合并或执行缓存源；统一延迟调度仍由后续外层宿主负责。
- Prepare/Commit/Cancel 保持独立候选 selector 历史，允许只 Update 不 Evaluate 的帧提交；验证机器、资源、角色代际、帧和 counter 归属。

这些输出仍是源更新请求，不是最终 79 骨混合姿态。固定 evaluator 不创建播放时钟，也未接实际停止 Montage。

## 验证

主目录 Release 测试，记录在 `artifacts/refactored-stop-pose`：

- 新增 13 项全部通过：原图完整性、精确 selector 时序与隐藏历史、8 种资源变异拒绝、30/60/120 Hz 连续源更新。
- 连续测试共 1260 帧，每帧取消重试；覆盖四种停止目标、五读者、十二 evaluator、Entry/目标同时更新、零方向输入、零外层权重、Inactive、初始化与独立读取。
- `related.trx`：57 项通过，包含 Stop 姿态源、Stop 状态/叶子、Standing 和真实 Movement traversal 回归。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

首轮 `graph.trx` 失败：误要求 ModifyCurve 的 authored 模板 curveValues 为 1，原模板实际为 0、可见 CurveValues_0 引脚为 1。修为校验 runtime 与实际引脚；`graph-second.trx` 通过，随后 `sources.trx` 13 通过及相关 57 通过。失败记录保留。

无 Core 算法变更、本批 Core 全量、新 UE 导出/运行、Godot 场景或性能验收；未声称 UE 整图姿态等价。

## 下一步与完整性缺口

核对本机 UE `AnimNode_LayeredBoneBlend.cpp` 发现曲线求值还依据 Skeleton 曲线元数据中的 LinkedBones 选择/排除来源。当前 Refactored catalog 的 skeleton 字段包含骨架与 reference pose，但没有该曲线元数据；不能仅从 `curveBlendOption=Override` 推断最终曲线行为。下一步补只读元数据导出及校验，再实现 mesh-space 腿部姿态、曲线和 Stop 过渡栈合成，并做连续原生整图对照。

还需 cache66→67→方向缓存的统一延迟调度、停止状态回调/StopQuick 实际动作播放、外层118惯性化、普通 Demo 接入与人工视觉验证。Ragdoll/Get-up、其他原定模块和最终性能目标继续保留；音频、道具物理、头颈专项仍暂缓。

用户已有 project.godot、规划文档、AlsLayerBlendingRuntime 和未跟踪诊断/uid 文件保持不动。
