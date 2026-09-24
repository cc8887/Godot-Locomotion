# Mantle 完整源动画与接入缺口

本批在 `.` main 增加 `tools/unreal/export_mantle_animation_inputs.py`，只读导出实际 Refactored 攀爬动画数据。没有改 UE 资产或 C#/Godot 运行时。

## 已导出的真实数据

- 6 montage：保留真实 `PostLocomotion` slot、段引用、长度/速率、BlendIn/Out、auto blend-out、触发时间、原生 T3D 与完整 notify 身份/时间/过滤字段。
- 3 sequence：High / Low_Left / Low_Right，各 68 条原始骨骼轨道，关键帧数 81 / 46 / 46。保留原始 channel 数和 float 分量、evaluation policy、全部曲线原文与 notify。根骨轨道逐值验证与上一批冻结 root 数据相同。
- 1 skeleton `/ALS/ALS/Character/SK_Als.SK_Als`：68 物理骨、79 逻辑骨，原始 reference/parent/mapping/virtual bone 定义/retarget modes，加原生 T3D（含 slot groups）。
- 独立 pose reference：3 动画 × 5 时刻（0/.137/.419/.773/1 × duration）× 4 上下文（raw、不锁根重定向、资产根锁定、提取根运动时根锁定）= **60 姿态 / 4,740 个骨骼 TRS**。完整数据与参考分离，参考引用生产文件 SHA。
- 所有三个 sequence 的 `enableRootMotion=false`、`forceRootLock=true`、`rootMotionRootLock=RefPose`。这与攀爬 motion source 单独读取绝对根骨不是同一个采样上下文，不能混用。
- 每个 sequence 都有 `PoseStanding`、`PoseGrounded` 两条曲线和 3 个 `AlsAnimNotify_FootstepEffects`，总计 9 个。音频仍按用户要求暂缓。
- 每个 montage 有 `SetLocomotionAction` 和 `EarlyBlendOut` 两个 state，总计 12。额外反射导出实际 payload，避免 T3D 省略默认值后猜错：action 为 `Als.LocomotionAction.Mantling`；early blend-out 高攀爬 .4 秒、低攀爬 .35 秒，check input / locomotion mode / rotation mode / stance 全 true，对应 InAir / Aiming / Crouching。EarlyBlendOut 是 native branching point，后续不能仅按导出的 TickMode 字段把它当普通排队 state。

## 已确认的移植缺口

现有普通角色使用 V4 animation set，`AlsAuthoredMontageCompiler` 显式绑定 `BaseLayer` / `Grounded Slot`，不接受 `PostLocomotion`。现有 raw compiler 只实现 Animation / Skeleton 重定向，其他模式明确拒绝，没有静默降级。

实际 Refactored skeleton 的模式为：5 Animation、3 AnimationRelative、60 OrientAndScale。AnimationRelative 对应 `ik_foot_root` / `ik_foot_l` / `ik_foot_r`。后两种原生算子尚未移植，必须补齐才可接完整姿态。

与 `v4_movement_source_inputs.json` 中 skeleton 对比：68 物理骨名称、父子关系相同。67 个 reference TRS 不逐位相同，但最大局部位置差仅 1.17162314e-5 cm，最大 quaternion dot 偏差 5.11815728e-8，不能把它夸大为骨架大幅不同。11 个 virtual bone 名称则全部不同，必须依据 source/target 定义重新展开，再显式绑定目标骨架；不能沿用 V4 logical bone index。

下一步：实现 AnimationRelative / OrientAndScale 的原生重定向和完整源数据编译，以 60 个独立姿态参考验证；随后建立物理骨/虚拟骨映射、曲线语义、PostLocomotion 插入点和原生 Notify 处理，再进入具动作身份的宿主时序、探测和生命周期。现有 4,536 motion source 对照只证明位移层，不能替代这些完整姿态/图语义工作。

## 验证与范围

按 UE 插件构建技能运行完整 Editor 目标及审计：0 actions、fingerprint `D98F80146ED037C94A3980B638367A792469BCD21021066BC657DD39A9FA9EC9`。日志前缀 `20260924T134539144Z-788d73f94f234dd29cf90f70c5a0dde5`。

初始 first / repeat 冷导字节一致；增加 notify 反射 payload 后的 final / final-repeat 两份也逐字节一致，均退出 0、0 error / 0 warning。日志与产物保留在 `artifacts/mantle-animation-*` / `mantle-pose-*`。导出均未保存资产。结构检查确认 3/6/1 closure、60 姿态全部包含 79 名称、参考依赖摘要匹配。当前没有 C# 姿态回放比较，不能称这 60 个姿态已经在 Godot 对齐。

冻结生产输入 SHA256：`E82CA0FCC1AB4F04D009AF7FA1F38BDC1C0A78759FD586EF341AF21024089049`。
冻结 pose reference SHA256：`1DF2A29D71ADCDCD7C1E3F702963EE12324C3845562D8419F84F655127BEB0A1`。

本批为 Python 只读导出，无新原生模块/配置变更，不重复普通 Editor/DV；没有新 Core/Import/Godot 构建、全量测试、可玩场景或视觉验收。前批证据不扩张为本批完整姿态验证。

主目标仍未完成；旧物理稳定性 9/12、Flail 0/3、复杂相机/整体物理缩放/最终性能预算保留，头颈和道具物理继续暂缓。用户 P4 修改与三份头颈诊断文件未修改、未纳入提交。
