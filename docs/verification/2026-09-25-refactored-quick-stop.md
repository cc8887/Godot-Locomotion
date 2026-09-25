# 原始 StopQuick 通知、参数与播放

后续原生对照纠正了本文的世界角度转换顺序和176度速率预期，见 `2026-09-25-refactored-quick-stop-native.md`；以该原生验证和修正后的实现为准。

新增只读 `export_refactored_quick_stop_settings.py`，从原 Parent CDO 的 AIS_Als_Default 读取全精度 QuickStop 参数和站立/蹲伏左右四个 Transition 引用。没有保存 UE 资产。

实际参数：blend-in 为原 float0.1、blend-out 为原 float0.2、start为原 float0.3、rate范围1.75..3。保存至 `assets/config/refactored_quick_stop_settings.json`，绑定既有 catalog SHA。

`AlsRefactoredQuickStop` 校验设置来源、catalog、原 Transitions 字段绑定、四序列的 Skeleton/additive/RootMotion/RateScale、原 EventGraph StopQuick→PlayQuickStopAnimation→GetParent 连线和通知身份。显式宿主 ID 允许与已有武器 Transition 资源共用身份。

选择逻辑保留原操作顺序：非VelocityDirection固定左侧及最小速率；否则先在double域计算 input或target world yaw减actor yaw，再转float并UnwindDegrees，然后将大于175的角度减360，最后按绝对角度/180进行不钳制的Lerp。因此176度映射为-184，结果略高于rate范围上限，这是原算法而非异常修正。Crouching选择蹲伏资源，其他情况选择站立。

Dispatch读取真实Standing机器的TransitionStarted/edge4/notify1，并在主线程候选阶段经共用queue.PlayImmediate播放，无StandingIdleOnly门控。复用既有Transition mesh additive sampler。宿主负责在worker PostUpdate后按通知顺序调用，并统一提交/取消所有候选。

## UE 证据

- 全Editor wrapper前缀 `20260925T075550264Z-6b67eca8b79141599b03bbd59fead590`，0actions、实际退出0；ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp全部审计通过。
- BuildId `f7c75dee-59c9-476c-85d9-645c917dd66f`，fingerprint `FA68F4872E1F3257ABA5208517AA41A339139838F2C35AE3476C635B880EEB72`。
- 冷导出PID41988，普通Editor PID34720，均保留原Process对象取得实际ExitCode0。日志 `refactored-quick-stop-cold.log` / `refactored-quick-stop-normal.log` 均有成功标记。
- 两份797字节导出 SHA256 都为 `7537FE7EF22CD8049443AA859B765D32ACB5F346D228062FEEB0A36FC8353AB2`。
- 普通Editor仍有两条既有Condition failed及PawnActionsComponent/Navmesh/LineSet材质/MotionVector/Crowd警告，本批未修复。无插件C++/配置改动，没有新DataValidation或打包。

## 本地验证

- 首轮13项：12通过、1失败。176度期望字面量为3.0277777，实际原float运算为3.0277779；修正测试期望，不改变生产运算或容差。
- 四组实际机器通知→共享Montage→Slot，60Hz共480帧、79骨/曲线逐帧取消重试、四资源都产生变化并最终回基底。蹲伏动画在受控Standing基底上验证，不能代表完整Crouching图通过。
- 角度边界0/±90/175/176/±180/±540、Input/Target切换、非Velocity模式、double减法后转float、非法数值拒绝均覆盖。
- TRX位于 `artifacts/tests/quick-stop/`，保留首次失败证据。Godot Optimize构建0警告0错误。
- 最终 `quick-stop-related.trx`：QuickStop/Transition/RestMontage相关76项通过，0失败；diff whitespace检查通过。

## 剩余范围

本批取得真实资产参数并接入真实资源播放，但未新增UE连续QuickStop函数/整图oracle，不能宣称原生逐帧1:1。完整角色宿主仍需采用这些模块；Crouching整图、普通Demo、Godot视觉和十分钟性能仍待。Ragdoll/Get-up等全部原目标继续保留，音频/道具物理/头颈专项按用户要求暂缓，用户现有修改未纳入本批。
