# Mantle 普通 Demo 接入

2026-09-26，基于 main/f3be43d，在主目录实施。按用户最新要求优先交付 ALS Mantle（抓边后登上台面）。未新增项目副本或 worktree，保留用户对场景、漫游 NPC、HUD、README 和分层代码的未提交改动。

## 使用

普通入口仍为 `scenes/demo/als_demo.tscn`。场景左侧新增 `World/MantlingCourse`，有 0.75 / 1.35 / 2.00 米三个标识台。靠近并面向边缘按空格；没有合适目标时继续原跳跃。空中会自动尝试抓边。X 取消当前翻越，G 仍可切入布娃娃。取消后会等待落地或新的跳跃意图，避免下一帧立刻再次自动抓边。

资源已经来自现有 `refactored_mantle_*` 导出，不需要重新启动 UE 或再导出资产。本机生成角色资产仍是运行前提；Git 中只有代码的全新检出不能据此视作完整资源交付。

## 接线与原实现依据

- 对照本机 ALS 的 `AlsCharacter_Actions.cpp`、`AlsCharacterMovementComponent.cpp::PhysCustom`、`AlsRootMotionSource_Mantling.cpp`、`AlsMantlingSettings.h` 和 `AlsCharacterExample.cpp::Input_OnJump`。原图导出中已绑定 Locomotion → PostLocomotion Slot → Layering → Head。
- Main 使用真实 Godot ShapeCast 做前向胶囊、顶部球体、目标全胶囊和接近区间的净空检查；保留地面 50–225 cm、空中 50–150 cm、110°方向拒绝、±50°伸展、35°坡度、125 cm高台分类及10 cm/s目标速度门槛。保留1.9/2.4 cm floor距离。Godot safe/unsafe接触区间按真实 witness/support plane 恢复位置，不把容差当成额外半径。Visibility 阻挡层沿用本项目 static/dynamic/destructible 的 1/2/4 位约定；可用 `als_mantle_disabled` 元数据禁止抓取。
- 原 selector 选择高度、空中或 Overlay 专用设置；原 root tracks 算起始位置、终点与 warp。移动平台的起点/目标锚点位于目标局部空间，每帧按当前基座转换。与原 PhysCustom 一样，攀爬期间胶囊不扫碰撞，入口净空检查是前提。
- Mantle 的六个 Montage 显式映射到当前完整资源库存之后，加入现有主图物理 Montage bank，与旧动作共用该 bank；没有额外动画时钟或第二个骨骼写入器。新 `AlsMantlingFrame` 只向 Worker 传值。物理时间生成 Montage_SetPosition 对应的预推进位置；seek 不派发跳过区间的通知，不改变 blend 时钟，也不改已提交实例。实例 ID 校验阻止旧播放身份定位到同资产的新播放。
- 同步 branching 状态在该 bank 内运行。Character 读取上一动画更新的 ActionEnd，保留最后一个物理步，避免因预物理阶段提前推进 Montage 而早停一帧。脚步使用现有队列/类型化输出；没有把 branching 伪装成普通排队通知。
- 原精度的身体姿态和 Montage 曲线经已有 host sampler/虚拟骨适配进入 PostLocomotion 槽；Slot 全覆盖时停止更新其 Locomotion 源，部分覆盖时传原 source 权重。然后进入现有 Layering/上身/脚部与最终写入。相机读取本次已提交的 Mantling 状态。
- Main 生命周期快照保存物理运动状态；动画实例、branching、结束反馈和输入在角色提交时发布，Discard 不推进已提交时间。X 中断淡出 .3 s；目标销毁在该帧提交之后按原设置进入 Ragdoll，物理帧清除旧 Mantle 输入。

本次没有把其余 Refactored 图/Overlay 全部迁入同一最终原生角色 profile；旧外层桥和源 Notify 兼容更新仍在。这与已经接通 Mantle 生产玩法是不同验收项。

## 自动验证

证据目录：`artifacts/tests/mantling-demo/`（本机忽略文件）。

| 检查 | 结果 |
|---|---|
| Optimize Godot 构建 | 0 警告、0 错误 |
| Core Montage/Mantling 相关回归 | 197 通过，`core-montage-final.trx`；其中新增4项覆盖三频率 seek/通知区间/取消重试及旧实例拒绝 |
| Import Mantle 与 Locomotion host | 102 通过，`import-final.trx`；包括已有原生运动、pose、曲线、branching 和资源绑定基线 |
| 低台普通 Demo，single / 60 Hz | 1 米台面，正常抓边并恢复落地，`low-second.log` |
| Torch 低台，parallel / 30 Hz | 180帧、1次起播、30个物理攀爬帧，`torch-30-final.log` |
| 高台，parallel / 120 Hz | 720帧、1次起播、194个物理攀爬帧，`high-120-final.log` |
| 空中抓边及渲染，parallel / 60 Hz | 360帧、1次起播、65个物理攀爬帧，`air-render-final.log` |
| 移动平台，parallel / 60 Hz | 平移+旋转，360帧、97个物理攀爬帧、正常落地，`moving-final.log` |
| 最终平台身份，single / 60 Hz | `moving-identity-single.log`通过；与parallel相同97个物理帧及最终胶囊位置，额外断言发布平台ID；没有声称全骨逐帧摘要对比 |
| 头顶受阻 | 360帧、0次起播，`blocked-first.log` |
| 目标速度过快 | 360帧、0次起播，`fast-final.log` |
| X 取消 | 21个攀爬帧后中断，恢复地面、未再次抓边，`cancel-final.log` |
| 目标销毁 | 21个攀爬帧后中断并进入真实 Ragdoll，`destroy-first.log` |
| 旧移动行为回归 | 30 Hz / 850帧、3次Pivot、4次动态补步，`locomotion-regression.log` |
| 相机与物理恢复回归 | 60 Hz / 480帧，含Ragdoll/第一人称，`camera-regression.log` |

高台和空中各生成60张截图。抽查高台42/60/84/108/144及最终空中48/66/84/120，覆盖伸手、抓边、撑起、抬腿和恢复站立。截图是普通生产输出；不是独立 pose smoke。文件名是测试采集时刻，异步渲染HUD帧号可能相差一帧，不能用来声称逐帧 UE 等价。

首轮构建出现局部变量同名和 Slot 字段名错误，已修复；新增测试首轮误写 traversal 字段名，修正后通过。低台初次夹具沿用用户已下移的地面，角色初始悬空自动抓边，被测试误判为地面类型；夹具改为统一地面并等待站稳。移动平台首轮反复乘旋转矩阵引入非刚性漂移，旧相机正确拒绝；夹具改为由累计角度重建刚性矩阵，补入相机失败门禁，最终重跑通过。失败日志保留，没有放宽原生容差。

结束反馈的一帧顺序和相机 Mantling 输入修订后，已重跑最终30/60/120 Hz案例、取消/移动平台及移动/相机回归；早期low/high/air日志仅作接入过程证据。最后增加移动平台的值快照身份检查，单独重跑 single 场景验证。

## 仍保留的验收项

完整高度/斜面/薄边缘/全部13 Overlay/连续反复翻越的人工矩阵、新UE整角色连续轨迹对照、多人长时压力与十分钟性能预算仍未关闭。当前 Godot 查询受实际物理后端的接触精度约束；只支持直立、均匀缩放的角色，复杂非均匀碰撞形状仍需显式适配。截图HUD性能来自带截图写盘并发测试，不作为帧预算证据。

未修改或启动 UE、未执行全量测试、未重新导出或推送远程。音频、道具物理、既有头颈拉长专项继续按用户要求暂缓。R2–R7 其余目标保持开放。
