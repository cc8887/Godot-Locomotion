# 原生 polygon 查询接入实验物理链路

本批直接在 `.` 的 `main` 实现。普通角色 demo 尚未切换到实验后端，不能将此记录视为 Ragdoll 或完整 ALS 验收。

## 实现

- `AlsPolygonQueryCache` 保存按 registry 原始有序 shape slot 索引的 GJK 缓存。body generation、shape revision 或双方解析后 margin 变化会冷启动；几何和缩放变化必须推进 revision。
- 查询结果随 geometry transaction 暂存、提交或回滚。查询执行失败后拒绝继续查询及提交，直到 Abort；支持显式 Release、Reset 和诊断复制。省略查询不等于销毁约束，恢复流形而未调用 Query 时也保留缓存。
- `AlsGodotContactQuery` 保留绑定的 cooked 凸包拓扑、原始盒体尺寸和凸包缩放，在双方都有原生 polygon 绑定时调用 Core GJK/EPA 和面流形算法。原始 pair 顺序不再被旧面查询适配器交换。无事务的直接诊断查询使用独立冷缓存。
- 精确单位缩放使用未缩放凸包几何分支；其他缩放使用 scaled 分支。UE `ChaosInterfaceUtils.cpp` 的 CreateGeometry 在 NetScale 精确等于 One 时创建 instanced wrapper，其几何方法转发内部凸包。
- 资产身体、地板和场景盒体绑定接入此路径；整链报告增加原生查询和缓存计数。普通 60 Hz 落地报告记录 45188 次原生 polygon 查询及 133 个缓存对，证明并非仅测试独立函数。

## 验证

日志目录：`artifacts/physics-native-query-20260921/`。

- Release、固定 JIT、串行 Core 回归 2806 通过；沿用排除 P5A golden/schema 的既有过滤范围。最终日志 `core-final.log`。
- Import 定向参考回归 4 通过：覆盖 12960 组 raw/scaled/box 流形及 528 帧 GJK/EPA 参考；不是 Import 全量重跑。
- Godot 优化构建通过，0 警告、0 错误。
- 30/60/120 Hz 实际接触场景均通过：每频率 551 精度、9 几何、5 流形、5 睡眠、3 事务和 4 原生 polygon 检查。新增真实脚凸包的正反传输、旋转和大坐标检查；原生绑定不调用 Jolt 查询。
- 60 Hz 场景世界检查通过，覆盖 13 个环境身体/形状、13 几何及 9 生命周期检查。

十二项整链以 `instanced-*` 日志和成功报告为准，最终 **8/12**，较此前 9/12 新增 30 Hz 平移平台失败。未调宽休眠时间或速度门槛。

| 频率 | 普通落地 | 高速落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 Hz | 失败：Mannequin 未休眠 | 通过 | 失败：停止后 AnimMan 未休眠 | 失败：平台启动前 Mannequin 未休眠 |
| 60 Hz | 通过 | 通过 | 通过 | 通过 |
| 120 Hz | 通过 | 失败：Mannequin 休眠太迟，未保持一秒 | 通过 | 通过 |

30 Hz 普通落地最终最大线速度 4.067823 cm/s；30 Hz 平移平台最终 1.365823 cm/s。120 Hz 高速落地 Mannequin 在第 1173 帧休眠，到第 1200 帧不足一秒。30 Hz 旋转平台相比前批提前到启动前失败，不能仅描述为原有失败未变。

矩阵之后增加了失败查询的事务防护与诊断覆盖；最终 Core 和三个频率 smoke 已重跑。该防护不改变成功查询的几何计算，矩阵未重复执行。本批无 UE 插件修改、构建或重新导出；既有普通 Editor 退出访问冲突仍未修复。

## 尚未完成与下一步

1. 导出并绑定实际运行时 UE shape wrapper 类型、缩放和 margin。当前 Godot 查询代理明确使用零 margin；cooked 内层凸包 margin 为零，不证明外层 wrapper 的 margin 为零。UE CreateGeometry 还结合缩放后的尺寸、solver 参数和 CVar 计算 margin，不能用默认值冒充项目观察值。
2. 补齐非零 margin 的 FConvex SupportCore，并接上已有双方运动状态相关的 pair margin 解析。
3. 接入原生 detector 分离检测距离及上帧速度扩展；当前查询仍是 cull=0，流形容差仍从 Godot float bounds 计算，尚非完整原生参数传输。
4. 按 native particle midphase 生命周期接入缓存 Release/退役策略；当前省略查询会保留缓存，仅显式销毁、重置和身份替换控制失效。不能把 ResetManifold 当作 GJK cache reset。
5. 在这些缺口补齐后重新跑十二项整链并定位四项休眠失败；尚不能断言 margin/cull 就是全部根因。随后继续普通 Ragdoll、Get-up、Pose Recovery、Mantle、完整 Camera 与十分钟性能预算。

用户既有 `2026-08-28-p4-aim-layering-foot-placement.md` 修改保持不动，不纳入本批提交。
