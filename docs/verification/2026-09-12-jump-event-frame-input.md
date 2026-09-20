# 跳跃事件、倍率锁存与帧末 Delay（第八十九批）

本批继续补统一 Main Movement/BaseLayer 的实际输入，不改变 P3–P7 范围。
JumpPlayRate 和 Jumped 已从调用方夹具改为接受跳跃事件驱动的候选状态。
生产 Demo 仍未切换到完整 BaseLayer；本批不关闭滑步、换髋或上身视觉问题。

## 原始行为与证据

正式 `v4_movement_runtime_inputs.json` 的 EventGraph 中，BPI_Jumped 的独立
七节点链为：Jumped=true → JumpPlayRate=MapRangeClamped(Speed,0,600,1.2,1.5)
→ Delay(0.1) → Jumped=false。Speed 使用 UE cm/s；Godot 模型公开接口使用 m/s。
新编译器检查事件接口归属、七节点闭包、变量/函数归属、数据/执行连接及常量。
普通 Delay 被替换为 RetriggerableDelay 等源语义变动会拒绝编译。

新增只读脚本 `tools/unreal/export_jump_event_inputs.py` 导出真实 CDO：
Jumped=false、JumpPlayRate=1.2000000476837158、Speed=0。同时取得七组真实
Kismet MapRangeClamped 输出，并读取角色 EventGraph，确认 OnJumped 调用
MainAnimInstance 的 BPI_Jumped。七组是原生数学函数结果，不能称为完整
Blueprint 跳跃/潜伏动作时序的运行对照。

正式数据 `assets/config/v4_jump_event_inputs.json` 与普通 Editor 冷启动重复导出
SHA256 相同：`4665C5449D72C2C4C112CCE5B5B0B684565EEE6A54B3538BFB80A7050D1BD5B8`。
角色图证据在 `artifacts/jump-character-event-graph.txt`。

本地 UE 5.9 引擎源码进一步确定时序：

- `Character.cpp` 的 CheckJumpInput 在实际跳跃成功时调用 OnJumped；角色
  PostInitializeComponents 设置 Mesh Tick 依赖 CharacterMovement Tick。
  因而此事件读取动画实例已保存的 Speed，先于本帧 UpdateCharacterInfo。
- `KismetSystemLibrary.cpp` 的 Delay 只在当前 CallbackTarget/UUID 尚无动作时
  新建 FDelayAction。重复事件仍执行前面的布尔值/倍率赋值，但不重置计时。
- `DelayAction.h` 使用 float TimeRemaining，每次减去 ElapsedTime，<=0 时触发。
- `LevelTick.cpp` 在 PostPhysics 后处理其余对象的 latent actions；AnimInstance
  不是 Actor/ActorComponent 的逐对象 latent 消费者。当前支持的正常角色更新
  路径先求值动画，再执行该 Delay 的清除。

## 实现与所有权

Core 新增 `AlsJumpAnimationInputModel`、状态和双阶段候选：Frame 供本帧动画
使用；Next 是帧末 Delay 更新后的历史。这样大于 0.1 秒的单帧仍能让该帧动画
看到 Jumped，下一帧才读清除后的状态。没有把清除提前到动画更新前。

BaseLayer 映射入口使用 FrameInput.JumpAccepted 和上一已提交全局 Speed，
在当前全局地面/空中输入更新前准备跳跃事件。映射后的状态规则读取 Frame.Jumped，
所有跳跃播放器和来源同步读取同一个 Frame.PlayRate。移除映射接口上的外部
jumpPlayRate 参数。未映射的组件回归入口仍可传入受控输入。

来源被 Slot 隐藏时，这个全局事件与 Delay 仍更新；来源节点本身不被迫推进。
只有最终姿势通过晚期验证，才与地面/空中输入一起提交 Next。取消与重试、
迟到的 Slot 失败均不改变已提交的布尔值、倍率和剩余计时。无事件时倍率保持，
不会随空中水平速度变化而重算；JumpPressed 或仅变为空中状态不能合成事件。

## 验证

- 新增 13 项跳跃测试，加既有地面/空中 26 项，共 39 项通过。覆盖三档频率、
  到期帧、长帧、重复事件不延时、二次起跳、输入拒绝及八类源数据变异。
- Core 契约/FrameExchange 24 项通过，新候选状态均为 unmanaged 数据。
- Godot 构建零警告/零错误。新增真实 Motor 场景首次构建发现命令源接口方法
  名错误，修正为 GetCommand 后完成下列运行检查。
- 映射 BaseLayer 3360 帧通过：6 次事件、3354 帧倍率保持、3 次隐藏期间到期，
  81 次跳跃来源 leader 实际时间增量检查通过；每帧重试、12 次晚期故障、
  24 个守卫保持通过。来源事件 65，原地面/空中更新和保持检查仍通过。
  Marker follower 根据 leader 相位同步，不要求其增量等于自身倍率乘 delta。
- 新真实 Motor 场景 30/60/120 Hz 各完成两次起跳、两次到期、两次落地和一次
  空中按键拒绝；空中帧分别 34/68/134。第二个实际下落角色始终不合成 Jumped。
  该场景验证真实物理和事件输入，没有求值角色姿势。
- 旧 BaseLayer 3360 帧、6 次故障回滚保持。生产 single/parallel 各 180 帧通过，
  结果摘要 `21E164D829153157`，完整姿势 `CF9225D4DE9B2C8B`；仍是旧 Standing
  入口的兼容性证据。

主要日志：`jump-global-runtime.log`、`jump-input-motor-30.log`、
`jump-input-motor-60.log`、`jump-input-motor-120.log`、`jump-input-legacy-base.log`、
`jump-input-production-single.log`、`jump-input-production-parallel.log`，均在 artifacts。

按 ue-diagnosing-plugin-build-load 技能执行完整 Editor 目标及全部适用项目插件
审计，退出零，输入指纹仍为
`075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564`，
BuildId `0c423ffb-b3f5-4fb8-9fc9-5db49727eb0b`。两个辅助技能本机未找到，
使用直接构建、日志、输出和运行结果核验。

只读 Python commandlet 与普通 Editor 冷启动重复导出均退出零，保存 UE 资产数
为零。日志 `ue-jump-input-export.log` 和 `ue-jump-input-editor-restart.log` 保留
既有 AI 组件警告；普通 Editor 还保留两条 AutomationTest Condition failed 及
导航/材质/渲染变量警告，不能称整份日志零错误。本批未改 UE 插件、配置或
加载方式，未重复插件打包或 DataValidation；没有部署 DLL。

## 下一步

继续核对 UpdateGraph 的 ChangedToTrue：原图会先清除 ElapsedDelayTime，随后
重置旋转相关状态。这些回调与逐帧 WhileTrue 是不同历史，不能用 ShouldMove
当前值代替切换事件。接着统一剩余旋转/方向输入、最终曲线反馈和 Worker/Demo
姿势入口，复用原晚期提交，避免保留两个动画推进者。

最终 YawOffset、动态 Layering/Add/LS、Aim/脊柱/手部、Foot Lock/pelvis/平台链
以及逐帧真实视觉验收仍未关闭。真实 Montage/通用动作、全部 Overlay/道具、
Mantle/Roll/Root Motion、Ragdoll/Get-up/Pose Recovery/完整 Camera 与最终十分钟
性能仍在原计划内；音频暂缓。本批未提交、回退或合并工作树。
