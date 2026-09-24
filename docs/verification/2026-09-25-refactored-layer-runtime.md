# Refactored 分层共享运行时

本批在主目录 `.`、`main` 实施，承接 `2026-09-25-refactored-layering-contract.md`。将 Refactored 属性和曲线尾部算子接入既有 LayerBlending 运行时，复用缓存、更新/求值及候选提交/丢弃机制；未新增播放时钟。

## 实现范围

- 图定义显式区分 V4 与 Refactored 属性模式，默认仍为 V4。Refactored 输入提供原生 LayeringState 的 22 个属性名，拒绝混用输入。View 因子不冒充 LayeringState 属性。
- 增加固定全权重 CurveAccumulate、CurveOverride、CurveReset 节点。前两者按基础分支、曲线分支顺序更新和求值，保留基础骨骼姿态；Override 替换完整曲线集合，包括存在性。Reset 写入已声明的值并标记存在；本批不是通用动态 ModifyCurve 实现。
- 重用原缓存调度，曲线分支可以独立请求缓存。Curves Slot 完全覆盖时，由原 Slot 相关性机制停止源分支更新和求值。
- Refactored Prepare 复用既有反馈身份检查，拒绝重入前不覆盖候选输入；失败或主动丢弃后可重试。

## 验证

新增测试使用受控的两骨骼、13 节点组合图，包含 TwoWay、骨骼过滤、共享缓存、Reset、Slot、Accumulate、Override。它不是完整原生 Layering 图，也不是 UE 逐帧运行参考。

- Core Release 定向 `FullyQualifiedName~AlsRefactoredLayer`：10 通过、0 失败，包含新增 6 个用例。覆盖负数/部分/全量/超量属性、骨骼与曲线独立分支、Slot 隐藏源、整图不相关、输入模式拒绝、重入保护和失败重试。
- Import Release 定向 `AlsLayerBlending|AlsRefactoredLayeringGraph|AlsLayeringInput`：65 通过、1 个既有条件跳过、0 失败。跳过项 `NormalEditorRepeatsTheConsumedGraphAndInputSemantics` 需要 `ALS_LAYERING_REPEAT_FILE`。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。
- 既有普通入口相机 smoke，headless、60 Hz、并行模式：480 帧通过，包含倒地、起身和第一人称切换，日志标记 `ALS_NATIVE_CAMERA_DEMO_OK`。这是原 V4 生产链回归，不是 Refactored 完整图场景验收。
- 记录：`artifacts/refactored-layer-runtime/core-first.trx`、`import-layer.trx`、`demo.log`。`git diff --check` 无空白错误。

本批没有修改 UE 插件、重新导出资产、执行全量 Core/Import 或最终视觉/性能验收。保留用户的 project.godot、P4 规划、头颈诊断和外部生成的 uid 文件。

## 后续

1. 导出并编译 Refactored 实际节点清单和原生缓存更新顺序，接入完整骨骼分支；不能用测试图顺序替代原生顺序。
2. 补齐 MultiWay 基础姿态分支、Refactored 虚拟骨语义和区域 Slot，接入 Head/View 图。
3. 将实际图、Montage 自有曲线和通知接入普通 Mantle 宿主，再推进障碍探测、移动目标、motion 生命周期及场景验收。

普通 Demo 尚未完成 Mantle 接入。旧物理稳定性 9/12、Flail 0/3、复杂相机碰撞及最终十分钟预算仍未关闭；头颈拉伸、道具物理和音频维持暂缓。
