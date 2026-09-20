# 持续接触匹配与摩擦锚点

本批在主目录 `.` / `main` 新增跨帧接触历史及单个接触对的运行时 owner。
承接前批几何 Gather 与接触行；尚未接入 Godot 真实碰撞查询或普通角色 Ragdoll。

## 实现与原生依据

对照本地 UE 5.9 `PBDCollisionConstraint.cpp` 的 `CalculateSavedManifoldPointDistanceSq`、
`FindSimpleSavedManifoldPoint`、`FindSavedManifoldPoint`、`AssignSavedManifoldPoints`，
以及头文件 `SetSolverResults` 和 `PBDCollisionContainerSolver.cpp` 的结果回存循环。

`AlsContactHistory` 实现：

- 默认 SimpleAssignment 从当前点索引开始循环旧锚点，允许多个新点使用同一个旧锚点。
  非默认的一对一模式也保留，匹配后按原生 RemoveAtSwap 移除候选。
- 小于 0.2 cm 取第一个精确候选；否则找小于 1 cm 的最近候选。边界为严格小于。
- 一端是 quadratic 曲面（球/胶囊等）时仅比较该端局部点；否则取两端距离平方的较小值。
  此阶段没有额外加入法向匹配门槛，保持原生语义。
- 复用成功恢复锚点和 InitialPhi，并清除新接触标志；无匹配使用新点本身作为初始锚点。
- 摩擦比例接近 1 保持原锚点；中间值按原生 Lerp 滑动锚点；近零使用本次接触点。
  点数达到 8 时不保存近零摩擦锚点，但仍计算整流形最小 InitialPhi。
- 禁用点不匹配，但原生回存仍处理它：摩擦比例为零、保留其当前 InitialPhi，按点数决定是否保存。

同一预分配实例用 `Prepare / Commit / Abort` 管理状态。失败 Prepare 不开启 pending，
失败 Commit 不交换已提交历史；Abort 可重试同一步，Reset 需先结束 pending。
连续 2048 次 Prepare/Commit 的热路径零托管分配。

`AlsContactPairKey` 是有序的两端 shape 标识，包含 body、generation、shape index、revision。
身份变化、观察步不连续或中间提交空流形时，不复用旧锚点。
这些标识由将来的 world owner 分配，本批提供隔离契约，**没有声称已实现身体注册表**。
端点交换视为不同 key；重用 body slot 或替换 shape 时，调用方必须更新 generation/revision。
睡眠岛保留历史的生命周期策略尚未接入；当前步编号用于连续的清醒接触观测。

`AlsPersistentContactPair` 将历史匹配、几何 Gather、接触行与结果回存组合起来。
禁用点在求解前压缩掉，并保存源索引；提交时恢复与原始流形的映射。
世界调用方需在物理步成功后协调 Commit，失败则 Abort；仅执行求解不会提前保存历史。
该封装不包含世界级多对象提交协调、碰撞检测、睡眠或重力。

## 原生参考

新离线入口：`PhysicsContactHistoryOutput=<新绝对文件>`。
调用真实原生 `Activate`（内部匹配旧点）与 `SetSolverResults`，不另写预期匹配算法。
导出文件 `assets/config/v4_physics_contact_history_reference.json`，1,823,937 字节。
使用紧凑 JSON，未格式化或重写其他已有原生参考。

192 组 = 两种匹配策略 × 四种 quadratic 组合 × 1/4/8 点 × 八种场景。
每组六帧：连续接触三帧、一帧空流形、重新接触两帧，共 1152 帧。
场景包含保持、滑动、零摩擦、摩擦阈值附近、接触重排、曲面旋转式局部位移、重复点/禁用点、
关闭摩擦恢复。非默认 CVar 仅在该导出进程中临时设置，并用 scope exit 恢复值与设置优先级。

此参考针对 **历史阶段**：摩擦比例与更新后的 InitialPhi 是显式供给的阶段输入，
不包含原生窄相或完整动态世界积分，不能当作连续落地轨迹等价证明。
Import 回放每帧用 Core 自己保存的历史；预期锚点仅用于比较，不会回灌下一帧。

