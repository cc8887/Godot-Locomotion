# Refactored 原始动画整批输入

发现原有 Refactored 输入只覆盖部分 Head/Base/Mantle，实际移动与 Overlay 资源尚未完整导出。本批扫描原项目 `/ALS/ALS/Animations`、`/ALS/ALS/Character/AnimationInstances` 和 `/ALS/ALS/Data/AnimationInstance`，额外纳入父 AB_Als 和实际 Settings。没有改写或保存 UE 资产，没有导出音频或恢复道具物理任务。

## 导出内容

索引 `assets/config/refactored_animation_sources.json`，180 个独立 payload 位于同名子目录，总 112,524,414 字节。每个文件使用 source 路径的 SHA256 命名，索引保存内容 SHA256；新增 Git attributes 禁止转换换行。

| 类型 | 数量 | 内容 |
|---|---:|---|
| AnimSequence | 127 | 原始键、求值/retarget/root-lock 元数据、完整 float 动画曲线、原文 |
| BlendSpace / BlendSpace1D | 8 / 1 | 原文、样本动画及点坐标 |
| AnimMontage | 9 | 原文和完整自有 float 曲线 |
| AnimBlueprint | 21 | 全资产原文、编译节点和缓存顺序数据，含父、移动、站蹲、Ragdoll、13 个 Overlay |
| AlsAnimationInstanceSettings | 1 | 实际配置原文 |
| CurveFloat | 10 | 原文；本批未增加 standalone RichCurve 全精度键读取接口 |
| ObjectRedirector | 3 | Registry 返回的原始重定向记录，不作为动画编译 |

动画包括 85 个非加法、15 个 local-space additive、27 个 mesh-space additive。所有 BlendSpace 样本与 sequence additive base 引用都能在索引中找到。这里不声称已验证所有蓝图、Notify、道具、音效等引用的完整依赖闭包。

## 可执行导入与原生姿态

`AlsRefactoredAnimationCatalog` 按需读取 payload，核对文件路径、内容 hash、source/class/schema；校验索引计数、重复身份、settings/parent 和 skeleton 身份。不会把整批大文本常驻进运行时。

`CompileAbsolutePose` 复用现有精确 raw/retarget/virtual-bone/root-lock 编译和采样。单次编译返回独立资源作用域，局部 animation ID 为 0，未来进入共享 bank 必须显式分配/映射，不能直接混入 V4 ID。

85 个非加法原始 Sequence 均已编译，逐个以五个时刻对照 UE 原始 `ReadRawAnimationPose`，共 425 姿态、33,575 个骨骼。最大位置差 `1.046694809947456e-13 cm`，处理 q/-q 后最大四元数分量差 `6.661338147750939e-16`，缩放差 0。原始数据不由参考姿态反推。42 个 additive 已完整导出，当前通用入口明确拒绝，尚未实现其全部基础姿态/时间策略；此前 Look 专用支持仍保留。

首轮失败发现八个 AAT_None 资产保留未启用的 additive base 配置。UE `UAnimSequence::IsValidAdditive` 在 AAT_None 返回 false，`GetAnimationPose` 不进入 additive 分支。新增独立 absolute 编译入口允许这些休眠字段，保留导出原文，旧 Mantle/Look 编译政策不放宽。第二轮所有姿态已经通过，仅测试预估“非加法大于 100”错误；核对真实 85/42 后改为精确数量断言。失败 TRX 保留。

新增 6 项测试；最后包含 Mantling/Look/Head native 的 Import 定向 117 项通过，Godot Optimize 构建 0 error / 0 warning。没有全量 Core/Import、Godot 场景或打包验收。

## 导出环境差异

完整 Editor 目标审计通过，0 action，fingerprint `05A9CE0F51DDC7551C7BD4BB9F38FB66FEADD9F8B6FA126D064F1ABA9512FE88`。日志前缀 `20260924T191502561Z-070126fe3a1644b8ae4a1c49f3f61456`。冷导退出 0、无 error/warning；普通 Editor PID 19152 实际退出 0，旧两条 Condition failed 和五项 warning 仍在。无插件源码修改，未新增 DataValidation。

冷索引 SHA256 `7691A4B9BDB654DE48F8E177158060E7E8EE3D67ABCD2769D286FD7E7DBB52DF`；普通 Editor 索引 `6D77EB6BE0E09C99009F2762466AD79F800275DFAFF50E687AB13D4B1B2882FB`。105 个 payload 字节不同：66 个 nativeText 字段、39 个 evaluation 字段；其余字段逐字段相同，包括全部 raw、动画曲线、编译节点、BlendSpace samples 及 425 个姿态参考。

39 个 evaluation 差异仅在 rootLockFirstFrame 的 rotation/scale 辅助值。66 个原文中 45 个仅行顺序变化，21 个蓝图有不同内容行；抽查包含重建函数图的 GraphGuid/PinId/链接 GUID。未将所有原文差异归因于单一原因，亦未定位 rootLockFirstFrame 的内部加载状态原因。两份输出均保留；提交冷启动快照，不归一化或声称字节确定性。普通 Editor 副本和日志在 `artifacts/refactored-animation-sources/editor`。

## 后续

普通 Demo 仍未切换。继续剩余 42 个 additive 的实际策略、真实 BlendSpace/移动/Overlay 图编译与共享资源身份绑定，再接宿主姿态链、视觉、Mantle gameplay。所有既有 Ragdoll/相机/最终性能缺项仍保留，用户暂缓事项不变。
