# 第八批：Locomotion Detail 源合同与规则选择

日期：2026-09-10。工作区：`ARCHIVED_P5A_WORKTREE_PATH`。
范围：完整性补完 A。源数据和规则组件已实现，**可玩 Demo 尚未接入 Detail**。

## 原生依据

扩展 `AlsStopGraphCommandlet` 的可选 `-IncludeDetail`，默认 Stop 导出范围不变。
只读导出 Detail、BaseLayer、移动参数更新及相关 EventGraph；保留状态/播放器默认值、
输入引脚、函数/状态引用和枚举映射。同时直接读取 GeneratedClass 的 BakedStateMachines，
获得 Detail、Locomotion States、Stop States 和 Main Grounded States 的真实编译后表。

遵循 `ue-diagnosing-plugin-build-load`：完整项目 Editor target 构建及插件审计通过后，
才冷启动只读 commandlet。没有复制 DLL、保存 UE 资产或改动引擎源码。
最终构建 fingerprint：`8499C4D5F30D467DE453F7CF4982EEF9A73ADCA7778ED2F62A3ACE00763C54F7`。
日志：`artifacts/locomotion-detail-baked-native-20260910.log`，退出码 0，0 errors / 0 warnings。

```text
ALS_STOP_GRAPH_OK graphs=51 plant_evaluators=12 assets_saved=0
ALS_DETAIL_GRAPH_OK players=16 assets_saved=0
```

源文件：`assets/config/v4_locomotion_detail_graph.json`，1,583,648 bytes。
SHA256：`9032C2CEEC40E364D619E1060E659C2D9EBABC2FB9787F46E2C67178EA07920E`。
以上为第八批原始 UTF-16 文件的大小和哈希。第九批 Godot 实测发现编码不兼容，
已从 UE 重新导出为无 BOM UTF-8，解码后文本逐字符一致；当前文件哈希与修复记录
见 `2026-09-10-detail-additive-runtime.md`，原始版本保存在 artifacts 中。
这是图配置与编译后表的原生观测，不是完整 AnimBP 的多帧姿势对照。
未执行 GUI 重启、数据验证或打包，不能作为插件发布验收。

## 已实现

- `AlsLocomotionDetailCompiler`：检查六个内容状态和一个 Conduit、初始 Walking、
  每帧最多一个转换、不跳过首帧转换、重新相关时初始化、状态不强制每次进入重置。
- 从实际引脚读取播放器起始时间和速率，保留 16 个 SourceNode 身份；校验四向
  VelocityBlend、Cycles 缓存、加法配置、基准动画、SyncGroup 与非循环播放策略。
- 结构化读取规则表达式，检查精确比较符、阈值、枚举和 Getter 绑定的状态/状态机；
  对照编译后边的目标、时长、类型、混合模式和事件设置，并保留其真实退出顺序。
- `AlsLocomotionDetailRules`：纯规则选择，无内部时钟或提交副作用。返回入口边与
  Conduit 末端边，使用末端边时长，不把两个时长相加；区分普通与惯性化过渡。
- 拒绝额外布尔操作数、动态播放时间引脚、错误 SyncGroup、非恒等 Scale/Bias、
  Clamp/LOD 门控、遗漏/重复源和不支持的状态/过渡事件。

原生 GetCurveValue 的 FunctionReference 使用 bSelfContext，memberParent 为空。
首轮加严校验误要求显式 AnimInstance 父类，导致 8 项有效合同测试失败；已依据真实
导出修正为自身上下文、空 self 引脚与 Weight_Gait 曲线，并新增错误目标/上下文反例。

## 关键语义

Walking/Running 直接使用 Cycles；其余四个内容状态在 Cycles 上叠加四向
`ALS_N_LocoDetail_Accel_F/B/L/R`，基准为 `ALS_N_Run_BasePose` 第 0 帧。

| 状态 | StartPosition 秒 | PlayRate | 同步组 |
| --- | --- | --- | --- |
| Walk->Run | 0.10 | 1.25 | Run Start |
| Run Start | 0.15 | 1.25 | Run Start |
| First Pivot | 0.25 | 1.25 | Pivot 1 |
| Second Pivot | 0.25 | 1.25 | Pivot 2 |

四个资源不等于四个播放器：各状态独立实例，共 16 个；共享同步组也不能合并身份。
资源已在资产清单中，但当前 Demo 的库闭包与采样路径尚未接入它们。

12 条边中 7 条使用 Inertialization。BaseLayer 下游有共享 Inertialization 节点，
不能把 Detail 的这些边当普通交叉混合，也不能只在分支内部做一个平滑系数。

编译后退出优先级与部分编辑器同优先级连线顺序不同：

- Running 先 First Pivot，后 Walking。
- First Pivot 先 Second Pivot，后 Running。
- Second Pivot 先 Running，后 First Pivot。
- Walking 先 Running，后 Run Conduit；Conduit 先 Run Start，后 Walk->Run。

Run Start/Walk->Run 的选择使用 Main Grounded 与 Detail 两个状态机的实际权重。
相关动画剩余时间条件为 == 0；Pivot 交换的 elapsed 条件为严格 > 0.1。
EventGraph 的 AnimNotify_Pivot 另有 Speed < TriggerPivotSpeedLimit 判断和普通 Delay
0.1 秒后清零。该事件链尚未实现，速度阈值也未作为已确认常量写入运行时。
事件延迟、状态门控和方向过渡时长是三种不同语义，不能合并为统一换向冷却。

## 验证

- Import 全量 580/580；其中新增 Detail 测试 39 项，含 31 种损坏/不支持配置反例。
- 规则测试覆盖原生退出顺序冲突、两级机器权重、Conduit 末端时长、严格边界、
  16 个独立源、加法基准与惯性化类型。1000 次预热后规则选择为 0 B 托管分配。
- Core Locomotion 319/319；不把这个历史组件集合计为新的 Detail 轨迹验收。
- Godot 项目构建：0 warnings / 0 errors。
- Stop Plant 与 Standing Cycle headless 回归通过：

```text
STOP_PLANT_OK rates=30,60,120 fixed_sources=12 sample_components=4608 bone_checks=2448 selectors=246 curves=72 alloc=0B outer_state_machine=not_connected
STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions=9 wait_frames=231 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603
```

回归日志：`artifacts/detail-contract-stop-regression-20260910.log` 和
`artifacts/detail-contract-cycle-regression-20260910.log`。它们只验证已有组件未回归，
不是 Detail 的 Godot 运行验收。没有重新做移动截图、全量 Core/P5A 或 P7 十分钟采样。
已核对 Camera/Input 文件哈希未变。

## 未完成与下一步

Detail 状态时钟、相关性/重置、实际加法采样、惯性化请求/历史姿势求值均未接入；
ShouldMove 的真实输入、NotMoving/Moving/Stop 外层链和 Pivot 事件也仍未完成。
当前编译器不执行 EventGraph、不提供 Sync 时钟，也不构成完整 UE 状态机解释器。

下一批先补加法姿势及下游惯性化运行时，再接源时钟、状态相关性与外层起停；
身份、Sync、曲线和事件统一复用 P5A 事务，而非在 Demo 中永久另建一套调度器。
随后完成 P4 动态 Layering，再进入原定 P5B/P5C/P6/P7。
本批没有改变 Demo 移动输出，不能声称起步滑步或换髋观感已改善。
