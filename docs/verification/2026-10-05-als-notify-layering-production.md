# ALS 优先级首批生产接入

基线 `main / a3011db9`，直接在主目录实现。用户已有场景、NPC、HUD、Kimodo 和 Lyra 未提交内容保持原样；没有提交或推送。本批依据 ALS-Refactored 调研推进通知接入、上身分层与质量测量，不代表 R2–R7 全部关闭。

## 生产行为

1. 六类原共享 Source Sync owner 的真实播放区间进入现有角色通知队列。`AlsRefactoredCharacterNotifyBinding` 保留 native source 顺序、最高权重样本、follower、weight、chance、LOD、ScopeFiltered、epoch、ReachedEnd 和原对象身份；过滤使用既有角色源 RNG，不新增通知时钟或状态生命周期。物理 Montage 的既有预过滤、branching 和合并顺序继续保留。
2. 原通知事件、对象、名称与 occurrence handle 使用独立后缀空间。完整类型化载荷及原 asset path 可以按新 event ID 查询；原 catalog 的 animation ID 不能当作 V4 animation-set ID 使用。事件查询表在构建时生成，运行时查询无 LINQ 分配。
3. Refactored 生产路径只使用旧共享 bank 收集仍在桥外的 Overlay/Ragdoll 源；不再运行旧主移动/站蹲状态机。原共享 source 空帧、状态结束、取消重试、事件候选和姿态候选仍由同一个角色提交。诊断增加原通知引用计数、原 Layering 提交帧计数、旧移动更新计数；普通及多角色测试断言旧移动更新为 0。移动状态诊断改读原图已提交状态，并标明其 namespace。
4. 普通 Demo 默认加入 `--refactored-layering-frame`。原 Layering 图的固定帧零站蹲基底、局部/网格空间加法、曲线混合及 Slot 曲线尾部使用既有原生组件。生产前后通过显式 FBX/native cm 坐标、物理骨映射、虚拟骨重建和 V4 Overlay → 原 Layer* 曲线映射连接；不建立第二套源播放器。图读取上一角色提交的最终曲线，失败丢弃候选。
5. 当前 13 个 Overlay 的姿态生产者、Aim/脊柱和手部约束仍是既有 V4 路径。区域 Head/Spine/Arm/Pelvis/Legs/Curves Slot 尚无生产 Montage 注册，因此使用明确 passthrough。**本项完成原 Layering 生产接入，不是完整原生上身或 13 个原 Overlay 替换。**原 Head/View 组件没有据此启用；头颈专项继续暂缓。

`--legacy-layering` 仅用于普通 Demo 的分层对照，保留本批原源通知与主移动链；`--legacy-animation` 保留原有完整 legacy 诊断入口。原 Layering 单独显式启用时要求同时启用 `--refactored-stance-hosts`，确保骨架边界已经验证。

## 最终蒙皮质量测量

`RefactoredStanceDemoSmoke --foot-quality` 使用 `AlsSoleGeometryObserver` 独立读取已经发布的实际 Skeleton/skin，而不是 Rig 的目标位置：

- 穿透来自所有 >=99% foot/toe 权重的支撑顶点；滑移来自原 bind pose 下固定下 4 mm 足底样本的质心。几何选择没有随测试结果改变。
- 在支撑表面局部坐标中统计厘米位移、锚点漂移、滑移速度和膝角单帧变化。平台运动由坐标变换剔除；不同平台、接触进入/离开和帧间断不跨窗口累计。
- 同时输出 Grounded 几何 ContactApplied 窗口、完全锁脚窗口（Amount >= .999，且无 thigh/foot 角度约束）及站姿转换后 560–639 标准阶段的观察窗口；最后一个窗口**不保证姿态已经完全稳定**。
- 膝角峰值记录物理 frame ID。测试原有两次 15 cm 胶囊外部扰动保持，不通过排除峰值掩盖问题。测量表示动画/几何接触，不能解释成支撑力验收。

60 Hz 新旧 Layering 使用完全相同的 1700 帧输入，得到：

| 完全锁脚统计 | 原 Layering 左 / 右 | V4 Layering 对照左 / 右 |
|---|---|---|
| 接触帧 / 连续样本 | 772/756；731/715 | 相同 |
| 质心每帧 RMS 滑移 cm | 0.0000616973 / 0.0000505892 | 0.0000565854 / 0.0000424578 |
| 最大锚点漂移 cm | 0.0013359953 / 0.0003844384 | 0.0011844389 / 0.0003844384 |
| 最大支撑穿透 cm | 0.0000461936 / 0.0006943941 | 0.0000447035 / 0.0006854534 |
| 最大膝角单帧变化 | 14.04920° / 17.83719°，frame 1514 | 14.04920° / 17.83718°，frame 1514 |

