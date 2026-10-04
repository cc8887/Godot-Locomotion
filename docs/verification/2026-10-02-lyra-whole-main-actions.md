# Lyra 完整 Main 动作连续对照

本批沿用 ALS 68 skin / 69 raw / 81 logical 与十四入口共享 `ItemAnimLayers` 实例。目标为原 Main、Linked 图、物理 Montage bank、主惯性与最终 FootPlant 的联合连续对照；整个迁移目标保持 active。

## 实现与原生边界

`capture_lyra_whole_main.py` 新增 actions 轨迹。原四十五个 Montage 复制为 transient，只替换其 Sequence 为既有 ALS81 重定向结果，BlendProfile 按目标骨名适配；原轨道、区段、混合设置及 Notify 保留。Main 的真实 Update/PostUpdate 冻结 Montage 数据，并由原五个 Slot 在完整图内求值，没有第二次 Montage tick。命令在本帧求值与 Notify dispatch 后执行。

三个固定 Provider 使用既有十二秒物理观察：0.5秒装备/通用卸装、1秒开火、1.4秒受击、2秒换弹、2.3秒开火打断换弹、3秒近战、3.15秒受击替换、4秒 Dash、4.3秒受击、5秒 Emote、6秒停止 Emote、7秒开火、8秒换弹、9秒停止换弹。Pistol/Rifle 使用各自原动作；Unarmed 使用原通用卸装及 Pistol 动作，检验跨 Provider 调用，并不表示无武器角色具备这些玩法。

新 `scripts/capture-lyra-whole-main.ps1` 校验真实包源码、拒绝已有进程/证据覆盖、保留原环境变量，并保持 AnimationData 可加载。原 Emote 是 Sequencer 数据模型，禁用该模块会在原资产加载时断言。夹具的 transient Character 补真实 AbilitySystemComponent，以合法接收原近战 GameplayEvent；没有安装 GameplayAbility 或装备对象。原 `AN_PlayWeaponMontage` 的装备查找告警仍保留，不能把本批叫做 GA/ASC/装备玩法验收。

Godot 只消费 authored 物理观察及独立 Montage 命令。每帧比较冻结实例顺序、动作身份、position/weight/previous/delta 的 float bits 和 Profile 存在性，真实轨道采样继续使用已有运行时。原 Main 三个边界分别比较完整骨骼姿态、曲线存在性/值 bits/flags、typed 属性和 RootMotion。原十个 Linked 函数输出单独检查，不使用其后的 Main Lean 结果替代。每帧取消后在同一身份重试，完整姿态/曲线/属性/RootMotion/Montage 候选及冻结 bank 必须精确相同，命令不得改变本帧冻结输出。

## 查询语义修正

原 Main `UpdateBlendWeightData` 的 PropertyAccess 调用 `UAnimInstance::IsAnyMontagePlaying`。本机 UE5.8 `AnimInstance.cpp` 该函数直接检查 `MontageInstances.Num()>0`，与 `Montage_IsPlaying(nullptr)` 的 active/playing 检查不同。停止/暂停但尚未移除的实例仍计入原查询。

Core 增加候选实例存在性查询 `IsAnyMontagePlaying`；生产 `LyraCharacterAnimation` 使用该查询，取消重试不发布查询历史。原 `IsActionPlaying` 的 active asset lookup 语义保持。首次诊断忘记向 Main 提供自主 bank 的观察，在 Unarmed/60Hz/frame31 失败；补正确观察后，错误使用 active/playing 查询在 frame46 的停止淡出边界失败。最终修正遵循上述原函数，而非放宽姿态门槛或读取 native weight/clock 驱动运行时。

## 验证与证据

最终 `artifacts/lyra-analysis/whole-main-actions-final-integrity.json` 已生成并通过审计。三 Provider × 三 Hz，每条十二秒，统一300序列和45 Montage 库；每种构建7560个参考帧，在惯性前、Rig前、最终输出三个独立运行边界全部通过，每帧都取消重试。Debug 和实际 ExportRelease Optimize 合计18个边界对照进程。

| Hz | 三 Provider 帧 | 命令 | 冻结实例记录 | 全身覆盖帧 | 重叠帧 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 30 | 1080 | 42 | 862 | 129 | 209 |
| 60 | 2160 | 42 | 1753 | 249 | 433 |
| 120 | 4320 | 42 | 3533 | 489 | 881 |

以上计数在两种构建、三个边界精确一致。姿态门槛保持位置 `1e-8 cm`、归一化 quaternion 差 `1e-10`、scale差 `1e-12`；曲线值/flags及Montage时钟/权重按原float bits比较，完整typed属性及RootMotion亦比较，没有放宽。

两构建各七项回归通过：Aiming 7560帧、Aiming共同scope 11340帧、Lean合成2100帧、Main Linked/Additives 11340帧、完整惯性宿主40320帧、Montage通知33235帧/45资产/27重绑/32695重试，以及普通十角色实际Godot物理、武器、换层和晚期取消。十角色每角色480移动及发布帧，两构建完整JSON报告精确相同。合计32个最终Godot进程均退出0、无ERROR/WARNING。

最终Debug/Optimize构建均0错误0警告。Optimize各轮六个Debug DLL/PDB恢复哈希校验通过；Win64数学DLL的资产、Debug和ExportRelease三处SHA相同。三成功UE采集退出0、0资产保存；863旧JSON、709原包、9项目/配置文件逐文件SHA保持。审计记录六份验证报告、三份采集、相关日志、当前十四个源文件hash，并保持 `completeAcceptance=false / goalComplete=false`。

失败证据保留：`package-actions` 的首次编译失败；禁用 AnimationData 的 `actions60-fixed` 退出3；无 ASC 的 `actions60-model` 完成捕获但因原近战 Notify 报 Error 而退出1；`actions60-first` 与 `actions60-playing` 的三边界失败日志。补 ASC 后 `actions60-asc` 退出0，其完整 native 结构与此前已完成的 `actions60-model` 精确相同，未通过进程不作为正常退出验收。

复现入口如下，新的RunTag/EvidenceTag不能覆盖既有证据；首次需要先构建匹配源码的独立探针包：

```powershell
./scripts/build-lyra-whole-main-oracle.ps1 -EngineRoot '../UE_5.8' -UnrealProject ../GASP58/GASP58.uproject -PackageName local-actions
./scripts/capture-lyra-whole-main.ps1 -RunTag local-actions60 -PackageName local-actions -Hz 60
./scripts/verify-lyra-whole-main-diagnostic.ps1 -Configuration Debug -RunTag local-actions60 -EvidenceTag local-actions60 -Retry -IncludeRegressions
```

运行Godot对照前按README构建Debug；Optimize需要实际ExportRelease Optimize产物。完整本批审计入口是 `python tools/verify_lyra_whole_main_actions.py`，成功审计输出不可覆盖；它检查本批固定标签的真实完成结果，不代替新的对照运行。

## 剩余范围

本批物理输入仍是受控观察，UE 为 GamePreview 静态地面，Godot 为解析平面。没有 UE CharacterMovement 真实移动、完整动作期间的换层联合 native、Jolt/GPU/近景握持/复杂地形/材质/性能或独立游戏导出验收。固定 Unarmed/Pistol/Rifle 动作组合不等于全部四十五 Montage 与全部 Provider 的联合验收。多 Group、self-layer、Unlink、Shotgun/Feminine 等既有开放项与用户暂缓项保持。
