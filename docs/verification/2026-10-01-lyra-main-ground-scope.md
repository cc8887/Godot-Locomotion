# Lyra 四个地面根共用 Main / Source Scope

2026-10-01，主目录实施。使用 ALS 原 68 skin / 81 logical 和现有导出资源。此批完成四根共同宿主的代码与运行验证；四根同时遍历的全新 UE oracle、完整 Locomotion 状态机和普通 Demo 仍开放。

## 实现

`LyraMainSourceScope` 扩展原三根宿主，接入实际 Pivot 根，不增加第二套角色调度器或 Main 时钟。四根保留各自源、HipFire、Warp 和 Lean 出现位置，按调用者提供的实际遍历顺序登记到一次共同 Sync。全部依赖预校验后一起提交，取消不发布 Main、层时钟、RootYaw 模式、方向锁存、Pivot timer 或 Warp 历史。

| 原 Main 路径 | 当前内部根 ID |
| --- | --- |
| Pivot StateResult20 → ApplyAdditive23 → Linked21 / Lean22 | 0 |
| Cycle StateResult14 → ApplyAdditive17 → Linked15 / Lean16 | 1 |
| Start StateResult10 → ApplyAdditive13 → Linked11 / Lean12 | 2 |
| Stop StateResult18 → Linked19 | 3 |

内部根 ID 不等于 UE 状态 ID。当前宏观状态/上一权重仍由外部输入，宿主尚不负责完整原生 Locomotion 状态选择和最终状态混合。

独立 `LyraMainPivotHost` 与共同宿主复用 `PrepareObserved`：消费同一个已准备的 Main candidate，保留原根相关性、初始方向锁存、正 timer 递减、源回写 timer，以及原方向/真实物理预测/组件变换绑定。所有 Lean 都消费这次 Main 更新。

新增 `LyraMainGroundResources`，合并 Start/Cycle/Stop/Pivot 的 150 个全局序列地址与三个 Lean 定义，重新绑定各距离 codec、源 occurrence 和压缩根。每次 `Create` 产生独立可变宿主和历史，可配置 player base / Lean base / epoch。资源定义共用；调用者仍需为每个角色持有自己的 Sync 历史。

重叠资源采用上一批实际瞬态 Pivot 清单中的 Marker 顺序，既有 JSON 不改字节。完整 native 大文件仅在构造期间用于提取清单，随后释放。当前资源装配来自既有三根及独立 Pivot 导出，不能据此宣称新的四根 UE 整链清单已验收。

成功求值后再发生 Sync 求值失败，会立即使该根输出失效，禁止旧姿态支持晚期提交；过期 candidate 先拒绝，不破坏另一个当前 candidate。

## 联合运行发现的 Sync 缺口

Stop 在隐藏后选择另一资源，仍可能持有旧 Marker 索引。安装版 UE 5.8 的 `Engine/Private/AnimationAsset.cpp`，`FAnimGroupInstance::Prepare` 明确执行：不使用 Marker 的命名组清空记录；带 Marker 的命名组在源未参与上一帧时清空记录。原 Godot 校验先以新轨道长度拒绝这些记录，导致合法重新加入不能执行。

修正该校验边界。独立源没有命名组 Prepare，仍拒绝无效索引；非有限数据仍拒绝；存在上一帧 occurrence 的资产切换仍要求精确匹配原历史记录。两项回归覆盖有/无 Marker 的命名组重新加入、独立源拒绝和失败不发布。

Pistol Cycle 的 Marker 名称集合也可能仅为一个脚标记，与 Start/Pivot 的双脚集合不同。原 Core 将不同完整集合视为不支持，而 UE Prepare 对各活跃源的名称求交集。此批补齐交集，并在遍历中跳过非共享标记；内部过滤后的索引转换回原轨道索引，保留源/样本历史身份。无交集时沿原长度同步路径清空记录。

