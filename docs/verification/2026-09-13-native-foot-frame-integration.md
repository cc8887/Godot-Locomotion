# 原生 Foot IK 接入真实 Gather、Worker 和提交链

第一百三十三批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 结果与入口

继续原 P4 的 Foot IK / Foot Lock / pelvis：将前两批的属性函数与九节点
控制链接入实际角色的物理采集、历史曲线和统一候选事务。显式使用
`--als-cycle --foot-ik-frame` 可运行 BaseLayer → Overlay/Layering/Aim →
双手 IK → 双脚 IK 的生产路径，关闭旧脚部模型和修改器的重复骨骼写入。
默认 Demo 仍是 BaseLayer 入口，不能把新入口的通过说成默认效果已通过。

同时修复 Main Movement/Grounded 包装图遗漏的 Enable_FootIK 曲线写入。
此修复作用于公共移动图，默认 BaseLayer 和 `--layered-frame` 同样受益。
不能因仍有后续 P5/P6 功能，就将当前移动外观问题推迟到这些阶段之后。

## 新发现的完整性缺口

正式 `assets/config/v4_main_movement_graph.json` 中 Main Movement States
的 Grounded 内容是三个节点：读取 Main Grounded States 缓存 → ModifyCurve
→ StateResult。ModifyCurve 的序列化 `Node.curveValues` 为 `[0,0]`，但真正
暴露引脚 `CurveValues_0` 和 `CurveValues_1` 均为 `1.000000`，Blend/alpha=1。

之前运行时只复制缓存，遗漏整个包装图。动画序列本身没有 Enable_FootIK_L
曲线，因而“完整导出动画”无法补上这两个图内生成值。实际回放出现过落地后
Enable=0，虽然原地转身的 FootLock 已为 1，仍无法进入脚锁函数的情况。

新增 `AlsGroundedMovementCurveCompiler` 校验原图节点、连接、模式、名字、
静态引脚和生命周期，编译真实引脚值。`AlsMainMovementFrameRuntime` 在
Grounded 状态输出处写曲线，再参与父过渡混合；不污染共享 Grounded 缓存。
这是“数据与公式存在，但图执行遗漏”的直接实例，不通过调小阈值修补。

## 逐帧输入与所有权

- `AlsFootIkPoseSample` 保存已提交的物理 root / ik_foot_l / ik_foot_r、组件
  到角色变换、启用曲线与角色/代际/帧身份。冷启动显式使用本实例参考姿势。
- Motor 在 MoveAndSlide 后、ALS 角色朝向更新前保存 LastMovementRotation；
  世界速度取移动后的 Velocity，补偿用世界 delta，动画插值仍用动画 delta。
- 主线程以先前已提交的 IK 脚位置投影到 root 高度，执行原 +50/-45 cm
  世界竖直射线，传递纯值命中/法线/变换快照；物理查询不进入 Worker。
- `AlsFootIkFrameRuntime` 使用上次成功提交的四条原始曲线更新脚部属性，
  再对本帧最终手部之后的姿势运行九节点链。本帧曲线只在成功提交后变为历史。
  曲线缺失按源 GetCurveValue 查询得到 0，姿势输出的曲线缺失标志仍保留。
- 场景与动画历史身份必须一致；失效输入、求值失败、候选丢弃均不推进状态。
  完整分层所有者的 ValidateCommit/Commit/Discard 包含脚部状态。
- 最终骨骼快照随可视候选通过 CommitStage 发布，下帧 Gather 才能读取。
  角色替换重新采集新骨架冷姿势与查询，不能给退休代际的姿势重新贴身份。
- 旧修改器只保留诊断遍历，native 入口脚部权重和骨盆输入为零；真实回放
  断言其写入事务数与脚链重建数都为零。

Godot 米制/Y-up 与 UE 厘米/Z-up 的向量、旋转转换集中到
`AlsFootIkCoordinates`。四元数转 Rotator 按本机 UE 5.9 的 FQuat4d::Rotator
源码处理普通与奇异分支；本批有轴向/奇异测试，没有新增这一路转换的 UE
导出 oracle。前批原生 IK/属性数学对照仍是局部证据，不是最终图姿势对照。

