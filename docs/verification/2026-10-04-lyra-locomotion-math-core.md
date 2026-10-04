# Lyra 通用移动数学 Core 复用

2026-10-04。按 ROADMAP 当前目标迁移 Lyra locomotion 的核心思想和运动算法，武器覆盖手枪／步枪，Unarmed 保留回退；本批不扩展 URO 或精确 UE 调度要求。

## 实现边界

现有 ALS 的标量插值内核归入 `AlsMath.InterpolateTo`，ALS `InterpolateLean` 和 Lyra Provider 的 Aim／HipFire 权重实际调用同一内核。中间运算保留 double，由调用点执行 float pin 转换；非正速度、近目标吸附及 alpha 钳制保留。

在现有 `AlsPrecisePose` 增加 `BlendWith`，承接左手 Layer 的局部骨混合。差值形式的 position／scale 插值、四元数半球选择与归一化保留。已有 `BlendTransform` 的加权求和次序不改；二者是同一姿态类型上的不同运算，没有另建姿态框架。

在现有 `AlsDoubleVector` 增加带平方容差的 `SafeNormal`。ALS FootPlacement 和 Lyra Main Observation 共用该实现，脚部默认使用原 1e-8f，Main 观察显式使用原 1e-4f；方向计算的水平向量仍使用原默认容差。

在既有 `AlsCharacterRotationMath` 扩展 ClampAxis／ClampAngle、旋转矩阵轴、RotateVector／UnrotateVector 和 CalculateDirection。Main 复用原 Normalize，保留 float 角度输入边界；ALS 移动平台速度历史与 Lyra 世界／局部向量转换共同调用矩阵运算。角度单位、+180 半圈约定、负零及原 promoted float DEG_TO_RAD 常量保留。

Lyra 的权重目标、站蹲 RootYaw 限制、cardinal dead zone、wall 阈值及更新顺序继续由其图策略定义。Godot 仍负责资源、输入、物理采集和模型发布。这批通用运算全部位于纯 .NET Core。

## 验证

Debug／ExportRelease 构建均 0 错误、0 警告。Release 相关 Core 测试 197 通过、0 失败、0 跳过，其中新增 31 项覆盖插值吸附／非正速度／双精度权重、环绕区间 ClampAngle、半圈和负零、旋转轴正交及向量往返、四方向、不同 SafeNormal 容差、BlendWith 端点／半球／亚 float 位移和错误参数。关联回归包含现有 ALS 相机、角色旋转、瞄准、平台历史、脚部及精确姿态。

运行期间 13 份实施／验证源冻结。Debug 14 个（含渲染）和实际 Optimize 13 个 Godot 进程均退出 0，无 ERROR/WARNING：

| 范围 | 每构建结果与证明边界 |
| --- | --- |
| Main Observation／Update | 各 2520 帧，观察 15120 阶段；scalar／flags／spring 精确，最大向量差 1.36425e-12 cm；包含取消重试及 update-only，受控原数据夹具 |
| Aim 权重 | 11340 帧、10866 pin 组、474 隐藏更新，double 权重和暴露 pin 精确，三 Provider |
| LeftHand | 3780 帧、3078 pose、1401 应用、513 update-only、189 隐藏；完整通道与 retry，位置最大差 7.11e-15 cm |
| Main／Aiming／Slot | 实际组合算子和完整通道回归，原门槛保持 |
| Main 最终 Rig | 三频／三 Provider 7560 帧；这里使用解析碰撞夹具，不作为真实复杂地形证明 |
| 两武器组件 | 现有 Pistol／Rifle 原资源及发布路径专项回归 |
| ALS 普通／Aim／Grounded | 当前普通入口 60Hz／1700 帧、3 Pivot／4 dynamic；原瞄准及 Grounded 关联回归 |
| Lyra 普通十角色 | 每构建 4800 角色发布、实际 Godot 移动、最终 Rig、换装及武器通知；Debug／Optimize 完整报告与前批相同 |

七张 FramePostDraw GPU 截图已查看联系表；站立、移动、手枪瞄准、步枪蹲姿、跳跃、落地及反向动作可见。仍为平地远景，人物材质偏白；近景握持和复杂地形没有在这批关闭。

Optimize 的六份 Debug DLL／PDB 按 SHA256 恢复。独立审计通过：13 冻结源、4619 其它当前 dirty 基线、870 导出 JSON、710 原 UE 包、9 配置及所引用安装引擎源码保持。证据前缀 `artifacts/lyra-analysis/locomotion-math-core-v1-*`，最终结果为 `locomotion-math-core-v1-audit.json`。

审计辅助脚本最初将新增测试数写为 32，读取实际 TRX 后修为 31；没有测试失败、算法修改或门槛放宽。运行前的初版 source freeze 保留为 `locomotion-math-core-v1-prefinal-frozen.json`，最终 freeze 在运行前重新生成。运行期间源码未改。

## 继续推进的实际入口

剩余明确的通用入口是 `LyraFootPlantRigMath` 的 Aim、轴角、Euler、Inverse 和 IK。Core 已有 `AlsTwoBoneIk`、`AlsRigTwoBoneIk`，应先共用现有解算，再扩展缺失的通用 Aim／Euler；原 FootPlant 图、骨层级与控制配置保持在 Lyra 适配层。`LyraFootPlantRigExecutor` 的纯数学 unit 同样需对齐 Core；碰撞查询保持 Godot 适配。

旧资源预览路径 `LyraHipFirePoseLayer.InterpTo` 和 `LyraRootYawOffset` 也仍有数学实现，需核对不同精度／容差后接共用入口。本批关闭的是当前生产 Main 的上述数学迁移，未声称通用能力已全部整理完成。

之后继续普通角色的台阶／坡面和近景握持、站蹲瞄准／连续起停／Pivot及真实键盘验收。目标仍 active，URO／精确 UE 调度与额外 Provider 继续后移。未启动或修改 UE、未保存资产／重导、未提交或推送；用户未提交修改保留。
