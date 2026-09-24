# Ragdoll 普通退出与默认 Get-up

## 本批结果

在主目录 `D:/GodotALS` 的 `main` 接通普通 Demo 的 G 切换：进入物理 Ragdoll，再按 G 根据骨盆朝向退出，落地时播放 Default Front/Back Get-up，空中则继承骨盆速度继续下落。恢复碰撞、角色朝向、mesh 位置以及物理快照后，使用已有唯一 Montage bank 和 Root Motion 管线播放起身；原生 GettingUp Notify 窗口结束后解除移动输入限制。G 可以再次打断起身。

用户要求暂缓的头颈拉伸诊断、道具物理附件未修改。用户 P4 规划文件保留，SHA256 为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 原生依据及资产

- 本机 ALS `AlsCharacter_Actions.cpp` 的 `StopRagdollingImplementation`：保存最终物理姿态、按 pelvis 朝向恢复 actor、恢复胶囊和 mesh；落地播放正反面起身，空中继承 pelvis velocity。
- `AlsCharacter.cpp` 的 `NotifyLocomotionActionChanged`：动作清空时解除输入限制。
- UE `AnimMontage.cpp`：实际推进速率为实例 PlayRate × Montage RateScale。两个默认起身 Montage 的 RateScale 为 `1.2000000476837158`，长度 1.5 秒，BlendIn 0，BlendOut 约 0.3 秒。
- 新 `tools/unreal/export_get_up_inputs.py` 只读导出 Roll 与 Default Front/Back 的 Montage 生命周期、实际 Skeleton slot group、Sequence/Montage Notify，不保存 UE 资产。
- 新 `p5_get_up_actions.json` 通过 `AlsRecoveryActionProfileCompiler` 扩展冻结的 P5A profile，保留 Roll ID 0，新增 Front 1 / Back 2。历史 profile 与旧参考导出不改写。
- 新 recovery Montage/Notify/source index 供生产加载；原生 source 请求包含 78 项、导出 1752 姿态，只新增两个 Get-up raw sequence，已有 raw sequence 在导出时启用字节一致保护。

两次独立 Montage/Notify 导出分别在 `artifacts/get-up-native-first`、`artifacts/get-up-native-repeat`，逐字节一致：

| 内容 | SHA256 |
| --- | --- |
| Montage | E21A131FADEC10EDD79E8EFBFABCB00D9C50C8A7B38742BB904AD3E3A7789E25 |
| Notify | E47A30B516ADC8D900CDE852D3AE8B31183C40B1C8C6C891A08FF98DBEF88D15 |

生产导出日志 `get-up-native-production.log` 与 raw 导出 `get-up-source-export.log` 均成功。没有独立重复新 raw sequence 导出，不将其计为冷导确定性验证。

按 UE 构建/加载 skill，启动 commandlet 前执行完整 Editor target 构建及全部项目插件审计。初次失败是系统没有 .NET 10；改用引擎自带 DOTNET_ROOT 后通过，无需改插件或系统安装。成功审计日志前缀：`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260924T034544209Z-46a65a279cf6420083379acab08179d9`，fingerprint `1CFC5657A618E30F849577D7EA003062B54DB04B41EF728389B91D449D0DF5E3`。本批未运行新 DataValidation/普通 Editor 重启验收。

## 生命周期变化

1. Main Lifecycle 在已提交动画和已完成物理步边界消费退出请求；先准备面向决定和按恢复后 Skeleton world 重基的不可变快照。
2. 安装 mesh/snapshot，恢复 motor 与胶囊，释放物理 owner，重置身体历史。快照保留供动画图的退出混合使用，不改写独立 Flail 源。
3. 地面请求进入现有 Action/Montage/Notify 管线；动画失败重试沿用同一请求，不重复播放、不重复移动胶囊。空中不发起 Get-up。
4. Notify 结束或动作终止解除输入限制。重新进入 Ragdoll 清除 Get-up 请求和限制。

另外修复物理显示步号必须按 activation 区分的问题，允许同一角色第二次进入从物理步 1 开始；Roll gameplay 只认 Roll definition，避免把 Get-up 误当翻滚。旧测试选择 Roll 时也改为明确 ID，不再假设整个资产库只有一个 Montage。

## 验证

| 验证 | 结果与证据 |
| --- | --- |
| Optimize 构建 | 0 warning / 0 error |
| Core Release，既定过滤及串行参数 | 2914 通过，`artifacts/get-up-tests/core.trx`；仍排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests |
| Import Release 全量 | 2486 通过、1 既有 NormalEditor 测试跳过，`artifacts/get-up-tests/import.trx` |
| Single60 首次完整循环 | 两次地面起身通过，`get-up-cycle-first60.log` |
| Parallel120 故障重试、起身打断、空中退出 | 两次完整起身、3 Accepted / 2 Completed / 1 Interrupted；首次退出后 BeforePublish 注入 1 次故障并恢复，`get-up-cycle-air120.log` |
| Parallel60 实际渲染 | 两次起身 + 空中退出，0 错误，6 截图，`get-up-rendered60.log` / `get-up-captures` |
| Single30 背面实际渲染 | 先受控仰卧 Back，再自然 Front，两次完成 + 空中退出，0 错误，6 截图，`get-up-back-rendered30.log` / `get-up-back-captures` |
| 原有 Montage 根运动 | 11 cases / 2540 frames / 2540 retries，最大位置误差 3.46452e-6 m，旋转误差 0，`get-up-roll-root-motion.log` |
| 原有普通翻滚/故障恢复 | Parallel，2 次注入，恢复通过，accepted=2 interrupted=1 end=1，`get-up-roll-recovery.log` |

正反面截图均检查 01、03、05、06：趴地/仰卧 → 撑起/坐起 → 跪姿 → 恢复移动。Back 测试在退出前于空闲边界刚体旋转整个物理岛到仰卧，验证分支及恢复，并非声称自然跌倒会产生该姿态，也不是 UE 整段轨迹 oracle。

新场景 `scenes/tests/character_ragdoll_recovery_smoke.tscn` 支持 `--single`、`--hz=30|60|120`、`--failure`、`--interrupt`、`--back`、`--capture-dir=res://...`。空中用例检查没有 Get-up、继承完整速度，后续帧保持 InAir 且继续受重力影响。故障日志中的 worker_evaluate 属于预期注入。

过程中的初次 .NET 缺失、source request 第四个旧 Notify 路径遗漏已修复，失败日志保留；不以失败日志替代最终通过结果。

## 仍待完成

- Overlay 专用 2H/LH/RH 起身变体尚未绑定，本批仅 Default Front/Back。
- 高落差、翻滚离地等自动 Ragdoll 触发尚未接通。
- Get-up 中停用/恢复、角色替换等专门生命周期用例尚未覆盖；普通翻滚的旧生命周期回归不等于起身覆盖。
- 静态物理稳定性旧矩阵仍为 9/12，Flail 稳定性仍为 0/3；本批未重新跑矩阵或改变门槛。
- 旧 UE 间歇退出访问冲突和 Condition 警告未解决。
- 头颈拉伸与道具物理按用户要求暂缓；Mantle、完整 Camera、十分钟性能预算以及其他原计划目标继续保留。

这批建立普通手动 Ragdoll/Get-up 的可运行闭环，不等于完整 ALS、联网时序或 UE/Godot 逐帧 1:1 等价验收。
