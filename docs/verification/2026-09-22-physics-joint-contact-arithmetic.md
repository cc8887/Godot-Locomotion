# 关节与接触求解的原生数值顺序

本批直接在 `D:/GodotALS` 的 main 实现。114 个独立线性关节的位置/旋转修正与 UE 相同；61 个整链案例、1464 个阶段的新鲜 Core 重放，DP、DQ、V、W、最终位置五个通道误差全部为 0，.NET 8.0.28 和 9.0.17 一致。这是**相同输入下的求解验证**，不是整个物理世界或普通 Ragdoll 已等价。

实际稳定性矩阵仍为 **8/12**。普通 120 Hz 的 AnimMan 第 727 帧休眠，Mannequin 到第 1200 帧仍未睡，未达到原来要求的持续一秒休眠。相较上批 Mannequin 第 1138 帧才睡，本次仍有回归；没有改休眠参数或放宽门槛。普通 demo 尚未接入该后端。

## 实现与证据

- 新增可选 `-PhysicsCoupledJointGather`。通过 UE 原生 conditioning / inertia 工具记录质量与惯量，通过独立原生容器记录单关节线性位置响应。诊断容器使用自己的粒子、SOA、SolverBodyIndex，以及复制的 solver body；只在诊断副本关闭角约束和驱动。没有把这些诊断设置应用到完整重放。
- 质量精确一致，double 惯量张量最大差 `4.338925755574552e-19`，转换到 float 缓存后差 0。114 个独立关节的 DP/DQ 差 0。导出的张量是原生工具结果，不能称为读取了内核私有缓存。
- 参考引擎以 `/fp:fast` 构建。检查实际 `Module.Chaos.12.cpp.obj` 和 `Module.Chaos.7.cpp.obj`，确认只照 C++ 表达式写代数等价代码仍会改变舍入顺序。线性关节父端 DP 先合并三轴冲量再乘质量；子端 DP 保留逐轴缩放减法；两端速度先合并再乘带符号质量。
- 角关节速度先把两端存储的 float 提升为 double，再相减。Projection 与接触缓存叉积显式保留 float 分离乘减，避免 .NET 9 的 `Vector3.Cross` 改变舍入。
- 接触法向/切向位置误差按原生实际分量归约顺序累加；速度阶段保留初始目标速度参与加法的位置、共享倒数乘法的夹取边界，以及先加法向角速度、再加两个切向响应的顺序。未改变摩擦系数、算法分支、求解次数或物理门槛。
- 新增两个真实落地捕获：AnimMan 输入帧 53、Mannequin 输入帧 54。修正前第 4 次 position_contacts（首次摩擦）出现差异；按原生切向顺序修正后仅剩 AnimMan 第 6 次法向差异；法向修正后 61 例全部 DP/DQ/P 为 0；速度修正后全部五个通道为 0。
- 8 组整链参考测试均升级为新鲜重放的五通道零误差断言；历史 `coreSamples` 保持原始字节与原门槛。姿态 Q 使用归一化 dot 指标，最大 `5.55e-16`，未声称 quaternion 分量逐位相同，也未声称覆盖所有可能约束配置。

新冻结文件：

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| `v4_physics_joint_gather_reference.json` | 493007 | `C05A3923154FD7B270AEFA8F28371125D8C39C4A33D089102A4580C839BA52B7` |
| `v4_physics_touchdown_coupled_reference.json` | 2591649 | `BF56129373702E381683EFD906C5830D9DBCB5EA06A4C9BABDB82D2086410F46` |

完整 Gather 诊断两次冷导出均为 `C2162053E6805DEC9232B25AD0E67FAE4EA4C84826CD22548891B7A4D36D59C7`，10021304 字节。新增诊断前后的 6 个 capture、144 个标准 nativeSamples、114 个逐关节快照未改变。落地重放冷复导与冻结文件同哈希。旧参考资产未修改。

## 实际 Godot 与稳定性

最后一次 Optimize 构建后重新捕获 88 个输入步。两个模型已采样的前 10 步 V/W 精确一致；AnimMan 完成步 50..53、Mannequin 50..54 也精确一致。未采样的 11..49 不作逐帧结论。

| 模型 / 完成步 | 最终最大 V 差 cm/s | 最终最大 W 差 rad/s |
| --- | ---: | ---: |
| AnimMan / 54（首个已采样分歧） | 0.0007275678802 | 0.00001703650483 |
| Mannequin / 55（首个已采样分歧） | 0.0006433167720 | 0.00003033566084 |
| AnimMan / 147 | 0.002600921004 | 0.0006722228633 |
| AnimMan / 160 | 0.1380966794 | 0.04462158752 |
| Mannequin / 147 | 0.09602258876 | 0.02425015078 |
| Mannequin / 160 | 1.242046319 | 0.08754934853 |

