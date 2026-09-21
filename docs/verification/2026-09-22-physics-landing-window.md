# 落地窗口：补齐外部粒子到求解器的穿透参数转换

工作目录 `.`，`main`。本轮修正真实移植缺口，并将下一个缺口缩小到粗碰撞筛选；没有放宽验收门槛，普通角色仍未接入实验 Core 物理后端。

## 发现与修复

扩展 `PhysicsWorldContactStart` / `PhysicsWorldContactFrames`，允许在十秒世界模拟中选择最多 10 个连续完成步。窗口带 native shape0/shape1 编号、求解器类型、约束初始穿透策略，以及逐点 initialContact/anchor/restored 状态。数据是 after-solve 观察，不称作原生 pre-Gather 输入。

原生 AnimMan 第 15 步 foot_l 与 Core 都重建为相同的 3 个接触点，局部点仅有微小舍入差。两个新点均没有旧摩擦锚点，因此不是“原生少重建或多匹配了点”。真正差异是约束 `depenetrationVelocity`：原生为 **1e10**，Core 为 **0**。

旧 `v4_physics_contact_settings.json` 实际观察的是 external particle 的 `-1`。此前只移植了 `FPBDCollisionConstraint::Setup` 的 `max(body0, body1, 0)`，遗漏了它之前的 `FPBDRigidsSolver::ProcessSinglePushedData_Internal`：负值意味着使用 `GetSolverSettings().DepenetrationVelocity`，本项目为 1e10。

这纠正了之前 `2026-09-22-physics-contact-settings.md` 关于“legacy solver 参数不应使用”的不完整结论。该参数不是直接替换约束公式，而是先解析外部粒子的默认设置，再由约束组合。

- Core 新增独立 `ResolveParticle(external, solver)`。显式 0 和正数保持原值，负值取已导出的 solver 值；非有限/非法默认值拒绝。
- Import 将 40 个 external 粒子值转换为内部值，沿用原始导出文件，未修改已有字节哈希依赖。
- 原生世界窗口逐约束验证实际值与编译结果相符；第 15 步重建点的 native InitialPhi 都为 0，Core 之前错误保留约 -5.65/-5.75 cm 的初始穿透。
- 比较工具支持直接读取 Core capture 目录，报告每个文件的 SHA256。旧 coupled-reference 接口、旧窗口默认 0..3 输出验证字节不变。

## 实际 Godot 捕获结果

修复前后相同初态、普通 30 Hz，与已有独立 UE 完整世界参考比较，使用全链实际捕获而非手工传入相同 Gather 行：

| AnimMan 完成步 | 修复前最大线速度差 cm/s | 修复后 cm/s |
| --- | ---: | ---: |
| 15 | 21.70255345 | 0.00067244066 |
| 16 | 74.45 量级 | 0.00050523047 |
| 17 | 46.98 量级 | 0.00053682960 |
| 18 | 64.23 量级 | 0.00071938181 |
| 19 | — | 37.524195 |

第 15 步最大角速度差降至 0.000038974258 rad/s。Mannequin 第 18 步仍为 0.00030606775 cm/s，第 19 步仍出现 15.218464 cm/s / 8.0736311 rad/s 差异；尚未修复。

下一缺口已有证据：第 19 步 Core 多出 Mannequin lowerarm_r 与 spine_01 的地面接触，原生没有。原生对应膨胀包围盒最低 Z 分别为 -42.632433 和 -42.179279 cm，地面顶面是 -43.398404 cm，尚未相交；Core 仅用扩大后的 narrow-phase cull 距离就创建了接触。AnimMan 第 18 步也已有多余 pelvis 接触但尚未产生明显速度差，第 19 步出现多余前臂接触。

这仍是粗碰撞遗漏的证据，不是已实现的修复。后续需移植完整 particle bounds、预测姿态、动态反向速度扩张及 static/kinematic 分支；筛选必须发生在 manifold restore 之前，不能只在 Query 里丢点而保留旧接触缓存。

## 验证

- Godot 优化构建 0 warning / 0 error；30/60/120 Hz 接触 smoke 全部通过。
- Core Release 固定 JIT 串行全量 2844 通过，沿用既有两个排除类；Import 全量 2441 通过、1 既有条件跳过。设置与原生落地窗口 3 项 .NET LatestMajor roll-forward 通过。
- 十二矩阵 **9/12**：全部 60/120 Hz 及高速 30 Hz 通过；普通/平移/旋转 30 Hz 仍未满足休眠预算。普通 30 Hz AnimMan139 帧睡（上批204，原生159）；Mannequin未睡，末秒 V7.439918 cm/s / W0.428579 rad/s。最大锚点2.159740 cm。不能用入睡更早称轨迹等价。
- UE 全 Editor 构建/审计通过，fingerprint `31AC9E0DB06E48871995C8C4F99E65B4312CAF3B4EB877D3DEA7F6D1A9878FE0`。首轮 shape-index lambda 返回类型编译失败，显式 int32 后完整重建通过，失败日志保留。
- 原生完整窗口来源两次冷导字节一致：SHA256 `7D5B284146D63CAE42E01BECD6D2380932DA06760228D4A6988195631AE1DE6A`。
- 新 `v4_physics_world_landing_window.json`：两模型 frame11..21，948246 bytes，SHA256 `EA4D9AD1B99E121F21EC5BF415743A710698D2ED68FFD828CB9736094B37E7DA`。抽取重复一致，移除不适用的十秒预算摘要。
- DataValidation 0 error / 3 既有 warning。普通 Editor PID29224 成功加载标记后以 0xC0000005 退出，DLL 已释放；两条旧 Condition failed 保留。本轮普通重启完整门禁失败，未解决既往间歇退出异常。
- 原始证据目录：`artifacts/physics-landing-window-20260922/`。40步比较重复字节一致；新捕获目录 `capture/`，上批修复前输入仍保留在上一批目录。未重写旧证据。

接着补齐上述粗碰撞筛选及接触生命周期，解决第 19 步/三个低频失败，再接普通 Ragdoll、Get-up、Pose Recovery，并继续 Mantle、完整 Camera 与最终十分钟性能预算。完整 ALS 目标仍未达成。
