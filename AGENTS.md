# 项目工作目录

- Godot 项目的主目录是 `D:\GodotALS`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `D:\GodotALS-p3-direction-alignment`、`D:\GodotALS-p4-pose-foot-placement`、`D:\GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新进度：`docs/verification/2026-09-21-physics-angular-mass.md`。修复停用电机目标更新与全睡眠集合投影写回导致的唤醒，后端睡眠/再激活探针通过。新增可选 `--native-angular-mass`：只把预测驱动轴/pyramid 限位轴和关节局部惯量用于系数计算，未替换 Jolt 内部响应，默认关闭。与 `--native-projection` 同开时九项七过：30 Hz 普通/高速通过（高速锚点 0.095589 m、末秒速度 0.180139）；60 Hz 普通/高速速度分别 0.443923/0.333732 失败；30 Hz 无接触末秒角误差退化到 0.039790 rad。默认九项仍八过，144 组结果与旧默认字节一致。组合 144 组相对单投影 5 改善/4 退化/135 近似不变，最大旋转仍 0.449575 rad。18 次单关节、跨零、通道和纯阻尼拒绝通过。下一步约束行接口/逐端局部惯量响应与接触共同求解，不再把仅改系数当完整原生求解；普通 Ragdoll 仍未接入。
- 前一批投影：`docs/verification/2026-09-21-physics-linear-projection.md`。原生 cached 线性投影 262 组 delta/速度/姿态对照通过；全链先缓存再投影，静态子级跳过。Godot 仅诊断场景 `--native-projection` 可选开启，默认保留上一批实现。投影开启后九项锚点全过，但整链仅六项通过：30 Hz 普通角超限、30/60 Hz 高速末秒速度失败。144 组位置最大偏差降至 0.001189 m，旋转最大偏差增至 0.449575 rad，93 组旋转退化；parity_asserted=false。18 次单关节、跨零释放通过。下一步对齐角限位/驱动轴、关节局部惯量及投影接触阶段；不得将独立公式通过当作完整物理等价。默认实现与剩余总清单见下条。
- 默认物理实现：`docs/verification/2026-09-21-physics-independent-channels.md`。软限制/硬锚点与姿态驱动分成两个 Jolt 约束，各自目标、系数和累计冲量；40 刚体/36 逻辑关节/72 后端约束。独立通道探针、18 次单关节、软限制第 3 帧跨零、三个频率无接触与普通落地、60/120 Hz 高速落地均通过。剩余 30 Hz 高速落地第 21 帧锚点 0.290387 m 失败，九项仅八项通过；30 Hz 普通末秒角超限 0.096568 rad 接近门槛。60 Hz 普通/高速瞬态角超限仍约 0.88/0.93 rad，不能称观感通过。144 组对照最大旋转偏差降至 0.394426 rad，位置偏差 0.011906 m，parity_asserted=false。当前电机仍用 Jolt 误差/轴与 warm start，不等于完整原生约束行。下一步投影/接触、关节局部质量与限位轴；检查停用限位通道的目标更新是否造成多余唤醒。零刚度 6DOF 位置电机会停用（不是硬化），纯阻尼逐通道拒绝。启用条件见 `2026-09-21-physics-joint-activation.md`，原生惯量见 `2026-09-21-physics-body-inertia.md`，原生轨迹见 `2026-09-21-physics-joint-solver-reference.md`，绑定见 `2026-09-21-physics-joint-transport.md`，912 组角度数学见 `2026-09-20-physics-joint-reference.md`。cached solver 使用 Pyramid；不得硬编码孤立对子比例或将关节局部惯量写回共享刚体。无接触隔离须同时清空 collision layer 和 mask。项目采用 Jolt Physics、2 mm penetration slop；实验关节尚未接入普通角色。先完成整链稳定性，再接 Ragdoll/Get-up/Pose Recovery。普通入口已有地面与中等落差自动 Roll；高落差/翻滚离地的 Ragdoll 仅接通触发判定。`--action-preview` 保留旧预览；P5C/P6 未整体验收。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。