## 验证结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 优化 Debug 构建 | 0 错误、0 警告 | `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` |
| 脚部属性/控制/帧事务与手部专项 | 82/82 | `artifacts/test-results/foot-frame-runtime.trx` |
| Core 合约布局与帧交换 | 24/24 | `artifacts/test-results/foot-frame-contracts.trx` |
| Grounded 曲线包装与移动图编译 | 36/36 | `artifacts/test-results/grounded-foot-curves.trx` |
| 原始 Main Movement + BaseLayer | 3,360 帧，30/60/120 Hz，同帧重试一致 | `artifacts/native-foot-main-movement-final.log` |
| 旧分层生产入口 | 并行 600 帧，含空中/落地/蹲姿及代际替换 | `artifacts/grounded-foot-layered-600.log` |
| 新脚部生产入口 | single/parallel 各 960 帧，姿势、角色和结果一致 | `artifacts/native-foot-verified-single.log`、`native-foot-verified-parallel.log` |
| 晚期姿势失败 | 脚部/控制器/骨骼/结果恢复，交换区未发布 | `artifacts/native-foot-late-transaction.log` |
| 晚期事件失败 | 候选事件存在，回调泄漏为 0，状态/身份恢复 | `artifacts/native-foot-late-events.log` |
| OpenGL 实际渲染横移 | 720 帧、48 张截图、552 移动帧、58 转身帧、角度诊断无违规 | `artifacts/native-foot-strafe-verified.log` 与同名目录 |

最终 single/parallel 960 帧的共同摘要：结果 `D898A6B5BD5DE295`，完整姿势
`7B82A91E8A09C723`，角色根 `DB5B813964D3479C`，采样姿势
`6804D603D2523040`。包含 223 个播放器/257 个采样项，316 帧脚锁活动、
910 帧地形偏移、37 个事件；lag/stale=0，旧代际拒绝及替换恢复通过。
新增帧事务专项还覆盖世界/动画时间区分、空中重置/重入、取消/坏姿势重试
以及热身后 2,000 帧属性与骨骼求值无托管分配。

实际渲染保存逐帧 NativeFootPose/NativeFootState 以及多帧胸/髋/手臂姿势。
查看了起步 75/90、第一次反向 240/255/270、第二次反向 435/450 帧：有
胸部倾斜、手臂与髋部姿态变化，没有据此宣称交错步或滑移达到 UE 原版。
角度诊断最大单帧脚部变化 14.138°，这不是支撑脚滑移的测量结果。
捕获暂停与截图开销存在，图中 FPS 不作为性能预算证据。

本批修正的另外两个诊断问题：独立 LandingProbe 应接当前图播放器范围，
不能把共享时钟缓冲区容量直接当图布局；原生 FootPose.LockAmount 应显示
实际脚锁属性，不能使用独立的 IK 启用权重。首次渲染目录
`native-foot-strafe-visual` 保留错误 HUD，最终 `native-foot-strafe-verified`
使用正确值；该修正只改变发布诊断字段，没有改动骨骼求值算法。原 960 帧结果摘要
`1D1E8AB550A80FFC` 也仅因锁定诊断字段修正更新，骨骼摘要保持。

## 尚未完成与下一步

1. 普通动画分支的根初始化、失去相关性/重入等生命周期与完整根选择器。
   当前 Worker 异常按已有策略冻结；本批回滚通过不代表支持跳帧自动恢复。
2. 明确平台运动、坡面与最终原生脚链的完整配合，执行 UE 同输入的多帧
   胸/髋/脚姿势和曲线对照；起步与反向只在真正支撑窗口比较相对地面轨迹。
3. 完成以上 P3/P4 整链与人工验证后切换默认 Demo，保留已通过的键鼠体验。
4. 原 P5A 通用 Notify/State、Sync/ActionPlayer/Slot 剩余生命周期；P5B
   全 Overlay/道具玩法；P5C Mantle/Roll/Root Motion；P6 Ragdoll/Get-up/
   Pose Recovery 与完整 Camera；P7 十分钟、10 角色及 Release 性能预算。

既有 Core 23 项失败、Import 分配测试不稳定、旧 p95=2.559ms 超过 2.5ms
均未在本批关闭。专项与短回放通过不能表述为全套测试或 1:1 移植通过。
音频仍暂缓；保留现有工作区修改，没有 commit、revert 或 merge。