部分后期误差改善、部分退化，不能称整段轨迹改善。完成步 143..152 的 20 个模型帧、416 个接触对，两端方向、图内次序、集合仍全部相同。

| Hz | 普通落地 | 高处落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 | 失败 | 通过 | 失败 | 失败 |
| 60 | 通过 | 通过 | 通过 | 通过 |
| 120 | 失败 | 通过 | 通过 | 通过 |

普通 120：最大 anchor `0.5033136714` cm，末秒 V `2.251618624` cm/s、W `0.2701405883` rad/s、limit `0.01006515597` rad，100798 个接触点。普通 30：AnimMan 第 142 帧睡、Mannequin 未睡，末秒 V `6.850353241`、W `0.4115706980`；原生 30 Hz 自身也有既有失败，不将其全部归因于移植。

## 世界输入定位与下一步

额外独立导出完成步 50..55 的世界观测，两次冷导出 SHA256 `2E1476305A8A145570FC63B77888D7F693202F3E9E162099BFDC6406806F8F0B`。产物保存在本批 artifacts，未替换已有冻结窗口。

- 首个速度分歧发生前，AnimMan 到完成步 54 的输入、Mannequin 到完成步 55 的输入，PostIntegrate/PreSolve 的身体姿态、速度、质量及惯量字段均精确相同。前三步原窗口的首步 PostIntegrate 原始惯量不同是 conditioning 尚未执行的阶段差异，PreSolve 一致；没有隐藏该差异。
- 完成步 50..55 共 12 个模型帧、100 个接触对的方向、次序、集合一致。
- 但接触局部点/法线已存在浮点差异：例如 AnimMan 完成步 53 最大局部分量差 `7.62939453125e-6` cm，Mannequin 完成步 54/55 最大 `6.103515625e-5` cm。比较时恢复 float 存储值，未比较会在 Scatter 改写的 friction anchor。
- 这些几何差异在部分尚未施加冲量的接触上更早出现；不能仅凭最大差就断言已找到全部休眠原因。下一步应沿首个有效落地接触的形状变换、局部点生成/恢复、摩擦 anchor 历史和 Gather 行继续核对，使用已有同输入求解零误差门禁排除内核退化。

随后仍须完成普通 120 Hz 稳定性、普通 demo Ragdoll 接入、Get-up / Pose Recovery、Mantle、完整相机与最终十分钟性能预算。总目标仍进行中。

## 验证和复现

- Core Release 固定 JIT、按既定过滤排除两个 P5A 类、串行：2873 通过。
- Import Release 固定 JIT、串行：2476 通过、1 项原有 skip。定向最终 .NET 8 为 10 项（Gather / coupled / joint step）；.NET 9 为 12 项（另含两个世界关节观测）；.NET 8 全量包含后两项。
- Godot Optimize 构建 0 warning / 0 error；30/60/120 接触 smoke、60 场景 smoke、144×12 原生对子、8 世界姿态测试通过。
- 本批中途一次原生对子在 frame 0 报 callback 步长不同，独立重跑和最终重跑均通过。仅增强失败消息以记录 case/frame/actual/expected/configured Hz，没有跳帧或放宽步长断言，尚未证明偶发问题已修复。
- UE 初次尝试直接调用未导出的 CachedSolver Init/Apply 导致 LNK2019，随后改为独立容器。最终完整 Editor target build + plugin audit 通过，fingerprint `A979C9E56F96A76C4ECECB5F7E6BC7303B1ACDCDB8B07C84F07133AD1D5D1425`，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`。源码镜像逐文件哈希相同。
- DataValidation 退出 0，0 error / 3 个旧 warning。正常 Editor PID 33980 出现 `ALS_JOINT_GATHER_EDITOR_RESTART_OK` 后原生退出码 0；两个旧 Condition 和历史间歇性退出 AV 未修复。本批没有打包构建结论。

所有诊断产物位于 `artifacts/physics-joint-gather-20260922/`：最终测试 `core-final.log`、`import-final.log`、`targeted-final-net8.log`、`targeted-final-net9.log`；最终场景 `final-checks/`；最终矩阵/捕获/比较 `final/`。根目录下较早的 matrix/captures 是仅关节修正时的中间结果，不代表最终接触修正。

汇编证据保存在 `chaos-joint-sections.txt`（线性位置 #1D8、速度 #210）和 `chaos-contact-sections.txt`（SolvePositionImpl、SolveVelocityImpl）。初次未命中的 test filter、失败编译及中间数值试验日志均保留，不计为通过。`compare_touchdown_geometry.ps1` / `touchdown-geometry-final.log` 记录几何诊断；最初环境名称映射错误的日志另行保留。

用户未提交的 P4 文档保持 SHA256 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，不纳入本次提交。
