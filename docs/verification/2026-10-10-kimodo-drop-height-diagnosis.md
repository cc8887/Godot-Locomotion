# 当前默认包的不同高度下落诊断

2026-10-10，响应用户“超过两米下落时动画错误／错乱”的反馈。确认中等高度触地后的自动 Roll 表演严重错误；本轮只诊断，未重新生成、切换默认或修改动作规则。

## 输入与方法

- 当前默认：`als_nonprop_20261010_air_crouch_combined_v2`，123 个替换身份；使用当前呼吸倍率和默认 Root Motion 关闭设置。
- 主目录优化构建成功，0 警告／0 错误。新增 `scenes/tests/kimodo_drop_height_diagnostic.tscn`，实例化普通 `AlsDemoEntry`，沿现有角色时钟、物理与提交链观察，不提供动画姿态。
- 在平坦开放地面以零初始速度分别从 1.5／2／2.5／3／5／8 m 放下；静止与前进输入分开。普通自动翻滚及 Ragdoll 保持启用。
- `ALS_RESOURCE_PACK_PATH=original` 做相同条件原始资产对照；运行期间不改默认指针。逐帧记录实际物理、状态／动作、显示骨架、脚部状态；支持状态附加各图阶段与 Montage 时间／权重。
- 证据：`artifacts/kimodo_als/drop-height-20261010-diagnosis/`，包含 `frozen.json`、各轮日志／JSON／GPU 截图、`summary.json`、`roll-asset-comparison.json`。默认指针、动画设置及构建 DLL 的摘要在矩阵前后保持。

## 结果与根因

| 高度 | 实际触地前竖直速度 | 路由与观察 |
| --- | --- | --- |
| 1.5 m | -5.23 m/s | 普通 Land；仍有旧 Land 缓冲偏浅问题 |
| 2 m | -6.04 m/s | 普通 Land；无提交失败／冻结 |
| 2.5 m | -6.86 m/s | 普通 Land；无提交失败／冻结 |
| 3 m | -7.51 m/s | 自动 Roll，Kimodo 直立悬空；原始资产贴地翻滚 |
| 5 m | -9.80 m/s | 自动 Roll，同样存在异常抬升 |
| 8 m | -12.41 m/s | 自动 Ragdoll；无完整图采样的实际两轮均可倒地并完成观察 |

`AlsMovementActionRules` 根据上一物理帧的竖直速度决定行为：向下速度达到 7 m/s 自动翻滚，达到 10 m/s 则优先 Ragdoll；没有固定 2 m 动画切换。高度表仅适用于本轮重力 9.8 m/s²、零初速与地面条件。3／5 m 的错误不应归为持续 Fall 本体或整个状态机乱跳。

3 m 的两组均在 frame 78 触地并创建 Roll；frame 79 开始求值同一 `ALS_N_LandRoll_F` Montage。frame 84 的播放时间均为 0.13 s／权重 1，frame 90 均为约 0.26 s／权重 1。当前包骨盆相对 Root 高度最高 1.803 m，原始实际翻滚最低约 0.139 m。1.5／2／2.5／3／5 m 两包触地前胶囊位置逐帧完全相同；没有以动画位移代替物理下落的证据。

直接检查交付 Roll 资产：

| 数据 | 躯干最大倾斜 | 骨盆相对 Root 的高度域 |
| --- | --- | --- |
| Kimodo SOMA 交付源 | 18.1° | 本项只测源倾斜 |
| 当前目标 runtime raw | 35.2° | 0.816–1.798 m |
| 原始 V4 raw | 129.8° | 0.147–1.021 m |

源动作本身没有完成过肩翻滚。当前旧 Roll 生成分支仅有“rolls forward over a shoulder...”文字、末尾站姿和水平线性路径，缺少触地／卷身／过肩／恢复的关键姿态条件及专项表演门禁。Root 关闭时仍保留了错误的竖直骨盆姿态；原始对照同样关闭 Root，故不能通过开启 Root 开关解释或修复该表演缺陷。

Land_Light／Heavy 及 Additive 在上一轮 Air 更新时均保持；此前对当前原始生成源和目标的检查还确认 Heavy 屈膝弱于 Light，见 `artifacts/kimodo_als/landing-20261010-inspection/land-asset-inspection.json`。Land 缓冲和 Roll 表演需要分别修正。

## 失败证据与覆盖边界

共执行 18 轮：14 轮完成观察，共 2,806 帧／118 张 GPU 截图。完成表示诊断记录完整，不表示动作质量合格；所有报告保持 `qualityAccepted=false`。正常轮检查角色错误／冻结、物理与姿态身份以及 GPU 截图提交／呈现身份。

另 4 轮 8 m 试验因 `--production-graph-capture` 不支持 Ragdoll 状态而失败：当前／原始各一轮 headless、各一轮渲染。原日志及 26 张失败前截图保留，不能计作通过。移除这项可选图采样后，当前／原始各一轮 8 m 前进渲染完成，分别观察 121 个物理驱动帧。该失败属于诊断采样覆盖限制，普通 Demo 不开启此标志；本轮未改采样器或削弱断言。

本轮仅 60 Hz／单线程、平坦地面、Default Overlay 与短时 Ragdoll 观察；未覆盖 Root 开启、全高度／斜坡／装备、30／120 Hz／并行、Ragdoll 起身、完整蒙皮量化、长时间或性能。截图 FPS 不用于性能结论。此前 30 m Air 专项关闭自动 Ragdoll，不能覆盖本轮发现的中等高度自动 Roll 表演缺陷。

![同一实际 frame 90：左当前 Kimodo，右原始 ALS](../../artifacts/kimodo_als/drop-height-20261010-diagnosis/3m-landing-roll-comparison.png)

## 后续修复范围

独立约束 Land 的触地屈膝／骨盆下沉／恢复并从同源 full pose 派生 Additive；Roll 需单独约束贴地卷身／过肩旋转／恢复及专项几何门禁，再执行完整生成到最终 FBX／Godot 和不同落地速度的实际运行。修复前保留当前默认诊断身份与失败状态，不以运行过程无错误代替动作验收。
