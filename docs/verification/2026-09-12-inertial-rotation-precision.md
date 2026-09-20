# 惯性化旋转精度与坐标轴修复（第 116 批）

本批承接完整性路线的第一项：排查第 115 批完整 Overlay 输出未通过的
旋转差异。已修复一个实际坐标轴错误，增加并验证双精度惯性化入口。
完整上游姿势精度链仍未完成，正式 Demo 的最终上身尚未整合。

## 原因与修改

本地 UE `AnimNode_Inertialization.cpp` 用 FQuat 双精度保存前两次输出、
计算相对旋转和轴角。角度/速度及持久化差量轴再转换为 float，应用时
恢复为双精度 FQuat，最后归一化并保存输出。不能把所有步骤一律改为
double，也不能把已经成为 float 的输入重新归一化当作原始 FQuat。

此外，`Quat.h::GetRotationAxis()` 在旋转向量过小时退回原生 X 轴。
项目的旋转转换是 `q = (-UE.X, UE.Y, -UE.Z, UE.W)`，所以这个固定轴
也必须转换为 **−X**。之前运行时直接使用 +X；在小角度/近单位四元数
下会把该分支的修正施加到相反方向。

新增 `AlsQuaternion` 和 `AlsInertialization.EvaluatePrecise`，保持输入、
应用结果及两帧旋转历史的 double 精度，保留原生差量轴/角度/速度的
float 边界。不擅自归一化输入。调用方同时提供准确旋转和对应的单精度
姿势投影，布局、投影或坐标配置不一致时拒绝；活跃历史不能中途改变
精度模式，Reset 后才允许切换。

候选复制包含双精度历史及组件旋转。普通单精度模式不额外复制未使用的
双精度数组；两种模式都在构造时分配缓冲区。热调用无新分配。

惯性化构造参数增加显式 `rotationFallbackAxis`，默认仍是原生坐标的
+X。Godot 的 BaseLayer、Detail、Overlay 及相关回放显式传入 −X。
核心测试保留原生坐标。CopyFrom 不接受坐标轴不同的所有者。

## 验证边界

`OverlayPoseSmoke` 新增 `--overlay-inertial-native-input`。它读取第 115
批已导出的 `beforeInertial` 原始 double 数值，使用真实 Overlay 图更新
得到的惯性化请求，直接验证最终原生输出。隐藏帧、零 delta、零权重、
中断和重新相关性沿用正式轨迹。该模式明确标为
`stage=inertialization_native_input`，不能冒充完整 Godot 姿势图通过。

原始夹具与普通 Editor 重复夹具未修改，SHA256 分别为：

- `3682BAFB9114F1C7604F4D025A7DA75CFDF43625637C5B45F371283A5DC46D22`
- `BA710EC5C1955F64E2623EE8C581B9C202DFA133CBE8EC18C888ED2C95DE1D24`

