# 原生瞄准输入与角色帧历史（第一百零七批）

日期：2026-09-12。工作区：`../GodotALS-p5a-events-actions`。
分支：`feature/p5a-events-actions`，HEAD 仍为 `d6b45e3`；保留现有修改，未提交或回退。

## 本批解决的完整性缺口

原 ALS V4 的 UpdateAimingValues 是动画蓝图全局更新的一部分，不应随移动
姿势来源的相关性停止。上身 Aim 图需要它的原始/平滑角度、脊柱旋转以及
AimSweepTime、InputYawOffsetTime、Left/Right/ForwardYawTime 历史。
只迁移原公式或读取当前镜头角度，会遗漏旋转模式分支的保持行为、插值历史
以及源图实际的数值转换。这属于原 P4 上身链路的前置补完。

`AlsAimingInputCompiler` 同时检查原 layering 导出和新增原生导出的真实连接：
五个执行分支、模式枚举、输入门控、参数/初值、旋转来源、映射端点、数值
精度及 UpdateCharacterInfo → UpdateAimingValues → UpdateLayerValues →
UpdateFootIK 顺序。数据来源必须是实际 ALS_AnimBP，不能用复刻公式作对照。

`AlsAimingInputModel` 保留 UE Rotator 的 double 存储、RInterpTo 的 float
时间/速率和整组三轴 near-zero 判断；NormalizedDeltaRotator 的拆分引脚
通过 BreakRotator 输出 float，再进入 double Vector2D/映射运算。脊柱的
MakeRotator 输入也为 float。FInterpTo 的这组蓝图引脚则是 double。
依据本机引擎 KismetMathLibrary.h 的 MakeRotator/BreakRotator 声明及真实
Blueprint 输出修正，未通过放宽比较门限掩盖初次失败。

原生脚本的 Rotator 构造同样经过 MakeRotator 的 float 参数。边界夹具记录
实际构造值，而非 Python 构造前的 double 意图；这是修正对照输入，不是
修改 UE 输出。冷导出前两次的失败和首次数值测试失败日志均保留。

## 已接入的位置与边界

`AlsMovementGraphDefinition` 在主线程编译并共享只读模型；每个
`AlsBaseLayerFrameRuntime` 独占候选/已提交瞄准历史。正式 PrepareFromFrame
捕获角色朝向、瞄准角度、世界输入和实际旋转模式，在移动相关性判断前更新。
Godot yaw 到 UE yaw 取反，输入世界 XY 使用 (-Godot.Z, Godot.X)。

提交随姿势一起发生；取消、晚期 Slot 失败和重试不提前推进历史。隐藏来源
时仍运行全局瞄准更新；速度朝向模式保留 AimSweep/Spine，其余模式或无移动
输入时保留 InputYawOffsetTime。标量组件测试入口不伪造角色输入。

**完整 AimOffsetBehaviors/OverlayLayer 姿势消费者尚未接入。** 本批接通正式
角色输入及历史，并不改变最终姿势；不据此宣称双臂、侧身、换髋或滑步完成。

## 验证

- `aiming-input-tests-2.log`：11 项 Import 测试通过，包括实际 Blueprint 的
  30/60/120 Hz 共 1,050 帧连续历史和 36 个原生插值边界。逐项绝对误差门限
  保持 1e-8，验证模式保持、重试、过期/外来/回收角色历史及源图变更拒绝。
- `aiming-input-core-tests.log`：4 项 Core 测试通过，覆盖 Godot/UE 轴向、
  整组三轴插值、10 个独立角色 single/parallel 位值一致、10,000 次热更新
  托管分配 0 字节。该结果不代替 P7 十分钟预算。
- `aiming-input-build-final.log`：Godot C# 构建通过，0 警告、0 错误。
- `aiming-input-frame-final.log`：3,360 帧，302 个隐藏帧，12 次晚期故障，
  24 个保护性拒绝，瞄准/姿势一同提交，取消及重试一致；同时重新编译普通
  Editor 导出的连接并核对全部原生数值。
- `aiming-input-production-single.log` / `aiming-input-production-parallel.log`：
  实际 Worker 各 600 帧通过。结果 EAAF62E4D0A80A76，完整姿势
  3103E3B355BF1F3B，根运动 A4F6C26CBAB8A0E7；事件 28，lag/stale 0。
  与第 106 批一致符合本批尚未连接新上身姿势消费者的边界。

## UE 原生对照与加载

采用 UE 插件构建诊断技能的整项目 Editor 构建/审计与冷/正常启动流程。
`aiming-input-editor-build.log` 退出 0，三个项目插件审计通过，构建指纹
4A59B8E7BBDD073014F8D1B21052E6E6928EBFEEA1CDAC9606623C5E55FC569F。
本批未改 UE 原生插件或配置，无需重复上批的 DataValidation/插件打包。

`export_aiming_inputs.py` 创建临时角色和 AnimBP 实例，通过 UE 反射的类型
化运行时 setter 赋值并逐次回读确认，再直接调用 UpdateAimingValues。
不编辑 CDO、不保存资产，完成后销毁创建的临时角色。

冷启动 `aiming-input-native-cold-3.log` 和普通 Editor `aiming-input-editor.log`
实际退出 0，都生成 1,050 帧/36 边界。两份 JSON 的初值、设置和全部数值
完全一致；UpdateAimingValues 原生文本含不同的非消费节点导出内容，不能
声称文件字节相同，正式编译器分别检查所消费的连接。

冷导出 SHA256：77EC17D15A7AE51DE591ACB48EBD3A43B77A61068D240F2A523C31A5714A7B4A。
Editor SHA256：39E19C3A54DDFF78F827DDCE228E521EA3FC1F7983872C2B9BF7A80938F99F95。
普通 Editor 仍有既有 AutomationTest 的两条 Condition failed，以及老资产
AI 组件、导航和材质警告；不把退出 0 描述成全项目无警告。瞄准导出无
Blueprint 访问错误，原生数值一致。相关 UE/Godot 验证进程均正常结束。

## 下一步

1. 按实际 AnimBP 补齐 AimOffsetBehaviors 和 OverlayLayer 的来源、独立时间、
   状态/缓存和骨骼空间，与现有 BasePoses/LayerBlending 组件连接成最终输出。
2. 把真正最终曲线反馈给 Layering、朝向和脚部消费者；保留 presence 及上一
   已提交帧语义。再完成 Foot IK/Lock/pelvis/平台的整链核对。
3. 同输入、同帧率、多脚相位对照 UE 与 Godot 的上身、支撑脚和换髋时序，
   加多帧截图及人工验收。原版换向条件门控不改成统一时间延迟。
4. P3/P4 表现闭合后继续 P5A 通用动作、P5B 全 Overlay/道具玩法、P5C
   Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera、P7 十分钟预算；音频暂缓。
