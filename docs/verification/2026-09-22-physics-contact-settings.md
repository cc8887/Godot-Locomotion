# 实际接触求解设置与初始穿透修正

主目录 `.` / `main`。本批纠正了实验后端此前使用的三个默认值；普通角色未切换后端。

## 原生观察

新增 `-run=AlsGodotExport -PhysicsContactSettingsOutput=<new absolute json>`，创建项目隔离物理世界并推进一帧，读取真正的 collision container 设置。另在实际两模型身体创建后观察 external particle，再用原生 constraint Setup/Activate 观察 48 个组合；没有把 Core 的计算结果作为观察输入。

新资产 `assets/config/v4_physics_contact_settings.json`，两次冷导出 SHA256 相同：

`29A780BCB8D5E591E7CDEE881CA79D349E2F32C44F705502A49532C2F4128E5E`

| 设置 | 实际 UE | 之前实验整链 |
| --- | --- | --- |
| MaxPushOutVelocity | 1000 cm/s | 0（无限制） |
| RestitutionThreshold | 1000 | 2000 |
| 身体 InitialOverlapDepenetrationVelocity | 40 身体均 -1 | 未传输 |
| Constraint 解析后的初始穿透速度 | max(body0, body1, 0)，默认组合为 0 | -1（跳过初始穿透处理） |

另观察到 legacy solver DepenetrationVelocity=1e10；原生 constraint Setup 没有用它覆盖已观察的 particle 值，因此没有将它误接到 Gather。摩擦位置/速度迭代 4/1、shock 3/2、splitImpulse=false，初始穿透处理开关 1、restitution pre-integrate 开关 0。

48 个原生 box 约束组合覆盖动态/动态、动态/kinematic、kinematic/动态，以及 -1/0/.3/100 的全部双方输入。Setup 与 Activate 后速度逐值一致；动态双端 per-contact=true、其余 polygon 对 false。它不是 sphere/capsule 几何或完整轨迹对照。

## 实现

- Core 新增明确的双方 overlap 参数解析。WorldContacts 可接受完整身体参数数组，复制并验证后按实际接触端点解析；旧独立诊断不传数组时保留显式 Gather 设置。
- Import 严格匹配模型、physics asset、身体索引/骨名及 authored override/default；拒绝当前未实现的 split impulse、其他摩擦/shock 迭代和 pre-integrate restitution 语义。
- 实验整链使用观察到的 MaxPushOutVelocity/RestitutionThreshold/身体 overlap 参数。外部场景端使用 0；当前场景没有导入非零自定义 overlap override，不宣称通用场景参数已完整迁移。
- 失败步骤不发布初始穿透历史。新增 Core 测试验证双方速度解析产生不同 Gather 深度、数组副本与 abort/retry，既有初始穿透/Gather 原生参考继续回归。

## 整链结果

日志：`artifacts/physics-contact-settings-20260922/`。仍使用旧时长、几何预算和至少持续一秒睡眠的验收门槛。

| 场景 | 30 Hz | 60 Hz | 120 Hz |
| --- | --- | --- | --- |
| 普通落地 | AnimMan 未睡 | 通过 | 通过 |
| 高速落地 | AnimMan 未睡、锚点误差偏大 | 通过 | AnimMan 未睡 |
| 平台平移 | 启动前 Mannequin 未睡（本批回归） | 通过 | 通过 |
| 平台旋转 | 启动前未完全休眠 | 通过 | 通过 |

总计 **7/12**，替代上一批 8/12。高速 30 Hz 不再第 9 帧穿地，可推进完整 300 帧，Mannequin 第 127 帧睡眠；AnimMan 未睡，最大锚点误差 6.66651147 cm、末速 6.19673729 cm/s、末角速 .73285282 rad/s、关节超限 .04531322 rad。仍不满足稳定性要求。

高速 120 Hz Mannequin 第 244 帧睡眠，AnimMan 未睡，末速 2.79015040 cm/s、末角速 .75397527 rad/s；不能将失败模型发生变化算成通过。高 30 Hz 逐帧 geometry trace 与第 6/7/8 帧捕获保存在同目录，为后续发现/历史/Gather 核对提供新输入。

## 验证门禁

- Godot 优化构建通过；60 Hz contact smoke：551 精度、9 几何、68 polygon、3 事务、5 流形、5 睡眠检查通过。
- Core Release 固定 JIT 串行 2822 通过（沿用两类 P5a 测试排除）；Import 同配置全量 2408 通过、1 既有跳过；退出码均 0。
- UE 完整 Editor 构建及插件审计通过，fingerprint `728E36F9847E68D819F214FB5B45599D50D8A2A8C8AA61CD83A91E4D30D80D3A`，日志前缀 `20260921T160258292Z-31813e59b3ab4ee498c9c5a5d9a61d4f`。首两次编译分别暴露 TObjectPtr 推导和 protected/外部 API getter 使用错误，已按源码修正；失败日志保留。
- 两次冷导出退出 0、字节一致；DataValidation 退出 0、0 errors/3 既有 warnings。主仓库和 UE 项目的三份改动源码哈希一致。
- 普通 Editor PID 32060，加载 exporter marker 成功，原生退出码 3221225477（0xC0000005）；退出后没有 exporter DLL 占用。两条旧 Condition failed 仍在。普通重启门禁未通过，旧退出异常未修复。

接下来继续五项失败的完整接触链路核对，尤其 quadratic/混合几何的分离接触、实际 primitive 参数、原生 tolerance 和历史规则；再普通 Ragdoll owner、pelvis 胶囊/相机、Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算。本批不是完整物理或 ALS 验收。
