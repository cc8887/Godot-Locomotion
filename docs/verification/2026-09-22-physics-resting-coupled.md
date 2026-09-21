# 未休眠帧：关节与接触共同求解原生对照

本批在主目录 `D:\GodotALS` / `main` 推进四项整链失败的定位，没有改变普通角色入口、休眠阈值或求解公式。

## 实际快照

在上一批float叉积修复后的实现上，捕获高速120Hz AnimMan第1140/1141/1142帧、普通60Hz Mannequin第540/541/542帧，共六份快照。每份包含完整身体/关节顺序、Gather后的接触行、shock层级与24个共同阶段，合计144阶段。

两个实际场景仍按原门槛失败，落地、休眠和限位诊断行与上一批逐行一致。capture日志和输入目录使用独立名称，没有覆盖矩阵日志。

沿用现有 `PhysicsCoupledInputs=` / `PhysicsCoupledOutput=` 导出，真实UE contact/cached-joint容器从捕获的输入重新计算每个阶段，输出不读取Godot的结果。原生导出两次退出0，参考文件7,593,353 bytes、字节一致，SHA256：

`938304ADCBFBBF051404950B3A612F29D6A78CD8711C51D35B72AB080B2D6876`

## 修正重放入口

第一次测试被普通接触行校验拒绝，N·U约 `-1.0485877e-5`。这是已有native-style Gather在近法向滑动速度下产生的float抵消残差；生产运行时本来就通过内部fromNativeGather路径处理，而测试错误地改走了更严格的手工接触行接口。

现将manifold初始化拆出内部 `GatherRows(..., fromNativeGather)`，只对reference测试开放程序集内部访问。公共 `Gather` 仍固定使用严格校验，生产 `GatherGeometry` 不变，接触轴不做正交化。扩展测试确认：原始Gather输出被保留、重放求解与GatherGeometry一致、公共入口仍拒绝该残差、非有限后续行失败时既有manifold不被发布覆盖。

## 结果与证据边界

四套coupled参考在 .NET 8和 .NET 9各4项全部通过，门槛未放宽。新六帧最大误差：

| 对照 | DP cm | DQ | 线速度 cm/s | 角速度 rad/s |
| --- | ---: | ---: | ---: | ---: |
| .NET 8重算对UE | 6.9675e-8 | 1.1071e-8 | 3.8481e-6 | 2.5068e-7 |
| .NET 9重算对UE | 1.1452e-7 | 1.3361e-8 | 4.1587e-6 | 5.1555e-7 |
| 原始Godot捕获对UE | 1.1452e-7 | 1.3361e-8 | 4.1587e-6 | 5.1555e-7 |

覆盖15组动态身体之间不同层级的shock接触。原生结果与Core不是逐位相等；上述结果仅说明给定相同接触行/关节输入时，这144个阶段的差异落在既有门槛内。不能据此证明持续历史、Gather输入、完整轨迹或休眠实现正确。

Core接触/共同求解相关87项通过，含新增入口约束与失败不发布断言。Godot优化构建0errors/0warnings；60Hz完整contact smoke通过（包括246组实际胶囊回放和全部旧项）。本批未重复Core/Import全量或十二项矩阵；最新全量基线仍上一批Core2834、Import2422/1旧跳过，完整矩阵仍8/12。

UE完整Editor target up-to-date并通过插件审计，fingerprint仍为 `66C1B4B900E26460EF33F8385CA4AD12A8CA10438C9D224DFB6CBDC6E2514D05`。本批未修改UE插件/配置，无新增DataValidation或打包变更要求。普通Editor PID9288加载标记成功、原生退出0、DLL无占用；两旧Condition failed仍存在，既往间歇退出访问冲突未修复。

日志、六份输入和重复参考位于 `artifacts/physics-resting-coupled-20260922/`。用户P4规划修改保持原hash。

## 下一步

当前快照仅保存Gather后的行。下一批应补录Gather前的局部接触几何、静摩擦锚点、初始穿透状态、shape-world姿态和实际速度，让UE重新生成接触行再对照，而不是继续仅把同一批已生成行喂给两个solver。

普通60/高速120/平移30/旋转30四项整链失败仍未关闭。普通Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera和十分钟预算继续保留在完整目标清单中。
