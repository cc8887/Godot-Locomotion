# 项目工作目录

- Godot 项目的主目录是 `D:\GodotALS`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `D:\GodotALS-p3-direction-alignment`、`D:\GodotALS-p4-pose-foot-placement`、`D:\GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新进度：`docs/verification/2026-09-20-montage-root-motion.md`。共同 Montage 已提取并发布身份化 Root Motion，Motor 尚未消费，R 仍为原地预览；下一步处理同帧碰撞移动与脚部查询顺序。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。
