# Refactored 基座空间脚锁状态组件

第一百五十九批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。
承接完整性修复计划中的 P4 平台脚部工作；本批实现纯状态组件，尚未切换
生产 Foot IK。未提交、合并或回滚已有修改。

## 来源与版本边界

本地 `D:/AdvancedLocomotionSystemV/Plugins/ALS/Source/ALS/Private/AlsAnimationInstance.cpp`
的 ProcessFootLockTeleport、ProcessFootLockBaseChange、RefreshFootLock、
ConstrainFootLock 为规则来源；默认设置来自 Public/Settings/AlsFootLockSettings.h。
AngleBetweenSignedXY 来自 Public/Utility/AlsVector.h，TwistAngle 与动画
权重门槛另核对本地 UE 源码。本批没有修改或重新构建 UE 插件，也未执行
新的 UE 数值导出；源码移植与独立几何测试不能称为原生逐帧数值配对通过。

当前动画资产/直接 AnimBP 对照是 V4，Refactored 是另一版本。V4 的
SetFootLocking 在满权重时逐帧重新捕获；Refactored 仅在进入满权重时
捕获，并保存世界/组件/基座锚点和锁定后的、尚未应用 IK 的 Final 姿态。
因此新增独立模型，不能悄悄修改已通过原 V4 配对的模型并称二者等价。

## 本批实现

`src/Als.Core/Locomotion/AlsBasedFootLockModel.cs`：

- 世界锚点用双精度，组件/基座/Final 边界显式转单精度，使用 UE 轴向和厘米。
- 基座以完整 ulong 身份区分，零表示无相对基座；基座变换不应用缩放。
- 满权重保持锚点；部分权重只允许下降；重新满权重读取上一帧 Final，
  并保留原版旧权重高于 0.9 时的世界锚点规则。
- 平台变化保持世界锚点并重新计算基座局部值；平台移除清空局部锚点。
- 显式传送后的 0.2 秒窗口从组件锚点重建世界/基座值，不使用猜测的位移阈值。
- MovingSmooth 释放速度 5/s，非地面释放速度 0.6/s；首次有效时正在移动
  或处于空中则不锁；禁用 IK/锁定设置时清空锁定历史。
- 使用实际骨盆旋转与参考大腿轴约束位置，默认大腿 90°、脚掌 40°。
  大腿约束更新世界和基座锚点；脚掌 twist 限制按源码只修改世界输出旋转。
- Evaluate 返回值候选，输入/状态不含托管引用或 Godot 对象；不在模型内部
  提交。相同已提交历史和输入可重算，不影响另一角色或已提交状态。

适配契约要求有限值、有效旋转、单位大腿参考轴和非退化正组件缩放。
MovingSmooth、骨骼有效性及传送时间由上层提供，模型不猜测来源。

## 验证

新增 `tests/Als.Core.Tests/AlsBasedFootLockModelTests.cs`，Release 22 项通过：

- 30/60/120 Hz 各六秒解析旋转/平移轨迹，动画目标持续扰动，基座局部锚点
  保持，世界位置与独立三角函数计算一致；每帧重复候选计算一致。
- 部分上升不锁、重新满权重读取前一 Final、接近满权重保持世界锚点。
- 三档帧率分别验证移动和空中衰减、释放以及冷启动不锁。
- 换平台/平台移除、传送窗口端点及连续修正、窗口外保持世界锚点。
- 正负旋转限制、骨盆参考方向、限制后的锚点历史、极小水平向量、组件
  与基座缩放差异、有效性恢复、禁用和非法输入。

结果 `artifacts/tests/based-foot-lock-159.trx`。
`dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` 成功，
0 warning / 0 error。未改现有生产调用和帧契约，因此未重复运行平台图、
静态地面姿势回放或截图；上一批约 12.89 cm 漂移仍是开放缺陷。

## 下一步接入所需数据

1. Gather 发布有效骨盆组件旋转、按真实参考骨骼层级求出的大腿轴、实际
   支撑基座身份与无缩放变换，以及显式传送/有效性历史。维持 Main 查询、
   Worker 值类型输入的边界；不能把 Godot 基座对象放入跨线程数据。
2. 区分动画目标、脚锁后的 Final、应用 IK/pelvis 后最终骨骼。原 Gather
   读取最后一种，而 Refactored 重新锁定依赖第二种；必须由运行时独立
   保留，不能将最终骨骼再当作 IK 前历史回灌。
3. 将两脚候选锚点、Final、基座/传送历史并入完整根的提交与取消；普通
   分支未访问时仍处理全局状态，失败后重试不能推进历史。
4. 接适配输出到现有 V4 控制链，明确消费锁定锚点及 Alpha 的位置，避免
   把已混合的 Final 再按 Alpha 混合一次。完成 FBX/世界轴向配对后再跑
   实际旋转平台、离台、切换、传送和同帧恢复，以及动态接触窗口渲染。

只有接入与实际场景验收完成，才能将此项标为 Demo 修复。完整默认根
切换、P3/P4 效果验收、P5A 剩余通用动作至 P7 仍未完成，音频暂缓。
