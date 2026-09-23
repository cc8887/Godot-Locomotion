# Flail 接触删除后的约束岛重建

本批在 `D:/GodotALS` 的 `main` 修复一处约束图生命周期遗漏。修复后，两套模型在同初态、同 Flail 驱动的 60 Hz 全 600 帧、120 Hz 全 1200 帧，身体位置、旋转、线速度、角速度均与独立 UE 世界逐值一致。比较恢复 JSON 的 float 存储精度；没有增加误差容限。这不是普通角色 Ragdoll 已完成，也不表示休眠预算已经通过。

## 根因与实现

本地 UE `Chaos/PBDCollisionConstraints.cpp:653` 先按旧 island container 顺序收集失效约束，再删除。`Chaos/Island/IslandManager.cpp:2137` 的 `ProcessIslandSplits` 在删除后沿持久化的每节点邻接数组重建容器，即使关节链仍然连通。节点邻接数组删除采用 swap removal，随后 `AssignIslandLevels` 为某刚体遇到的第一条固定端接触赋 level 1，其他同体接触可能成为 level 2。

旧 Core 只在全局容器中删除约束，没有保留节点邻接顺序并重建。AnimMan 第 64 帧移除 head→spine_03 的两条接触后，UE 选择 spine_03 的 shape 1 作为第一条地面支持，Core 仍选择 shape 0。该帧身体输出尚同，第 65 帧开始分歧。

`AlsContactConstraintOrder` 现在预分配并事务维护节点邻接表，按旧容器顺序删除并更新端点数组；存在删除时沿动态节点深度优先重建接触／关节容器，再进行层级排序。Commit 发布邻接状态，Abort 保留旧状态，Reset 清除历史。不引入逐步堆分配。当前仍是固定身体／关节拓扑的受控后端，不能据此宣称实现了 Chaos 通用世界的 island 创建、合并、部分睡眠或动态关节拓扑。

另修诊断捕获：`AlsIslandStepCapture` 从本帧候选 joints 写入实际 drive target 和启用轴的 K/C。此前直接输出静态资产参数，动画驱动的单步重放会使用错误输入。未启用的参数仍为资产元数据，不声称这些未参与求解的字段等于 UE 当帧原始字段。

## 定位证据

产物目录：`artifacts/flail-first-contact-20260923/`。

- `steps.json`：第 65 帧两模型 postIntegrate/preSolve 的初始及预测 COM、旋转、速度、质量、惯量全部一致。
- `contacts.json`：37 对、93 点的 shape world、局部点、摩擦锚点和标志一致；近零局部法线有最大 3.33e-16 差异，未据此宣称所有几何位模式相同。
- `gather.json`：相同 Core 原始接触输入交给 UE Gather，93 点求解输入分量全部一致。
- `coupled.json`、`stages.log`：修正捕获后，两模型共 48 个阶段的 DP/DQ/V/W 与 UE 独立容器重放全部一致。
- `order-window.json`：61–63 帧次序相同，AnimMan 64–65 帧各四条次序不同；`order-final.json` 修复后十个采样帧均相同。
- `comparison-final.log`：60 Hz 两模型各 600 帧 P/Q/V/W 差异为零；AnimMan 两端均第 220 帧休眠，Mannequin 两端均十秒未休眠。
- `comparison120.log`：120 Hz 两模型各 1200 帧 P/Q/V/W 差异为零；AnimMan 两端第 1087 帧、Mannequin 两端第 476 帧休眠。
- `comparison30.log`：30 Hz Mannequin 全 300 帧身体状态相同；AnimMan 第 20 帧首差 P=1.3061421345383906e-5 cm、Q=8.344650268554688e-7、V=0.0005950927734375 cm/s、W=1.621246337890625e-5 rad/s，仍待定位。

独立 UE 输出 SHA256：

| 文件 | SHA256 |
| --- | --- |
| native.json（60 Hz，含第 65 帧观测） | 7C9A9C050E3819691BAB3777DCB29B830F5BF886E55A3E47D7F1949C90C13C23 |
| native120.json | F3445589E862F28063B97A16A1C85B4C61170136854122B965B0D0159EDB0EC7 |
| native30.json | C97DC7E5E448799453F07E727C64FF0A225301D6CFD2B536FEC08B4C96CC7388 |

没有修改 UE 源码、镜像插件或冻结资产；使用已完整构建的插件，启动前审计通过。未声称本批重新进行了完整 Editor 构建、DataValidation 或普通 Editor 重启。

## 验证与未完成项

- 新回归测试先红后绿，覆盖删除后的支持反转、Abort/重试、Reset、2048 次增删及回滚零分配。Core Release 固定 JIT 串行 2884 项通过，沿用排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests 的既有过滤；相关 17 项在 .NET 9.0.17 通过。
- Import Release 固定 JIT 串行全量 2485 项通过、1 项原有 Editor 条件跳过。
- Godot Optimize 构建零警告／错误；60 Hz 接触 smoke、场景接触 smoke 通过。
- 首次广泛测试误用未优化 Debug，并行运行出现两个旧路径的零分配断言失败；已中止该轮，保留 `core-physics.log`，随后按项目既定 Release 固定 JIT 串行方式验证。没有修改这些测试或阈值。
- 静态目标完整落地矩阵仍 9/12：60/120 Hz 普通、高落差、平移平台、旋转平台均通过；30 Hz 仅高落差通过，另外三项旧失败保留。
- 动态 Flail 休眠／稳定性矩阵现在 **0/3**（上一批 1/3）。30 Hz 原先偶然通过的 AnimMan 本次未休眠，UE 同设置也未休眠，但两者轨迹仍不一致；60 Hz Mannequin 与 UE 同样未稳定；120 Hz AnimMan 与 UE 同样休眠过晚，达不到保持一秒的门槛。未延长用例或放宽阈值。

下一步先定位 30 Hz AnimMan 第 20 帧差异，同时保留原生也失败的稳定性问题；再推进普通角色 Ragdoll Seed／胶囊停用／骨盆跟随／限速／物理显示／退出，随后 Get-up、Pose Recovery、Mantle、完整 Camera、最终十分钟性能预算。普通角色目前仍未接入这条 Core 物理生命周期，不能把本批对照测试当成可交互功能交付。

用户 P4 规划文件未改动、未纳入本批提交，SHA256 保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
