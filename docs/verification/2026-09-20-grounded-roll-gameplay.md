# 普通入口的地面 Roll 玩法

本批直接在 `${env:GODOT_ALS_ROOT}` / `main` 实现。普通入口
`scenes/demo/als_demo.tscn` 默认启用地面翻滚及同帧 Root Motion 碰撞消费；
R 触发，X 取消。`--action-preview` 保留旧的可替换原地 Montage 诊断模式，
可额外配合 `--montage-root-motion` 单独验证位移传输。

## 原版依据与实现范围

本地 `${env:ALS_REFERENCE_ROOT}` 的
`b754d6f0f2bb03741d301f8fb88077ebfe561e17`：

- `AlsCharacter_Actions.cpp`：`StartRollingGrounded` 仅地面触发；
  `IsRollingAllowedToStart` 允许无动作，或 Rolling 且同一 Montage 不再
  `Montage_IsPlaying`；成功播放时捕获输入 yaw，无输入时保留角色 yaw。
- `FAlsRollingSettings` 的 C++ 默认值：启动时蹲伏、朝输入转向、转向半衰期
  0.1 秒。本批使用这些明确的默认值，没有宣称已导出 Refactored DataAsset
  对这些字段的覆盖值。
- `UAlsRotation::DamperExactAngle`：角度归一化、175 度以上差值的逆时针选择、
  `InvExpApprox` 半衰期阻尼；复用本项目已校验的原生浮点近似实现。
- 本地 UE `AnimInstance.cpp`：`Montage_IsPlaying` 查询资产的 active instance，
  不把已经淡出的旧实例当作当前播放。自动淡出移除 lookup 后可以再次触发。
- UE `CharacterMovementComponent.cpp`：先以运动前的网格变换消费平移，移动后
  执行 PhysicsRotation，再叠加 Root Motion 旋转。
- UE `Character.cpp` 的 `OnStartCrouch` / `OnEndCrouch`：胶囊缩矮时补偿
  mesh 相对高度。补偿同时用于展示、Motor 原生脚部采集、分阶段脚部查询以及
  Root Motion 的 mesh-to-character 变换。

这次是源码对照、Core 行为测试和 Godot 正式图集成验证；没有新生成 UE
Character 整段翻滚轨迹 oracle。原始动画/根运动采样证据仍见上一批报告。

## 状态与线程边界

`AlsRollingState` 包含请求、物理播放身份和已锁定的目标 yaw，不另建播放时钟。
Worker 在共同 Montage 接受请求后形成候选；结果提交后 Main Motor 才使用。
被拒绝的重复请求不会替换物理实例，也不改变目标 yaw。

动作结束、取消、故障恢复取消，以及当前实例的 Rolling Notify End 会清除
玩法状态；旧实例的结束通知不能清除新实例。语义停用清空 Worker 和 Main
两侧的状态。普通调度暂停不清空已经提交的状态，也不重复积分 Motor。

翻滚中使用有效蹲伏命令并禁止跳跃；输入适配器保留用户的期望姿态，结束后
按原有站起空间检查恢复。帧输入新增 GameplayAction、MeshHeightOffset，结果
末尾追加 Rolling，并将活动 Roll 身份/朝向加入摘要；原有字段偏移保持。

## 验证

产物目录：`artifacts/rolling-gameplay-20260920/`。

- Godot 优化构建：0 警告、0 错误。
- Core Release：2576 通过；按既有规则排除两个历史 P5A fixture。
  `core-final.trx`。
- Import Release：2286 通过、1 个既有跳过；`import-final.trx`。
- 普通入口真实 R / Space 输入，30 / 60 / 120 Hz：地面接受、重复拒绝、空中
  拒绝、翻滚中反向输入不改目标、逐帧半衰期转向、禁止起跳、结束恢复站姿，
  以及保持用户期望蹲伏。每档 2 次接受、2 次拒绝、2 次完成。
- 最终高度补偿后的 60 Hz 单/多线程各 420 帧，结果摘要相同：
  `5EACFB40302FFE6B`。同时断言展示根与 Main 脚部采集采用相同补偿位置。
