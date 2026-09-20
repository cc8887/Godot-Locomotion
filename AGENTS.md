# 项目工作目录

- Godot 项目的主目录是 `.`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `../GodotALS-p3-direction-alignment`、`../GodotALS-p4-pose-foot-placement`、`../GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新进度：`docs/verification/2026-09-21-physics-joint-solver-reference.md`。新增真实同步 Chaos 步进的 144 组单关节轨迹/受力参考、有效 solver overrides 和两层惯量调节观测。软限制改为按预测角度激活，释放已回到限内的动量：整链 60 Hz 普通落地通过现有门槛（最后一秒最大线速度 0.181105 m/s），但 30 Hz 普通/无接触、120 Hz 普通及三个频率高速落地仍失败。144 组回放最大旋转误差从 0.871 降至 0.403 rad，仍有 8 组误差增大；不得宣称后端等价。关节质量调节数学已对原生验证，尚未接入运行时；刚体几何/连接臂惯量调节仍待移植。单关节绑定基础见 `2026-09-21-physics-joint-transport.md`，原生有效配置、912 组角度数学参考见 `2026-09-20-physics-joint-reference.md`。实际 cached solver 使用 Pyramid；不得把单对刚体记录的惯量比例硬编码到整链。UE 导出器复用一个预览世界，普通 D3D12 与 NullRHI 导出字节一致。无接触隔离须同时清空 collision layer 和 mask。项目采用 Jolt Physics、2 mm penetration slop，实验 40 刚体/36 关节尚未接入普通角色；下一步先解决完整约束/接触稳定性，再接 Ragdoll/Get-up/Pose Recovery。普通入口已有地面与中等落差自动 Roll；高落差/翻滚离地的 Ragdoll 仅接通触发判定。`--action-preview` 保留旧预览；P5C/P6 未整体验收。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。
