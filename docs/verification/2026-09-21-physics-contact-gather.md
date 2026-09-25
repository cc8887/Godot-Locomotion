# 接触几何 Gather 与初始重叠状态

在 `${env:GODOT_ALS_ROOT}` / `main` 继续前批接触行，新增运行时代码 `AlsContactGather`。
现在能够从原始 shape-local 接触点、局部法向、锚点、世界形状姿态、COM 和速度，
直接生成接触求解输入；不需要加载原生已经 Gather 的接触臂、切线或恢复目标来运行。

## 原生语义与实现

对照本地 UE 5.9 的 `PBDCollisionContainerSolver.cpp` 中
`UpdateCollisionSolverContactPointFromConstraint`，以及 `Vector.h::Normalize`：

- 世界点保持 double 精度；用两端逆质量加权得到公共接触点，再减 COM 转为 float 接触臂。
- 法向从 shape 1 旋转到世界。几何切线先叉乘世界 Y 轴，退化时改 X 轴；
  Normalize 的 1e-4 门槛针对平方长度，而不是长度。
- 没有持续锚点、有恢复系数或属于新接触时，计算包含角速度的相对接触速度；
  非退化滑动方向替换第一切线。持续锚点接触在不需要速度时保留几何切线。
- 新接触摩擦位移由速度乘 dt 估计；已有锚点使用两个世界锚点差，再叠加接触点误差。
- 恢复门槛为 threshold × dt，严格小于负门槛才产生恢复目标速度。
- 初始重叠支持新点残余深度、既有点初始深度、流形共享最小深度以及逐点模式；
  按初始去穿透速度更新 allowance，再限制单步推离，最后减 target phi。
- 禁用位置/速度/摩擦标志随输入传递，不修改求解行的既有语义。

`AlsCachedContactManifold.GatherGeometry` 在 scratch 中生成所有求解行和更新后的 InitialPhi，
完整成功才交换缓存；失败保持原有流形与状态。每步累计量仍归该流形独立所有。
InitialPhi 通过 `InitialPhiAt` 供 owner 读取，owner 应在完整物理步提交成功后再保存为持久状态。
接触与关节的解析耦合测试现在使用此几何入口，验证 Gather 输出确实进入共享迭代。

仍使用原批准的 C# Core 架构，UE 仅离线导出，不改引擎，不增加 UE 运行时依赖。
本批没有实现 shape/body 身份分配、窄相碰撞、跨帧锚点匹配或真实世界 provider。
支持从持久状态求解，不等于持久状态的完整管理器已经实现。
采用二维摩擦、积分后速度恢复、初始去穿透开启的原生默认配置；不支持 split impulse
或实验性的积分前速度恢复，也不在此处处理物理材料组合规则。

## 原生数据与逐阶段验证

新增 `PhysicsContactGatherOutput=<新绝对路径>`，schema 2：
`assets/config/v4_physics_contact_gather_reference.json`，**432 组，9,395,790 字节**。
包括原始 geometry/settings、Gather 结果、八轮位置、隐式速度和两轮速度阶段。

覆盖 30/60/120 Hz × 双动态/任一端 kinematic × 1/4 点 × 12 场景 × 两种姿态。
旋转变体使用不同的两端 shape 旋转/偏移与约 `(100000,-200000,300000)` cm 世界原点。
除原有无摩擦/锚点/恢复/分离/禁用行/推离限制外，增加 Y 轴法向退化、target phi、
初始点与既有初始深度；动态—kinematic 还覆盖原生共享 InitialPhi 模式。
这是合成流形的原生容器观测，不包含窄相检测或真实世界落地。

两次冷导出退出 0，`ALS_PHYSICS_CONTACT_GATHER_OK cases=432 assets_saved=0`，字节一致。
SHA256：`CCE586123F1AA6C9CED2349A7234CD70FC1254469CBFE357D0BBC213A8343EAD`。
旧 288 组出口也重新执行，结果与旧正式文件 SHA256
`4A1E8229395320399CA6441DF09FABFD564E30BDBE37EFF43630161E5DC03E1E` 一致。
新出口独立建档，没有替换旧参考或重排原始资产。

