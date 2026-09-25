# 原地转身和动态过渡：真实 Montage 与 Standing 惯性退出

## 本批实现

- `AlsRefactoredRestMontages` 绑定原始八个转身、四个动态过渡动画，校验 Skeleton、Slot、Grounded 组、资源类型和播放速率。宿主分配动画 ID 与组 ID，可以与武器 Transition 共用一个 Montage bank。
- Parent 候选队列按 UE 的 Transition → Turn 顺序消费；预先校验两个请求及 bank 资源，成功播放后才确认 Parent 请求。stopQueued 阻止本轮播放而保留队列。
- `AlsRefactoredRestMontagePose` 使用物理 Montage Position 采样，转身为绝对姿态，动态过渡为 mesh additive；保留原 79 骨架和曲线存在性，不创建第二套播放时钟。
- 共享 Montage runtime 支持资产惯性退出：自然结束或显式停止立即移除该动画贡献，同时产生原时长的惯性请求。同组最后一次请求生效；新动画顶替旧动画仍使用新动画的普通 blend-in 设置。
- 请求队列与当前求值快照分离，支持取消重试及生命周期清理。当前快照以后发生的显式 Stop 请求，在下次快照生效。
- Standing Slot 60 只在 Idle 本帧实际更新时向外层 118 转发请求，未遍历的 Slot 不保留过期请求。请求携带角色、generation、frame、catalog 身份；118 保留原 RotationYawSpeed 排除规则。

## 依据与验证

本批只读核对本地 UE `AnimMontage.cpp` 的 Stop、动态 Montage 创建，`AnimInstance.cpp` 的同组替换、请求覆盖和 proxy 快照，`AnimNode_Slot.cpp` 的请求转发顺序，以及 ALS `AlsMontageUtility.cpp`、`AlsAnimationInstance.cpp`。本批无 UE 插件或配置变更、无新 UE 启动/导出或 DataValidation。

- Core Montage 相关测试：168 通过，0 失败。证据 `artifacts/tests/rest-montage/montage-inertial-core.trx`。
- Import Rest/Standing/Transition/Mantling 相关测试：189 通过，0 失败。证据 `artifacts/tests/rest-montage/rest-montage-related.trx`。
- 新增完整 Standing 转身退出测试：1 通过，0 失败。证据 `artifacts/tests/rest-montage/rest-montage-outer.trx`。
- 十二资源在 30/60/120 Hz 下累计 7560 个播放帧，每帧取消重试并比较 Slot 姿态和曲线，验证结束回到受控基底。Crouching 在该测试中使用受控 Standing 基底，不能代表完整 Crouching 图通过。
- 180 帧真实 Standing Idle Slot → 状态机 65 → 惯性节点 118：自然结束后原始 Slot 回到 Idle，118 保持有效平滑、Yaw 曲线直通；取消重试结果一致，最终惯性结束。
- Idle 隐藏/重新更新、过期请求、不同角色请求拒绝均覆盖。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v minimal`：0 警告、0 错误；`git diff --check` 通过。

实现过程中修复了局部函数捕获 `in` 参数、Span 传 IEnumerable，以及新增测试的 target-typed new 重载推断编译错误；这些均在最终通过之前处理，没有调整容差或跳过测试。

## 未完成边界和下一步

尚无 UE 连续 Rest Parent/Montage 惯性退出专用 oracle，以上是源码核对与本地实际资源联动证据，不等于原生整图逐帧对齐。未完成 Idle Slot 全权重时 Source 更新裁剪、最终 node68 封装、停止动作回调消费、完整 Crouching 和统一角色宿主。

普通 Demo 仍使用原有路径，本批没有 Godot 渲染、人工视觉或十分钟性能验收。下一步补原生连续对照和剩余 Standing/Slot 更新语义，再推进统一宿主与 Demo 接线；Ragdoll/Get-up、其余既定 ALS 模块目标仍保留。音频、道具物理和头颈问题按用户要求暂缓。用户未提交改动不纳入本批。
