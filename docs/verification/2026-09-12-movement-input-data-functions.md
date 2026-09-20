# 真实移动输入：原生数据与公式切片（第八十三批）

本批承接 BaseLayer 统一帧所有者，补其真实输入适配所缺的正式源数据。属于
原 P3 主移动与 P4 Lean，并服务于 P5A 来源/提交统一；不改变原 P3–P7 范围。

## 源数据与发现

原 `v4_main_movement_graph.json` 和输入导出包含变量的图内消费，但没有完整
包含这些变量的计算函数。新增 `-IncludeRuntimeInputs` 模式，只读导出
ALS V4 的 15 个计算/更新图、13 个 CDO 默认值、6 个曲线变量绑定和 5 条实际
CurveFloat。正式文件是 `assets/config/v4_movement_runtime_inputs.json`。

- 蹲伏 `StrideBlend_C_Walk` 实际引用 `StrideBlend_N_Walk`，保留同一实例。
- `LeanInAirAmount` 输入为负的下落速度，原始区间为 [-4000, 0] cm/s。
  不能当成非负动画播放时间，也不能复用地面 Lean 配置。
- `DiagonalScaleAmount` 使用前后 Oscillate；其余四条曲线使用 Constant。
- 动画速度默认值为 150/350/600、蹲伏 150 cm/s；生产配置原本就是
  1.5/3.5/6、蹲伏 1.5 m/s。本批没有修正不存在的生产速度配置错误。
- VelocityBlend 插值速度为 12，地面/空中 Lean 插值速度均为 4。

第一次 Python 审计未收集 CDO 曲线引用，曲线数为零；后续补读取对象引用。
Python/T3D 和 BlueprintLisp 输出仅作审计。T3D 六位小数会截短空中曲线的小
切线值，因此正式曲线使用 C++ 反射 JSON，保留浮点值、插值模式、切线和外推
设置；每条同时导出 201 个 UE `GetFloatValue` 结果。没有保存或修改 UE 资产。

## 实现边界

`AlsMovementInputCurve` 拥有不可变关键帧，复用已有 Core 非循环曲线求值，
支持负数输入和往复外推。导入时逐一校验全部 1005 个 UE 样本，绝对误差
上限 0.000002；截短为六位小数的切线数据被拒绝。

`AlsMovementInputFunctionCompiler` 检查以下三个函数的实际输入连接、函数
归属、局部变量归属、坐标分量和返回/执行链，再构造无状态公式对象：

| 原函数 | 已实现语义 | 还需要的帧输入 |
| --- | --- | --- |
| CalculateCrouchingPlayRate | Speed / AnimatedCrouchSpeed / StrideBlend / MeshScale，限制 0..2；零分母按 UE 返回零 | 当前速度、正确更新后的 StrideBlend、网格竖直缩放 |
| CalculateInAirLeanAmount | 角色局部速度 / 350 cm/s，乘独立空中曲线，输出 LR/FB | 同帧角色局部速度及 FallSpeed |
| InterpLeanAmount | 分别对 LR/FB 使用 UE FInterpTo，小差值直接到目标 | 候选历史、DeltaTimeX、当前地面/空中插值速度 |

Core 输入速度使用 m/s，曲线采样入口转换回原始 cm/s。局部坐标接口为 Godot
的 +X 右、+Y 上、-Z 前；世界速度转角色局部空间属于后续帧适配器。本批没有
用 ActorYaw 的临时值替代同帧角色旋转，也没有编造落地命中或 Mask 曲线。

生产 `AlsMovementGraphDefinition` 在主线程加载这些曲线和三项公式定义；
Worker 共享同一编译定义。**实际帧输入适配尚未接通，Demo 仍使用旧移动入口。**
导出其他 12 个图不表示其执行流程已经完成验证或接线。

## 验证

- 专项 25/25 通过：1005 个原生曲线值、负数域、外推、绑定别名、数据精度
  拒绝、零分母/缩放、空中坐标分量、原生样本对照及算法连接变异拒绝。
  FInterpTo 与除零语义还核对了当前本地 UE KismetMathLibrary/UnrealMathUtility。
- 首次公式构建因项目 `GodotAls.Core.Math` 命名空间遮蔽 `System.Math` 失败；
  已显式限定，最终 Godot 构建零错误、零警告。Python 语法检查通过。
- 生产 single/parallel 各 180 帧通过，均加载 6 个曲线绑定、13 个默认值、
  3 项公式；来源事件各 10 个。结果摘要 `21E164D829153157`，完整姿势摘要
  `CF9225D4DE9B2C8B` 与前批一致。
- 本批没有修改来源事务/最终提交逻辑，没有重复前批所有晚期故障专项。

UE 完整 Editor target、项目插件依赖/二进制审计通过，输入指纹为
`5BEA25B70A07F8C24527F24741074866AF3B8528CC2DE12E0B26D2B197E82E4A`。
原生导出重复两次退出零，SHA256 相同：
`B3A79D928CEDEDA339B79F77D7310C14F2DBC9461B2848C11E43FDEB3035D75A`。
每次导出零错误、一条既有 AI PawnActionsComponent 警告，assets_saved=0。
DataValidation 退出零，零错误、三条既有 AI/导航警告。
独立 BuildPlugin 包 `artifacts/unreal/AlsMovementInputsValidation-20260912`
成功，未将包内 DLL 部署回项目。仓库与项目内 exporter 源码一致。

普通 Editor 经完整 target/审计门禁后用 D3D12 冷启动，执行只读审计导出，
15 图/5 曲线/13 默认值齐全，正常关闭并退出零。日志保留此前已出现的两条
AutomationTest `Condition failed`，以及 AI/导航、默认材质和渲染线程变量
警告；不能描述为普通 Editor 日志零错误。未出现本批插件加载/导出错误。
打包后重启前 wrapper 重建了 NetCore 并重新通过所有项目插件审计，最终
BuildId 为 `061c70bf-0063-442d-85b0-cf70719664e8`，输入指纹未变。

主要日志：`artifacts/ue-movement-runtime-inputs-exact.log`、
`ue-movement-runtime-inputs-repeat.log`、`ue-movement-inputs-validation.log`、
`ue-movement-inputs-editor-restart.log`、`movement-inputs-worker-single.log`、
`movement-inputs-worker-parallel.log`。

## 下一步与未关闭项

继续核对并统一地面速度/加速度、Stride、JumpPlayRate 和更新顺序；输入来自
真实 FrameInput 与上一已提交最终曲线。补真正的主线程胶囊扫掠和可行走面
判定供 LandPrediction 消费，不能拿 FootHit/Floor 的固定值顶替。输入候选
与 BaseLayer 的来源、姿势和通知使用同一提交/取消边界，再接 Worker/Demo。

随后依次闭合最终 YawOffset、动态 Layering/Add/LS、Aim/Lean、脚部/pelvis，
完成起步与左右换向的逐帧视觉验收，再继续完整 P5A 动作链、P5B Overlay/
道具、P5C Mantle/Roll/Root Motion、P6 物理恢复/Camera、P7 人工与十分钟性能。
本批没有新的完整 UE 图逐帧对照、视觉截图、平台脚锁或性能验收；起步滑步、
换髋、上身均仍未关闭。音频暂缓，未提交、回滚或合并工作树。