两者独立消费均通过 886 帧/79 骨惯性化输出对照：位置最大误差
4.986882e-7 m，四元数距离 7.90745e-7，曲线误差 2.3841858e-7。
图仍采样全部 148 来源、记录 366 次请求及 4 个通知，886 次图取消/重试
一致。这里的重试计数指完整图；双精度所有者复制另由核心测试验证。

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| 冷启动夹具，原生输入隔离 | 886 帧通过，退出码 0 | `overlay-inertial-final-native.log` |
| 普通 Editor 夹具，原生输入隔离 | 886 帧通过，相同误差，退出码 0 | `overlay-inertial-final-editor.log` |
| 惯性化核心专项 | 17 项通过；含原生 900 帧的两种精度入口、反射坐标小角度、模式/投影门禁、10,000 次复制/更新/求值零分配 | `overlay-inertial-precision-tests-final.log` |
| Core Locomotion 命名空间 | 525 项通过 | `overlay-inertial-core-regression.log` |
| Release Import 全集 | 1,919 通过、1 既有跳过 | `overlay-inertial-import-regression.log` |
| 正式 Worker 全覆盖 single / parallel | 各 600 帧通过，两模式结果一致；事件 28、lag/stale 0 | `overlay-inertial-production-full-single.log`、`overlay-inertial-production-full-parallel.log` |
| BaseLayer 帧事务 | 3,360 帧通过，晚期失败/重试保持 | `overlay-inertial-base_layer_frame-regression.log` |
| Main Movement / BaseLayer 尾部 | 3,360 帧通过，204 帧惯性化修正、168 帧 Slot、6 次晚期失败重试；独立尾部检查通过 | `overlay-inertial-base-layer-tail.log` |
| Detail 惯性化 | 33,180 骨检查、420 重试、热调用零分配 | `overlay-inertial-inertial_detail-regression-final.log` |
| Detail 状态机 | 82,950 骨检查、1,050 重试、热调用零分配 | `overlay-inertial-detail_machine-regression-final.log` |
| Grounded 缓存 | 1,260 帧、99,540 骨检查、3,249 次姿势求值通过 | `overlay-inertial-grounded_cache-regression-final.log` |
| Godot Debug Optimize=true 构建 | 0 错误、0 警告 | `overlay-inertial-precision-build-final.log` |

Worker 的业务结果摘要仍为 EAAF62E4D0A80A76，root 为
A4F6C26CBAB8A0E7；完整姿势摘要从 3103E3B355BF1F3B 变为
EE519FBE375F4A2B。该变化来自已接入的坐标轴修正，不能声称姿势输出
未改变；也不能仅靠摘要证明视觉效果已达到原版。额外 180 帧短回放
通过，正式覆盖证据使用上述 600 帧结果。

回归发现三个旧场景仍按渲染 Skeleton3D 骨数分配缓冲区，而 Detail
采样器已经输出完整逻辑骨架。首次 `inertial_detail` 因长度不一致在
惯性化之前失败。三个场景现按 `sampler.ReferencePose.Length` 分配，
不截断逻辑骨骼；首次失败日志保留。这是旧验证入口的修复。

本批没有修改 UE C++ 或插件，没有重新导出资产。原生依据为本地引擎
源码和已有冷/普通 Editor 夹具，不能把夹具消费写成重新运行 Editor。

## 仍未通过与下一步

完整 Godot 来源采样和混合后的输出仍未通过：886 帧记录 1,615 个超限
骨骼条目，四元数距离最大 0.000717925（第 115 批为 2,014 个、
0.0017207011）。位置最大 9.597685e-7 m，曲线最大 2.3841858e-7。
完整诊断实际退出码 **1**，日志 `overlay-inertial-final-complete.log`。
容差未放宽。新增双精度入口尚未接入正式上游姿势图，单精度模式仍保留。

双精度原生输入可以通过，而实际图输入仍失败，下一步继续补齐原始
采样、附加基准、局部/网格混合及状态栈到惯性化的旋转精度传递。
具体入口包括 `AlsRawSequencePoseSampler.BlendTransform`、逻辑/虚拟骨骼
展开、`AlsLocalAdditivePose`/`AlsMeshSpaceAdditivePose` 和
`AlsOverlayPoseRuntime` 的混合累加。必须保留原始键及参考姿势的真实
精度、原生归一化位置和权重类型，不能只在最终输出补做一次归一化。
然后完成独立来源时钟、跨图同步/通知事务、正式 Aim/Overlay/
BasePoses/LayerBlending 最终层、最终曲线/脚部约束，再做上身、换髋、
交错步及起步滑步的 UE/Godot 多帧与人工验收。

全 Core 的既有 23 个 P4/P5A 失败未在本批关闭，525 项命名空间通过
不代表整个 Core 通过。既有 p95 2.559 ms 超出 2.5 ms、完整 P5A–P7
及最终十分钟预算均保留；未做新的最终性能验收。音频仍暂缓。
本批未 commit/revert/merge，保留现有用户修改。
