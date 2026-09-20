# 项目工作目录

- Godot 项目的主目录是 `D:\GodotALS`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `D:\GodotALS-p3-direction-alignment`、`D:\GodotALS-p4-pose-foot-placement`、`D:\GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新进度：`docs/verification/2026-09-21-physics-body-inertia.md`。刚体层几何/连接臂惯量调节已通过 120 份原生记录校验并接入实验关节 owner；绑定时从 cooked actor bounds 与实际拓扑重算，释放后恢复原惯量。Mannequin/AnimMan 为 19/21 刚体，共 40 刚体/36 关节。30/60/120 Hz 无接触整链和 120 Hz 普通落地通过；30 Hz 普通角度、60 Hz 普通速度及三个频率高速落地仍失败。60 Hz 普通在原始惯量模式通过，补齐原生惯量后回退；不得冒用上一批的通过结果。18 次单关节回归与 144 组重算惯量轨迹回放通过，但后者最大旋转偏差仍 0.403482 rad，不代表后端等价。关节局部质量调节、独立约束行、投影/接触仍待接入或对齐。原生轨迹与软限制释放见 `2026-09-21-physics-joint-solver-reference.md`，绑定基础见 `2026-09-21-physics-joint-transport.md`，原生配置与 912 组角度数学见 `2026-09-20-physics-joint-reference.md`。cached solver 使用 Pyramid；不能硬编码孤立对子惯量比例。无接触隔离须同时清空 collision layer 和 mask。项目采用 Jolt Physics、2 mm penetration slop；实验关节尚未接入普通角色。先完成整链稳定性，再接 Ragdoll/Get-up/Pose Recovery。普通入口已有地面与中等落差自动 Roll；高落差/翻滚离地的 Ragdoll 仅接通触发判定。`--action-preview` 保留旧预览；P5C/P6 未整体验收。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。
