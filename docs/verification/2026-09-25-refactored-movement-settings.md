# Refactored 移动设置与全精度曲线

主目录 `D:/GodotALS` / `main`，承接 `716f3fa`。保留用户修改；本批不切换普通 Demo。

## 实现

新增只读 UE `ReadMovementSettings` 与 Python 导出脚本。从原 `AB_Als_C` CDO 读取实际 Settings 绑定，反射导出 General/Grounded/Standing/Crouching 的全部设置字段；引用的 RichCurve 直接读取原 float 键、切线、插值和外推模式，不解析六位小数的 T3D 文本。七个引用去重为五个资源：Forward/Backward 共用 yaw 曲线，Walk/Crouch 共用步幅曲线。

资源独立保存为 `assets/config/refactored_movement_settings.json`，绑定原 animation catalog SHA256。没有重写原 catalog 或其 payload，因此旧原生轨迹依赖不变。设置包含原 walk/run/sprint/crouch 速度 150/350/600/150 cm/s、Pivot 阈值 200、速度混合半衰期 .1 s、Lean 半衰期 .2 s，以及移动平滑阈值 150 cm/s。

Import `AlsRefactoredMovementSettings` 冻结设置和共享曲线，交叉验证 catalog/settings 身份与旧 nativeText 中每个曲线属性的准确引用。拒绝缺失/重复/无关曲线、非有限或非法数值、加权切线、未知插值/外推、缺失或不符的原生采样。

`AlsRefactoredMovementCurve` 复用已有 native float 标量曲线求值；新增原 yaw 所需的 CycleWithOffset 时间重映射，依据本机 UE `RealCurve.cpp::CycleTime` 和 `RichCurve.cpp::RemapTimeValue`。步幅仍使用 Constant 外推。没有把 yaw 曲线裁剪到 [0,1]，也没有把周期边界替换为振荡。

## 验证

- 新增 13 项测试：实际设置/全部采样，以及 12 类资源变异拒绝。连同 Movement traversal 和 weapon resources，最终 33 项通过，无跳过。证据 `artifacts/refactored-movement-settings/settings-final.trx`。
- 五曲线共 1,238 个原生采样（241 个覆盖域内及域外的均匀点，加上每个原键时间），最大绝对误差 `1.1920929e-7`，预设预算 `2e-6` 未放宽。Walk/Run 原切线与六位小数文本确实不同，测试明确防止回退到舍入数据。
- 第一轮 1 失败/12 通过，诊断复跑 1 失败：UE 编译器将采样输入中的 `/200.f` 优化为乘 float 倒数，C# 直接除法导致 sample53 输入 -120.6 与原 -120.600006 不同；同一输入的实际曲线值一致。对齐输入生成后通过，失败 TRX 保留。没有修改曲线数据或放宽比较。
- Godot Optimize 构建 0 警告/0 错误，Python 语法检查和 diff whitespace 检查通过。未跑全量测试或 Godot 场景。

## UE 构建与导出

使用 UE 插件构建技能的完整 Editor-target wrapper，6 actions 成功，四个项目插件 ALS / AlsGodotExporter / AutoTestTools / BlueprintLisp 审计通过。引擎为 `D:/UnrealEngine` 5.9，BuildId `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`；未手工修改 BuildId 或复制二进制。构建状态 fingerprint `0C07299B2350AD1D52B953EF1B84300D441A8553DEEA978F969EB8CC1683CB79`，日志前缀 `20260925T044910976Z-8df913e2039b4506836ccc204082955b`。

冷启动 commandlet 实际 exit0，导出 marker `ALS_MOVEMENT_SETTINGS_OK curves=5 assets_saved=0`。普通 Editor 首次 PID35648 正常日志退出，但独立重新获取 Process 后未能读取 ExitCode，不能当成退出码证明；随后持有原始 Process handle 重跑 PID42244，实际 exit0。cold/editor/editor-verified 及入库 JSON 逐字节一致，SHA256 `1F4A2B9AB66245ABAA52F62E83B77670CA3172441373396E64EA0774F34DD4AF`。

DataValidation 实际 exit0，0 error / 3 旧 warnings（旧 V4 PawnActionsComponent、Navmesh 版本及关联资产加载）。普通 Editor 两条旧 Condition failed 与音频/导航/旧 V4/材质等警告保留，不能称无警告启动。引擎 NoRedist 的旧可选模块 manifest 被跳过的日志也保留；本批导出所需项目模块已通过审计并实际加载。插件为 Editor-only；本批无打包。

## 下一步

现在可以基于全精度数据实现真实 Parent 的 RefreshGroundedMovement、RefreshStandingMovement、RefreshCrouchingMovement、VelocityBlend/Lean 和 InitializeStandingMovement，再加入 UE 原生刷新函数及整条 Movement Details 连续轨迹对照。本批只有资源/采样，不代表这些刷新函数已经执行。

Notify 激活 Pivot、外层65/66、共享角色宿主、普通 Demo 切换、Ragdoll/Get-up/Pose Recovery、Mantle、相机完整验收和十分钟性能预算等旧缺口仍待完成。音频、道具物理、头颈排查继续暂缓。