新增回归覆盖正反向共享名称、原始索引、无交集回退及跨循环历史；2,000 次循环热路径无分配。既有单源 Marker mask 必须与自身轨道一致、坏历史、phase mode、容量/晚期失败等门禁保持通过。这些新边界依据本机 UE 源码与受控测试，尚无新增 UE 连续交集 oracle。

## 最终验证

Godot 4.7.2 Mono，实际 headless 场景；三 Provider 为 Unarmed/Pistol/Rifle，三 Hz 为 30/60/120。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 四根宿主内重放实际 UE Main Pivot，其他三根隐藏 | 3,780 帧、3,528 姿态、285,768 骨；曲线/属性/RootMotion、根方向/timer、取消及晚期失败；3 个合法空序列无 tick 保留 | `artifacts/lyra-analysis/four-root-smoke-final.log` |
| 原生 Pivot 姿态误差 | max position `1.1435103132435445e-13 cm`，quaternion `9.586729210015212e-16`，scale `0`；门槛仍为 `1e-8 cm / 1e-10 / 1e-12` | 同上 |
| 原生 Pivot Lean 时钟 | 3,780 检查 / 7,722 样本，逐位相同 | 同上 |
| 四根同时运行、受控外部遍历 | 3,780 帧、8,400 姿态对照、1,680 四根同时激活帧、420 全隐藏帧、24 种顺序、150 资产；每帧 prepare 和晚期 cancel/retry，13,860 坏操作拒绝 | 同上 |
| 旧三根真实 UE 回归 | 3,780 帧 / 9,105 姿态 / 737,505 骨；状态根回调、RootYaw 与 Start 锁存保持通过 | `four-root-regression-main-state-history.log` |
| 独立真实 UE Main Pivot 回归 | 3,780 帧 / 7,056 两阶段姿态 / 571,536 骨，85,860 坏操作拒绝 | `four-root-regression-main-pivot.log` |
| 相关 Core Release | 76 通过，0 失败，0 跳过 | `four-root-core-final.trx` |
| 最终 Debug / ExportRelease Optimize | 各 0 错误、0 警告 | `four-root-final-debug-build.log` / `four-root-final-optimize-build.log` |
| 只读资源保护 | 508 UE 包、619 之前 JSON 加 Main Pivot 两份 JSON，共 621 份既有文件，SHA256 保持 | `four-root-final-verification.json` |

整体验证：`python tools/verify_lyra_main_ground_scope.py`，输出 `LYRA_MAIN_GROUND_SCOPE_FINAL_VERIFIED`。

### 失败保留

首次编译误用不存在的 `TargetPath`，随后修正。联合夹具最初将帧号误设为从 1 开始，并按引用比较两个独立 Lean 数组，已改为从 0 开始和内容快照。随后旧 Sync 对重新加入/名称交集的拒绝推动上述生产修复。交集测试首次未固定 follower 的初始相位，原算法会选择最近的另一个同名 Marker 区间；固定初始相位后按同一门槛检查，没有修改选择算法或放宽误差。

`four-root-smoke-first/second/third/diagnostic/diagnostic2/core-fix/rejoin.log`、`four-root-core-intersection.trx` 和编译失败日志均保留。最终三场景实际退出码为 0，日志无 Godot ERROR/WARNING。

## 保持开放

本批没有 UE 启动、C++ 修改、新导出或人工渲染验收。四根同时激活部分是 Godot 受控 clean/retry 对照，不能称为四根共同 UE 原生验收；已有单 Pivot 和三根原生证据范围不扩大。

下一步先捕获实际同一 UE Main/ItemAnimLayers 下四根共同遍历、共享 Marker 交集和真实状态历史，逐值对照现有宿主，再接完整 Locomotion 状态机、Idle/Air、全部 typed Interface 执行入口、Notify/Montage、最终足部与普通 Demo。最终单次 skin 发布、视觉、多角色和性能验收仍须完成。整个 Lyra 目标保持 active，ALS R2–R7 和用户暂缓项不因此关闭。
