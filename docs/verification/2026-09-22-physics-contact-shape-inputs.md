# 碰撞形状的 actor 变换与落地回归

本批直接在 `.` 的 main 实现。修复碰撞形状经质心往返转换产生的舍入差异，普通 120 Hz 落地恢复通过：AnimMan 第 635 帧、Mannequin 第 1004 帧休眠，与独立 UE 世界参考的休眠帧相同。末秒 V/W 均为 0，独立复跑报告字节一致。完整矩阵由 8/12 恢复为 **9/12**；仍不代表普通 demo 或所有物理场景完成。

## 原因与实现

UE 的 `FShapeInstance::UpdateLeafWorldTransform` / `GetLeafWorldTransform` 将碰撞叶的相对变换直接与粒子的 actor 变换相乘。旧 Core 先把 shape local 转到 COM，再与预测 COM 相乘；actor 四元数经过 float 存储后并非严格单位四元数，这条代数等价路径会引入约微米以下的数值差异，并进入接触点生成、恢复和摩擦历史。

- `AlsRigidBodyIntegration.Predict` 保留已经计算的实际存储 actor，与原有 COM 和速度一起返回，没有增加一次姿态重建。
- `AlsJointIsland` 使用预分配数组把 actor 传给接触 owner；关节求解继续使用原来的 COM。
- `AlsWorldContacts` 的 bounds、query、恢复、history、Gather 共用直接由 leaf local 与 actor 组成的形状变换。旧 COM-only 诊断入口通过 `StoreActor` 重建；它不能恢复调用方已经丢失的原始舍入信息，实际 Island 路径不经过这个后备分支。
- `IAlsIslandContacts` 新增借用 actor span 的重载，旧无状态 provider 可继续使用默认转发；三个 WorldContacts 包装器同步转发，失败回滚与零分配回归通过。
- 没有修改积分结果、关节/接触求解次数、摩擦系数、休眠参数、步长断言或通过门槛。

## 独立原生观测

扩展现有 PreSolve 只读观测，记录实际 constraint 的 shape-relative/world 变换、局部接触点/法线、匹配后的静摩擦 anchor、初始/禁用标志及恢复系数。钩子位于碰撞激活和锚点匹配之后、Solver Gather 之前，避免误用 Scatter 后改写的 anchor。既有 after-solve 观测未改。

两次冷导出 SHA256 均为 `9F39639FAAEEBB9C34CB7FE570CDAB69EBFFEF2F696F62AE8008A31733ADD0FC`。与上批 `touchdown-world.json` 比较，48,040 个身体样本的原始 JSON 完全未改变。

新增冻结窗口 `assets/config/v4_physics_world_shape_window.json`：2,555,407 字节，SHA256 `924DE0BFEC352133505170E6FE05B90AF9F4C0E21C97BCC6F7A59FCCCF908BF1`。包含两个模型的基线 49 及完成步 50..55，并保留完整来源哈希。旧冻结参考未替换。

新增独立测试从 UE PreIntegrate 输入运行真实 Core Island 积分，通过真实 WorldContacts 的 PrepareBounds 观察形状；不把 UE 预测输出注入计算。共 190 个动态接触端点：

| 指标 | 修复前 | 修复后 |
| --- | ---: | ---: |
| 最大位置距离 cm | 1.9056437107209895e-6 | 0 |
| 最大四元数分量差 | 3.3306690738754696e-16 | 0 |

测试先红后绿，.NET 8.0.28 与 9.0.17 结果一致。这里证明的是这些独立输入下的碰撞形状边界。

## 实际 Godot 结果

本批重新捕获两个模型各 44 个输入帧：0..9、49..59、76..78、140..159，共 88 个运行步。相较原生对应完成步，最终 V/W 最大差全部为 **0**；上批 AnimMan 完成步 54、Mannequin 完成步 55 的首个已采样速度分歧消失，后期已采样窗口也一致。未采样区间及最终 actor 的 P/Q 没有据此宣称逐帧逐分量等价。

完成步 50..55 的 12 个模型帧、100 个接触对、136 个点：形状位置/旋转、局部点/法线、已有摩擦 anchor 全部差 0，点数和初始/anchor 标志无差异。相应 PostIntegrate/PreSolve 身体输入的 24 个阶段、8 类最大误差字段均为 0。完成步 143..152 的 20 个模型帧、416 对接触方向、图内顺序与集合继续一致。

| Hz | 普通落地 | 高处落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 | 失败 | 通过 | 失败 | 失败 |
| 60 | 通过 | 通过 | 通过 | 通过 |
| 120 | 通过 | 通过 | 通过 | 通过 |

普通 120 最大 anchor 为 `0.5033122136111926` cm，末秒 V/W 为 0，末秒 limit 为 `0.010122481093551072` rad，84,571 个接触点；两次报告 SHA256 均为 `57E02BE2D56BCEBDE1E842D76FF333906A9D729DE48854DB1A131F49445A3603`。

普通 30：AnimMan 第 159 帧睡，Mannequin 到第 300 帧未睡；末秒 V `6.988268852233887` cm/s、W `0.4035205543041229` rad/s。两个 30 Hz 平台场景仍在开始移动前因未睡失败。原生 30 Hz 自身也有既有休眠失败；本批未完成该频率完整轨迹对照，也未关闭这三项或放宽门槛。

## 验证与限制

- Core Release 固定 JIT、既定过滤排除两个 P5A 类、串行：2,873 通过。
- Import Release 固定 JIT、串行：2,477 通过，1 项原有 skip。
- .NET 9 定向 Import 10 项通过：新形状测试、原生 Gather、8 组 coupled。61 例、1,464 阶段的同输入求解五通道零误差继续成立。定向 Core 20 项通过，覆盖世界接触、关节岛、接触次序及相关回滚/零分配。
- Godot Optimize 构建 0 warning / 0 error；30/60/120 接触 smoke、60 场景 smoke、144×12 原生对子、8 世界姿态场景通过。对子本批未复现上一批的回调步长失败，不声称偶发问题已修复。
- 按 UE 插件构建技能执行完整 Editor target build + plugin audit，fingerprint `8CFC4E0EEB11697E1B2445B402CE6DAE5407E2C635D1E43B6DC008D6C9554019`，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`。变更的 canonical/mirror 源文件 SHA256 同为 `904575A1A685B31B635B8A7FB9F3E39998F6198585D31C636FFA9CAAA3F22BFF`。
- DataValidation 退出 0，0 error / 3 个旧 warning。普通 Editor 首轮 PID 24564 加载验证成功、日志关闭后进程退出 `0xC0000005`；独立重启 PID 35272 加载验证成功、原生退出码 0。两个旧 Condition 与间歇性退出 AV 仍未修复；首轮失败日志保留，不将复跑成功称作根因修复。本批无打包构建结论。

产物位于 `artifacts/physics-contact-shape-inputs-20260922/`：原生 `native.json` / `repeat.json`、先红后绿 `shapes-before.log` / `shapes-after.log`、`before.json` / `after-contacts.json`、`after-world.json` / `after-inputs.json` / `after-order-late.json`、`captures/`、`matrix/`、`checks/`、全部构建/回归/Editor 日志。`run_matrix.ps1` 与 `run_checks.ps1` 为本次复现脚本；使用新产物目录以保留历史失败。

下一步推进普通 demo 的 Ragdoll 接入与退出生命周期，继续保留 30 Hz 稳定性问题；随后是 Get-up / Pose Recovery、Mantle、完整相机与最终十分钟性能预算。当前正常入口仍未切换到新物理后端，总目标继续进行。用户的 P4 规划修改保持原始字节，未纳入本批提交。
