# Refactored 分层图连接、反馈输入与曲线末端

为接入 Mantle 检查原版实际图与 C++ 后发现，普通宿主仍使用 V4 LayerBlending/UpdateLayerValues；Refactored 的 Layer* 和 ViewBlock 不会因存在于采样结果就自动生效。本批补齐原始图输入、关键连接验证、Refactored 输入模型及曲线末端，尚未切换普通角色完整分层图。

## 原版证据与实现

只读导出 AB_Als、AB_Als_Locomotion、AB_Als_Layering、AB_Als_Head 四个原始 AnimGraph，保存各图原文与 SHA256，未保存 UE 资产。首次完整 Blueprint 导出含生成图重复，最终脚本改为精确导出 authored AnimGraph 对象，文件约 1.46 MB。

真实主链为 Locomotion → PostLocomotion Slot → Layering 的 Locomotion Input → Head 的 View Input → ControlRig → Ragdolling → Root。本批 compiler 验证 Locomotion/Slot/Layering/Head 关键连接、Slot 默认 source update 策略；不是整个主图编译器。

Layering 曲线末端：Overlay 缓存 → ModifyCurve 将六个 Layer*Slot 设置为显式零 → Curves Slot → 与 Locomotion 缓存曲线 Accumulate → 用结果 Override 骨骼混合分支的全部曲线。自定义 CurvesBlend 原生默认 amount=1、mode=Accumulate。新 AlsRefactoredLayeringCurveTail 提供 PrepareOverlay 和 Compose 两阶段；调用方必须在中间实际求值 Curves Slot，接口不假定此 Slot 永远为空。不同名存在性保留，只在骨骼混合分支中存在的曲线被最终 Override 清除。

这解释了 Mantle 负曲线的必要性：例如 Locomotion LayerHead=-1 与 Overlay LayerHead=1 累加为存在的零，禁止提前截断负值。原版 RefreshLayering 直接读取 float 值；两个 ArmMeshSpace 使用 !FAnimWeight::IsFullWeight(LocalSpace)，不是 V4 的 1-Floor(LocalSpace)。新 AlsRefactoredLayeringInputModel 保留这两个规则，按名字绑定、缺失或 absent 为零，拒绝同帧/跨角色/跨 generation 反馈。

模型还计算 RefreshView 的三个曲线因子：ViewAmount=1-clamp(ViewBlock)、Head=ViewAmount*(1-clamp(PoseAiming))、Spine=ViewAmount*clamp(PoseAiming)。没有实现其动作期间角度冻结、RefreshSpine 历史、视角状态机，不能称完整 Refactored View 已完成。冷帧表示第一次 Refresh 后无曲线的结果，不表示 C++ USTRUCT 尚未更新前的构造默认值。

## 验证与局限

- UE 完整 Editor 目标 0 action 构建和插件审计通过，沿用 fingerprint 7AE5DC05C3174F83E4519A0610CB08CAFEF05B06884EB2B33766F58F4DDC282A；日志前缀 20260924T162341586Z-0efe820bb0cf4d7d84fb925130ee06ed。没有插件/配置修改，本批未重新普通 Editor 重启、DataValidation 或打包。
- 两次最终脚本冷导出均 exit 0、0 error/0 warning、四资产。原文件 SHA256 FCE08136C45ECF2ACED06C290469E7DB742A02309D83876B520CB6557C090F9F；复导 SHA256 211B257464CA47A4E981823ACDA7A7D260FB9DCAAF4099CA57BA940AAE24F0A3。三图原文相同，Locomotion 原文仅五个未连接、类型为空、隐藏 ErrorTolerance 引脚的 PinId 不同；移除这五个 GUID 后完全一致。保留原文和复导，不伪称字节一致。
- Core 四项通过：20 项原始分层值含负值/大于1逐属性保留，full-weight 阈值相邻 float、36 组 ViewBlock/PoseAiming、presence、cold/past-owner/retry/非法反馈。
- Import 六项通过：真实图编译、负曲线抵消、六曲线 reset、Curves Slot 后显式输入、最终曲线 override；重算 digest 后改 Slot/模式/reset 仍被拒绝，原文 hash 错误拒绝，非法曲线不部分写输出。
- Import 定向 133 通过/0 失败/0 跳过；随后加强动态绑定拒绝后六项重跑通过。最终 Optimize 构建 0 警告/0 错误。产物在 artifacts/refactored-layering。
- 初次 Core 构建因 Math 被项目 namespace 遮蔽失败，改 System.Math；新增 compiler 首次 regex 字符串编译错误修正；首轮图测试五过一失败，误把对象名字前缀当类名，查原文修为 AlsAnimGraphNode_CurvesBlend，未改变实际图或放宽断言。
- 这些是原文拓扑校验和源码公式测试，不是独立 UE RefreshLayering/View 运行轨迹或完整图姿态 oracle。没有新 Godot 场景、渲染验收或全量 Core 回归。

## 下一步

迁移 Refactored Layering 的骨骼分支、缓存更新和 Head/View 消费，处理其分支过滤器引用的 Refactored 虚拟骨与当前 V4 逻辑布局的差异，再把共享 Mantle bank/完整 curves/typed events 接入真实角色图。不能给现有 V4 图简单换曲线名，也不能只把 PostLocomotion 强行插到最终 IK 后。之后继续障碍探测、移动基座和 Mantling motion 生命周期。

全部旧未完成项保留：物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生对照、旧移动 oracle 闭包、完整视觉及十分钟性能预算。头颈、道具物理、音频暂缓；脚步粒子/贴花未执行。用户 plan、project.godot、诊断文件和外部 uid 不纳入提交。