Import 测试从原始 geometry 生成 Core 输入，再送入 Core 行求解，不将 native Gather 输出
作为新模式的运行输入。432 组所有阶段通过，保持前批各项容差：

| 比较量 | 最大差值 |
| --- | ---: |
| Gather 接触臂/基/误差/恢复目标/InitialPhi | 0 |
| 有效质量 | 0 |
| DP，cm | 4.4858435e-8 |
| DQ，rad | 6.1776952e-9 |
| 线速度，cm/s | 1.221299e-5 |
| 角速度，rad/s | 1.6900834e-6 |
| 累计推离 | 1.0861345e-7 |
| 累计冲量 | 3.4898e-5 |
| 摩擦比例 | 1.193583e-5 |

零差值是当前 432 组、当前平台观测，不声明全部输入或完整物理轨迹逐位相同。
旧 288 组数值结果未变。

Core 新增 8 项（含三种频率参数）语义测试：逆质量加权与大世界平移、切线门槛与锚点、
恢复门槛、初始重叠生命周期、目标深度顺序、非法输入、失败缓存与 2048 次 Gather 零分配。
初始聚焦回归 17 项通过；Godot 优化构建通过，0 warning / 0 error。

## 构建与故障记录

按 `ue-diagnosing-plugin-build-load` 完整构建 Editor 目标并审计插件闭包。
UE 5.9，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`，最终输入 fingerprint
`4C9202329BA83D2D2FC7FFDF527B5EBE2EE9B022464DD702AB9F5309ADD52365`。
构建日志前缀：UE `Saved/Logs/PluginBuild/20260920T205332042Z-c3abb78e046c4071a4756ef874dffced-*`。
主仓库与 UE 本地插件源码同步修改，无 DLL 拷贝或引擎 fork。
技能引用的两项 superpowers 技能不可用，以直接源码、构建审计和实测作为依据。

首次 UE 编译误用 `SetObjectState`；本地粒子 handle 提供 `SetObjectStateLowLevel`，
已修正合成探针后重新全量构建/审计通过。未将低层状态设置用于真实 evolution 管理。
首次失败日志前缀 `20260920T205047802Z-e7ccc0e0b5eb4196aaf0b4281fd48b4a-*` 保留。

Core 首次全量 2654 通过、1 失败：既有
`AlsFootSupportGeometryTests.RuntimeSkinningAllocatesNoManagedMemory` 报告 2448 字节。
检查该热路径未发现新增分配表达式，本批未修改它或放宽断言；独立三项足部测试复跑通过。
具体分配来源尚未定位，不能归因于 JIT 或仅凭复跑抹去首轮失败。

固定 JIT 配置下完整复跑 **Core 2655 通过**，使用既有 P5A Golden/TraceSchema 过滤；
**Import 2358 通过、1 既有条件跳过**（需普通 Editor LayerBlending 重复导出环境变量）。
全量运行与聚焦测试均未增加排除项或改动零分配门槛。

普通 Editor 冷重启输出 `ALS_CONTACT_GATHER_EDITOR_RESTART_OK` 并退出 0。
仍有既有 UnifiedErrorTest/Condition failed 和引擎材质/资源提示，不称无错误日志启动。
DataValidation 退出 0，0 error / 3 旧 warning（AI 组件与导航版本）；
本批仅改 Editor 导出工具，无适用的仓库打包要求，未执行打包。

完整产物目录 `artifacts/physics-contact-gather-20260921/`，保留首轮失败、重复导出、TRX 和构建日志。

## 下一步

稳定 body/shape 身份、持续流形匹配与摩擦锚点更新，再接真实碰撞世界查询。
随后补重力/外力、运动 kinematic、动态双向响应、睡眠/唤醒和整链落地验收。
普通角色 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能预算继续保留。
本批未修改普通 demo，未关闭旧 Jolt 落地失败，也未做画面观感验收。
用户 P4 规划保持原字节与未提交状态；所有交付集中在主目录 main。