两次冷导出退出 0，输出 `ALS_PHYSICS_CONTACT_HISTORY_OK cases=192 frames=1152 assets_saved=0`。
文件字节一致，SHA256：`516EFBF5593590E18FC6CCF535509DF756C8AFD1DC9B222E9E62ABE876A6D5E4`。
192 组全部通过：1715 次锚点恢复、2445 次新接触，锚点最大差值 **0**；InitialPhi、
新接触/恢复标志、保存点数量及流形最小 InitialPhi 均一致。
这是当前样本和平台的结果，不外推为完整物理世界逐位一致。

## 运行时语义验证

新增 Core 14 项测试，覆盖精确优先/近似边界、simple 与唯一匹配区别、quadratic 四组合、
锚点保持/滑动/无摩擦、大流形压缩但保留全局深度、身份代次/形状修订/顺序/空帧失效、
禁用点、失败回滚/同一步重试、零分配。
其中两项使用实际 `AlsPersistentContactPair` 经 Gather 和八轮位置/两轮速度求解，
检查提交前后历史、禁用点索引映射及失败 Gather 后恢复。聚焦 14 项通过。

Release 全量固定 JIT 回归：Core **2669 通过**（既有 P5A Golden/TraceSchema 过滤），
Import **2359 通过、1 既有条件跳过**（普通 Editor LayerBlending 重复导出环境变量）。
Godot 优化构建 0 warning / 0 error。本批未改变普通入口，未重复运行其画面或旧落地探针。

## UE 插件验证

使用 `ue-diagnosing-plugin-build-load` 完整构建 Editor 目标、插件闭包审计，再冷启动导出。
BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`，输入 fingerprint
`D94963CE02AACECE6AC2F8C1CB5F21B04DA157C6440B7F215D73F219775EAB56`。
构建日志前缀：UE `Saved/Logs/PluginBuild/20260920T210820264Z-87c6316236d045fc81cb9a24d296b530-*`。
主仓库与 UE 项目本地导出插件源码一致，未修改引擎或复制 DLL。
技能引用的两项 superpowers 技能本机不可用，采用直接源码、构建审计和实际验证记录。

首次普通 Editor 冷重启成功加载 exporter、输出专用标记，但进程在日志关闭后返回
`-1073741819`（访问冲突）。日志没有对应调用栈；项目 Saved/Crashes 无新记录，
应用事件日志与本地 CrashDumps 未找到这次故障。保留 `editor-restart.log`，原因未定位。
相同参数独立复跑 `editor-restart-repeat.log` 输出 `ALS_CONTACT_HISTORY_EDITOR_RESTART_OK`
且退出 0。本批没有执行新的导出函数于该启动检查，不能据此归因或宣称修复退出崩溃。
两次启动仍有既有 UnifiedErrorTest/Condition failed 与引擎资源缺失提示，不称无错误日志启动。
DataValidation 退出 0，0 error / 3 旧 warning（AI 组件和导航版本）。本批仅改 Editor
导出工具，无适用的仓库打包要求，未执行打包。原有接触行与 Gather 参考文件字节未变。

产物在 `artifacts/physics-contact-history-20260921/`。

## 继续推进顺序

接下来接世界侧稳定标识/碰撞过滤、真实形状查询与多个接触对在同一身体缓冲中求解。
再加重力/外力、动态对象双向响应、运动 kinematic、连续碰撞与睡眠/唤醒。
完整落地/高速/多频率验收通过后，接普通角色 Ragdoll、Get-up 和 Pose Recovery。
Mantle、完整 Camera、十分钟性能预算等总清单仍未完成。

普通 demo 本批未改变，旧 Jolt 落地失败未关闭，无画面观感验收。
继续原批准的 C# Core 架构，UE 仅离线参考，无引擎 fork 或 UE 运行时依赖。
用户 P4 规划保留原字节与未提交状态，不纳入本批提交。
