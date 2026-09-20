# Grounded 加法 Slot 的独立 UE 对照

日期：2026-09-13。第一百二十三批。上一批补齐通知与帧所有权，本批继续
原 P5A 前置依赖，验证实际 Slot 混合；完整 Godot ALS 目标保持未完成。

## 对照发现并修正的差异

1. `BlendPosesTogetherIndirect` 只在非加法混合输入超过一个姿势时归一化
   旋转。原 `AlsMontageSlotPose` 总会归一化，改变了单姿势原始关键帧
   的四元数长度；现保留原生单姿势语义。
2. `FTransform<double>::BlendFromIdentityAndAccumulate` 在双精度向量中
   计算 `1 - alpha`。精确加法实现原先提前执行 float 减法，现使用
   `1d - alpha`，保留传入 float 权重并避免再次舍入该补数。
3. 超额权重时，Slot 的姿势数组使用同一个 float 倒数归一化因子，
   对齐当前 UE 优化构建的运算结果。逐项直接除法存在数值差异。
   阈值和 SourceWeight 的计算规则没有改变。

修改位于 `AlsMontageSlotPose.cs` 和 `AlsPrecisePoseBlender.cs`。
没有增加固定换向延迟、手臂偏移或强制脚锁，也没有改已确认的键鼠逻辑。

## 新原生探针的证据范围

`UAlsMontageLifecycleProbe::ExportAdditiveTrace` 调用真实
`FAnimMontageInstance` 更新、`GetSlotWeight` 和 `SlotEvaluatePose`。
使用原 Transition L/R、真实转身序列、79 骨骼、原始数据及重定向，
不保存资产。固定基础姿势来自原转身序列的 0.37 秒；导出完整骨骼和曲线、
各实例样本、冻结求值、命令及播放状态。

15 组、2,080 帧包括：两过渡资产各 30/60/120 Hz，自然结束、连续替换、
同帧多次请求、站立转身共享组中断、零倍率、零混合、反向与自定义淡出。
另外显式包含两类节点边界：把非加法原序列放入 Grounded Slot，以及
`Montage_Play(stopGroup:false)` 制造重叠。这些是测试构造，不能声称
原 ALS 的过渡事件使用这些特殊策略；真实动态过渡仍保留组中断。

两种验证口径分别执行所有帧及同帧取消/重试：

- 使用原生权重与原生样本，单独比较移植 Slot 混合。
- 使用移植运行时的权重及原生样本，比较物理生命周期与组合输出。

每种口径覆盖 264 个多实例重叠帧、141 个超额权重帧、188 个普通/加法
共同相关帧，以及 48 个 Grounded/站立 Slot 并存帧。最大位置误差
`1.5888218580782548e-14` cm，四元数分量差长度
`6.499844741924095e-16`，曲线误差 0。

正式夹具：`tests/Als.Core.Tests/Fixtures/P3/v4_additive_slot_native.json`。
冷启动与普通 Editor 重复导出字节一致，SHA256：
`8DF78E29DBBFCE33FF8B31F3B5DBA2A6C5E5819A5562EF659D4A18CF6001013A`。
新的测试按同半球四元数分量比较，包含长度；原始键值只是近似单位旋转，
不能直接把 `1 - abs(dot)` 当成它们的旋转距离。

该夹具用 UE 提供的实例样本隔离 Slot 混合，不是 Godot 资产采样器或
完整 ALS AnimBP 的最终逐帧验收。实际 Godot 原始来源采样另有既有对照；
最终全图缓存遍历、时钟反馈与完整 Demo 仍需继续。

## 验证与保留的失败

- 相关 Core 111 项通过，包括新原生对照和既有 Montage、精确姿势回归；
  正常 Editor 夹具另通过两种严格口径。位置平方阈值 `1e-12` cm²、
  四元数分量差平方阈值 `1e-20`，两种口径相同。
- Import 相关 57 项通过、1 个既有跳过。本批未重跑全套 Import，未关闭
  上一批两处零分配断言在全量运行中的不稳定问题。
- Godot 优化 Debug 构建 0 警告/错误。Overlay 886 姿势、52 隐藏帧、
  148/148 来源及重试对照通过，原最大误差保持。
- 组合回放 1,260 帧、26 次晚期失败、6 个过渡资产通知帧和每帧重试通过，
  30 个事件、284 帧实际加法播放。改变骨骼的累计观测数为 16,656，
  该数只说明实际消费，不能当作视觉正确性指标。
- 原 Worker 单/并行各 600 帧通过，各 28 个事件，lag/stale=0；
  result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
  root=`A4F6C26CBAB8A0E7`，与上一批一致。
- UE 完整项目构建及三个项目插件审计通过。最终 BuildId
  `69495b43-79df-4b9c-b7e9-cd138757c1fb`，输入 fingerprint
  `9A73FAE64658B708AF6F3DCFF0A6B733D994DAE2607EB9628B4E27F785441C35`。
  隔离插件包 `artifacts/unreal/AlsAdditiveSlotPluginValidation-20260913-123-final`
  构建退出 0，没有部署该包的 DLL；打包后项目审计再次通过。
- DataValidation 退出 0，688 资产，0 error、3 个既有 warning。
  冷启动导出退出 0；普通 Editor 首次数据相同但退出 `0xC0000005`，
  无本次新增的项目崩溃报告。导出脚本改为离开启动脚本、等待 Slate 回调后
  请求退出，第二次退出 0。保留首次日志，不宣称已证明退出异常的根因。
  普通 Editor 启动仍记录两条已有 `AutomationTest: Condition failed`。

首次 14 组夹具缺少超额权重覆盖，并发现单姿势归一化差异；保存在
`artifacts/additive-slot-native-first.json`。随后严格分量比较识别出加法
补数及归一化因子的差异，所有失败 TRX 均保留。没有降低最终误差门槛。
日志前缀为 `artifacts/additive-slot-`，测试结果在 `artifacts/test-results/`。

## 下一步

本批完成新增 Grounded Slot 的独立播放/混合对照。接下来进入最终图
初始化、缓存更新顺序与 Aim/Overlay/BasePoses/LayerBlending 组合；将
真正最终曲线反馈给角色旋转、Foot IK/Foot Lock/pelvis，再做同输入、
同脚相位的多帧和人工验收。默认 Demo 尚未启用最终上身，不能关闭双臂、
侧身、换髋、交错步或起步滑步问题。

P5A 剩余通用动作、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能范围均保留。
既有全 Core 23 项失败和 p95 2.559 ms 超出 2.5 ms 预算未关闭。
音频仍暂缓；没有 commit、revert 或合并。
