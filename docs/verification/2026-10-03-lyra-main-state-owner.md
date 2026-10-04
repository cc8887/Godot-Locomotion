# Lyra Main 状态归属拆分

2026-10-03，继续 ALS 人物与 Lyra Animation Interface/Layer 移植。此前已取得多组实际 Linked 实例执行原 Main 的连续参考；本批先将 Main 更新、直接 Lean、根回调历史和 Main 曲线反馈改为角色持有，为后续按绑定实例分配私有图宿主提供边界。

## 生产改动

新增 `LyraMainGraphStateOwner`，实际持有原 `LyraMainUpdateHost`、`LyraMainLeanCompositionHost`、`LyraMainGraphState`、Main TurnYaw 和反馈值。`LyraMainLocomotionHost` 创建并保存唯一 owner，随后把同一对象传给初始及替换的 Linked 实例；不再随换层分配新 Main/Lean，再从旧实例搬迁历史。

Main 的状态机、cache、统一 Sync 历史和 Montage 仍由原角色宿主持有。Linked 的 Start/Cycle/Stop/Pivot、Idle、Air、Aiming/Skeletal 等图宿主及播放器仍保持各自原归属，本批没有调整它们的采样、混合、时钟或源遍历顺序。Main 图根回调的 Start/Pivot 历史属于 Main owner；Linked Pivot 的私有字段继续属于 Linked 宿主。

每个候选帧登记当前 source scope 作为 writer，Prepare/Validate/RootYaw/Commit 都验证该身份。只有匹配 writer 的 Cancel 能清理 Main 与直接 Lean；退休 scope 的 Cancel 不会取消新实例的 Main 帧。owner 还验证资源 bank、sequence 数组及 Lean ID 空间，并拒绝另一角色认领。Main Lean 的原 epoch 保持，Linked 替换 epoch 不会重置 Main。

原 `AdoptMain` 兼容入口现在仅检查或附接同一 owner；普通生产换层已经在新实例构造时借用角色的 owner。待提交帧禁止改接；需要换用另一 owner 时，已经使用或已认领的目标不能丢弃原状态。退休发生在原绑定提交之后，原取消/重试及统一提交顺序保留。

## 验证

Debug 与 ExportRelease 构建均成功，0 错误、0 警告。两构建共 42 个最终 Godot 进程通过，无 Godot ERROR/WARNING：各十一项原绑定/三 Provider 三 Hz 换层/十角色、各九项三 Hz 三姿态边界，以及各一项独立联合 source scope。Optimize 实际加载 ExportRelease 程序集，五轮六文件 Debug 恢复经 SHA256 检查。

十八个原 Main 诊断进程合计 45360 个提交帧；原精度与逐帧取消重试均通过，记录 219762 次退休 scope 取消检查和 54 次其他角色认领拒绝。每构建独立联合 scope 执行 3780 帧，覆盖隐藏、update-only、共同 Sync、身份错误拒绝、反馈和取消重试；它保持既有受控 scope 范围，不作为新增原生整图参考。两构建十份完整普通报告相同，且分别与上一批 `layer-codegen-v3` 全报告一致。

独立 `tools/verify_lyra_main_state_owner.py` 封口通过，报告为 `artifacts/lyra-analysis/main-state-owner-v2-integrity.json`：核验当前两构建程序集、全部日志/完整报告/owner 计数、五轮恢复与 source 哈希，原 869 JSON、710 UE 包、9 项项目/配置保持。原接口模型、绑定算子、生成合同与 Main pose host 源码也保持；原生探针源/已构建包及捕获脚本哈希保持。Godot 多 owner 姿态等价、完整物理等价及目标完成仍显式为 false。

原完整 Main 诊断增加实际对象归属检查：逐帧验证 Main、Update、Lean 均为初始角色对象；真实换类之后，每个退休 Linked 实例在新帧 Prepare 后执行 Cancel，并验证当前候选仍可提交；另一角色尝试借用 owner 必须拒绝。这些检查与原骨姿态、曲线、属性、root、字段、Montage 和取消重试对照一起执行，原精度门槛保持。

本批使用已有 `rebind30-final`、`rebind60-first`、`rebind120-final` 原生参考；没有启动 UE 或重新导出。ALS 人物仍为 68 skin/69 raw/81 logical 骨布局。

首次准备后续验证时，上一个普通验证进程尚未结束，预检以 `Workspace Godot is running` 拒绝启动；未产生新的诊断运行或比较结果。等待原进程终止后顺序重启，最终上述 42 个进程均完成。初版 Debug 构建日志和最终 v2 证据分别保留。

## 当前边界

普通 Godot Main 仍执行十四入口单共享 Linked 实例。本批 owner 允许一个活动 source scope 写入 Main 帧；它尚未实现多个实际实例的统一收集、一次跨 owner Sync 或多组完整姿态执行，不能据此关闭通用 Layer 移植。

下一步将调用点路由到绑定结果中的实际实例，各实例更新通用字段并持有私有图历史，Main 直接源与所有实际访问的 Linked 源按原遍历顺序登记到角色共同批次，随后一次 Sync、各 Layer 完整输出求值、Main 曲线复制及统一提交/取消。按类查询仍需遵循原 Linked 节点首个匹配顺序，不能固定取 Cycle 实例；所有实例参加游戏线程预更新，但 worker 更新由首次实际 Linked 根访问触发并按帧去重；隐藏实例保留 worker 字段历史。

不同类/部分实现/default/self/Unlink 的多组整图、共享持久实例、复杂参数/继承及通用导出闭包保持开放。完整 UE/Jolt 物理仍有此前 314/1680 帧差异，本批受控物理输入的动画对照不关闭该门槛。没有本批 GPU、近景、复杂地形、全量 managed、十分钟、性能或独立导出验收；音频、道具物理和头颈继续暂缓，整个目标保持 active。

## 复跑

已有成功文件不可覆盖，须使用新 EvidenceTag；本批最终标签为 `main-state-owner-v2`。

```powershell
& scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Debug -EvidenceTag <新标签>
& scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Optimize -EvidenceTag <新标签>
# 30/60/120 分别使用 rebind30-final/rebind60-first/rebind120-final。
& scripts/verify-lyra-whole-main-diagnostic.ps1 -Configuration Debug -RunTag rebind60-first -EvidenceTag <新标签>-60 -Retry
& scripts/verify-lyra-whole-main-diagnostic.ps1 -Configuration Optimize -RunTag rebind60-first -EvidenceTag <新标签>-60 -Retry
# 两构建独立联合 scope 证据需按同样格式保存，随后封口：
python tools/verify_lyra_main_state_owner.py --tag <新标签>
```

Godot 验证必须顺序执行；Optimize 使用实际 ExportRelease 六个 DLL/PDB，并在 finally 中恢复 Debug 文件、检查 SHA256。独立审计检查当前程序集、构建日志、完整普通报告、原姿态边界和真实 owner 门禁，以及原 JSON、UE 包和项目配置哈希。