最大膝角变化紧随 frame 1513 的原 15 cm 扰动。560–639 观察窗口仍有约 4.43°/4.04° 的单帧变化，新旧路径的峰值分别同在 572/575 帧，差约 0.00003°/0.00001°。这支持“未观察到本批分层替换引入明显膝角差异”，不证明所有支撑切换或复杂地形稳定，也不把上述变化自动定义成需要修改动画的缺陷。

全部几何接触窗口含部分锁定、步态滚动及扰动；其整体滑移远大于完全锁脚窗口，因此不能用它直接评价固定支撑。原始数据在 `quality-native60.log` / `quality-v4layer60.log`，30 Hz 的独立记录在 `stance30-quality-final.log`。

## 验证证据

本机目录 `artifacts/tests/2026-10-05-als-production/`，ignored，不提交生成资产。

| 检查 | 结果与文件 |
|---|---|
| Debug / ExportRelease 构建 | 最终均 0 错误、0 警告；不是打包验收 |
| Core 通知队列/状态/溢出/取消/过滤及质量统计 | 最终 41 通过、0 失败、0 跳过，`core-clean-final.trx`，含峰值帧统计 |
| 原通知、Source Sync scope、PostLayer、原 Layer native 及实际 Walk/Roll → 角色适配 | 最终合并 39 通过、0 失败、0 跳过，`import-clean-final.trx`；实际源适配覆盖 30/60/120 Hz、full payload/path、取消同帧重试和查询零分配 |
| 原共享宿主、角色动作及 Standing 严格 native | 24 通过，`host-native-regression.trx`，原门槛未改变 |
| 普通 Demo 30/60/120 Hz | 850/1700/3400 帧；Grounded mask 30、Locomotion mask 29；各 3 Pivot、4 动态补步。30 Hz source refs 147，60 Hz 146，120 Hz 151；这些是提取引用，**不是派发数量的帧率等价声明** |
| 实际渲染 | OpenGL/NVIDIA RTX 5080，60 Hz/1700 帧，35 张截图；检查 110/255/1200/1550 的冲刺、蹲伏移动和站蹲瞄准。瞄准镜头仍裁下肢，不能称完整脚部视觉验收 |
| 十角色移动旋转平台、全部 13 Overlay | single/parallel 各 3621 提交帧、2 次取消、1 次提交暂停，Overlay mask 8191；`dispatch-single.log` / `dispatch-parallel.log` |
| single/parallel 摘要 | Pose `8312A368C37F3025`，Root `AC83A25857256884`，Result `B8A74F5EA0F23661`，三者分别完全一致；各派发 130 个事件 |
| 普通 Roll | 60 Hz/420 帧，2 次接受、2 次忙碌拒绝、2 次完成，`roll60.log` |
| 普通 Mantle | low/moving/destroy 各 60 Hz/360 帧；分别 59/97/21 活跃帧，destroy 中断并进入 Ragdoll，`mantle-*.log`；没有据此覆盖全部高度/Overlay |
| 普通 Ragdoll/Get-up/相机 | 60 Hz/480 帧，Ragdoll 与第一人称均覆盖，`camera-demo.log` |
| 相机故障恢复 | 2 个故障 episode、10 次失败后恢复，`camera-ragdoll.log`；2 条预期容量注入 warning，其余普通回归无 WARN/ERROR |
| 最新 ALS/Lyra 入口交接 | 60 Hz，8 次交接，空中/蹲伏各 2 次，故障与动作延后均通过；`switch.log` / `switch.json` |

最终相关测试合计 104 项全部通过（Core 41 + Import 39 + 宿主/native 回归 24），不是全量测试验收。

新增夹具初次失败分别来自：缺少真实 source scope UpdateCounter、错误假定稀疏 sample ID 的空间为 player*3（实际为 player*16）、错误假定通知索引 0 必为 Sequence（实际可为 Montage）。均修正夹具前置条件，未放宽运行时身份门禁或原生阈值。Core follower 用例首轮写错 policy 的 OnFollower 布尔位置，修正后通过；首次质量编译的 Math 命名空间冲突已修复。失败 TRX 保留。

## 资源与余项

本批没有修改动画关键帧、FBX、骨骼 hierarchy/rest、蒙皮、原曲线 JSON、Notify/Marker 导出或 UE 内容；没有启动 UE、重新导出或生成替换资源。只新增运行时曲线/事件映射和现有虚拟骨重建，因此本批运行不要求修改动画或骨架资源。

下一阶段仍需要：桥外 Refactored 动态动作 bank 的源通知与完整消费者；原 Overlay/Aim/手部组合接入；完整 Character→Parent→图/动作连续 UE oracle；坡地、台阶和不同支撑相位/动作的测量矩阵；性能、十分钟与人工验收。RootScale、CameraShake、Footstep 完整载荷查询已经保留，但不能把类型化事件进入队列称作所有物理/音频/特效副作用实现。音频、道具物理和头颈专项保持用户暂缓约定。