- 两次 BeforePublish 故障后恢复，取消旧 Roll 并在同帧接受新的 Roll：
  失败不发布新目标/身份，Motor 不重复积分；Rifle → Bow 提交保持。
- 语义停用 pending / held 两个边界：恢复后没有旧 Roll，下一次请求可重新
  播放；generation 回调退役及动作输入 Commit hold 回归通过。
- 普通入口 Alt/A/D/鼠标 360 帧通过，原有键鼠方向行为保留。
- 旧动作预览的墙体碰撞回归通过：240 帧、77 个受阻帧，前进约 0.649892 m；
  保留 3 接受 / 1 替换 / 1 取消 / 1 完成的传输合同。
- 十角色旧 Montage 传输诊断（启用 Root Motion，未启用新 Roll 门控）单/多线程
  各 3621 帧通过；pose `B4508FA2A28E2C6C`、root `81A75CF9FB938426`、
  result `73C8E7996429124D` 与上一批相同。这是旧传输兼容性回归，不能将其
  当作十角色新 Roll 玩法或十分钟性能验证。
- 多帧渲染保存在 `frames-height/`，检查了启动、翻滚中段和恢复段。
  截图显示胶囊蹲伏的网格下沉已修正；不是完整 UE 视觉逐帧一致性认证。

复现核心用例（ENGINE 为项目使用的 Godot 4.7.2 Mono console）：

```powershell
& $ENGINE --headless --path D:/GodotALS res://scenes/tests/rolling_gameplay_smoke.tscn -- --hz=60
& $ENGINE --headless --path D:/GodotALS res://scenes/tests/rolling_gameplay_smoke.tscn -- --hz=60 --single
& $ENGINE --headless --path D:/GodotALS res://scenes/tests/animation_failure_recovery_smoke.tscn -- --replacement --prop-switch
& $ENGINE --headless --path D:/GodotALS res://scenes/tests/animation_deactivation_smoke.tscn -- --boundary=pending
```

`action_input_smoke` 显式选择旧预览传输语义，保留原来的“重复请求替换”测试。
本批生命周期 fixture 在开放地面落稳后触发，不再依赖出生悬空时也能播放 Roll。

## 首错及修复

1. 初次 Core 回归的字段顺序断言未登记追加字段；补齐契约列表后通过。
2. 新烟测两次构建错误分别为缺少 Dispatch 命名空间和诊断属性名写错；均已修正。
3. 旧故障/停用 fixture 的首帧 R 被地面门控拒绝。第一次定位还漏算场景中
   CollisionShape 自身的 -0.43398404 m 偏移；按 collider 世界位置放置并等物理
   落稳后通过。没有修改用户的测试地形。
4. 渲染暴露蹲伏胶囊下移造成 mesh 下沉；按 UE 规则补偿所有消费路径，
   单/多线程重新检查，保存修复前后的截图和日志。

## 未完成项与下一步

本批只关闭地面主动 Roll 这一段，不关闭整个 P5C。

- **落地自动 Roll 尚未启用。** Refactored 先判断高坠落速度 Ragdoll，再判断
  700 cm/s 的 Roll 阈值，以 1.3 播放倍率并朝水平速度 yaw 翻滚。需要同时补齐
  动作请求播放倍率合同与 Ragdoll 优先级，不能忽略前者只接一个落地按键替代。
- **翻滚离地转 Ragdoll 尚未实现。** 目前会继续当前 Montage 并受重力影响，
  这是已知的完整性差异；没有用取消动画冒充 Ragdoll。
- Mantle、真实 Ragdoll / Get-up / Pose Recovery、Ragdoll 道具清理与重装备、
  剩余 Notify gameplay、完整 ALS Camera、复杂地形观感与十分钟性能预算仍在清单。
- 下一步先补 Roll/Ragdoll 的玩法边界和落地请求倍率，再推进其余动作链路。
  本批的短回归、调试截图及 HUD FPS 不作为十分钟性能证书。
