# 项目工作目录

- Godot 项目的主目录是 `.`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `../GodotALS-p3-direction-alignment`、`../GodotALS-p4-pose-foot-placement`、`../GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新进度：`docs/verification/2026-09-21-physics-joint-transport.md`。两套资产的实验 adapter 已绑定 36 个 Godot 关节（2 个六轴自由 root 约束跳过），使用公开角弹簧 API；单关节 30/60/120 Hz 三轴正负扰动 18 次通过。整链 60 Hz 落地仍未通过最后一秒 <0.2 m/s 的门槛（峰值 0.363421 m/s）；无接触整链 60/120 Hz 通过，但 30 Hz 最后一秒角度超限 0.251030 rad，不能把问题只归因于接触。尚未接入普通角色。下一步补原生受力/轨迹参考，修复软限制/驱动/接触耦合并完成整链稳定性，再接 Ragdoll/Get-up。原生有效配置、912 组角度数学参考见 `2026-09-20-physics-joint-reference.md`，实际 cached solver 使用 Pyramid，不是椭圆锥。C# XML 缺少条目不表示没有公共角弹簧 API；无接触隔离须同时清空 collision layer 和 mask。项目采用 Jolt Physics、2 mm penetration slop，独立刚体落地通过不能替代整链验收。普通入口已有地面与中等落差自动 Roll；高落差/翻滚离地的 Ragdoll 仅接通触发判定。`--action-preview` 保留旧预览；P5C/P6 未整体验收。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。
