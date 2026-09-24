# Mantle 宿主资源与通知编号映射

本批实现 Refactored Mantle 与普通 V4 角色资源的组合接口，避免独立编译时的局部 ID 互相覆盖。普通 Demo 尚未启用该组合，也未接通完整攀爬。

## 资源映射

`AlsMantlingHostResources` 接收完整宿主 animation set、authored actions、turns 和动态 sequence assets，先验证既有 bank 与资源存在性，再按稳定原始 ID 顺序分配动画、Montage、动作编号。动画与 Montage 从完整 set 的最大 ID 后分配，不能只看本帧或当前播放动作。给 Refactored Locomotion 分配独立于现有物理组的组编号。

原始 pose 数据的 source identity、骨架、单位、原始键与资源哈希保持不变；只有播放接口的资源编号映射到宿主空间。PoseSource 按映射后的动画编号选择原始采样器，多个 Montage 仍共用相同序列资源。构造时复制宿主数组，公开 ID 映射只读，组合过程不修改原资源。

这不是把 Mantle 动画追加成 V4 animation set 的伪条目。新 ID 不能拿去索引旧 set.Animations；后续宿主必须将 PostLocomotion 的采样路由到 Profile.CreatePoseSource，并使用正确骨骼/曲线适配。11 个虚拟骨和原生厘米坐标的既有边界不变。增加新的宿主资源后需重新构造完整组合，不能复用旧的编号分配结果。

## 通知编号合并

BindNotifies 在既有源 policy 前缀与 Montage 表后追加 Mantle 事件，同时映射 event/object/name/occurrence handle。旧源 policy、范围、时间线和已有动作通知保持原值；脚步配置按新的 event ID 返回只读查找表。原生两个 branching state 根据映射后的 action ID 编译，仍由同一物理 bank 驱动。

合并对象是由现有正式编译器产生的宿主通知表，其中 Montage occurrence handles 已位于 source layout handles 之后。没有引入另一套角色播放身份或时钟，也没有在资源层实现跨组 gameplay 取消策略。

## 验证

- 真实宿主 9 个 Roll/Get-up 动作与 6 个 Mantle 动作形成同一含 15 个 authored assets 的 bank；还带入真实 turn 和左右 transition 序列。三种新序列、六个新 Montage/动作 ID 与宿主编号分离。
- 调换宿主数组顺序，映射结果相同；修改调用方数组不影响组合结果；不存在于宿主 set 的动画/Montage 被拒绝。
- 六个动作各 21 个采样时刻，126 次完整 79 骨姿态与曲线采样和局部原资源逐值一致，旧局部编号在映射采样器中被拒绝。
- Roll 与 Mantle 可在一个 bank 中共存，Mantle 组内替换不误改 Roll/root owner；丢弃重试、Ragdoll 统一停止、状态最终清空通过。这是底层组隔离验证，不表示最终玩法允许同时播放两种全身动作。
- 合并后的旧 policy、definition、range、timeline 均逐值校验；同帧 Roll 状态和 Mantle 脚步通知可正确区分物理实例与 event ID。
- 既有独立 UE 144 条轨迹、11001 帧、306 通知，分别在原始编号和真实宿主编号/合并通知表下回放通过。两次播放/通知数值最大差均为 0，RNG 与身份对照通过。使用已有 oracle，没有重新导出 UE 数据。
- Import Release Mantling、MontageNotify、RecoveryActionProfile 定向共 106 通过；Optimize 构建 0 警告、0 错误。TRX `artifacts/mantle-host/import-final.trx`。没有新 Core 全量、Godot 场景或渲染验收。

## 接下来

将组合资源接到角色宿主的正确采样与 PostLocomotion 图位置，合并骨骼/曲线空间及最终 typed event dispatch，再串接障碍探测、移动基座、MantlingRootMotionSource、动作进入/中断/销毁与 Ragdoll 转换。资源接口本身不能作为普通 Demo 攀爬功能完成的证据。

其余旧未完成项继续保留：物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生覆盖、旧移动 oracle 闭包、完整视觉与十分钟性能验收。头颈、道具物理、音频暂缓；脚步贴花/粒子也仍只有配置引用。用户 plan、project.godot、诊断文件和 uid 不纳入本批提交。
