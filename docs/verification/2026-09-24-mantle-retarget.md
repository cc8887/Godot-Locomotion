# Mantle 原始姿态重定向

日期：2026-09-24。主目录 `.`，分支 `main`。

## 实现

新增 `AlsPrecisePoseRetargetModel`，按本机 UE `AnimationRuntime.cpp` 的
`RetargetBoneTransform`、`BoneContainer.cpp` 的 OrientAndScale 缓存和
`UnrealMath.cpp` 的 FindBetweenNormals 实现同骨架、非 baked raw 的 tracked-bone pass。
完整保留 Animation、Skeleton，并增加 AnimationScaled、AnimationRelative 和
OrientAndScale。缺失轨道、虚拟骨和无 source reference 不进入重定向操作。

Relative 保留旋转乘法顺序、末尾归一化、位置差和安全倒数 scale；OrientAndScale
保留参考平移相近时不建缓存、当前平移相近时直接采用目标平移，以及反向向量分支。
长度与比例使用原生 float 边界；阈值在厘米空间解释，FBX 米坐标传入单位系数 100。
兼容骨架 remapping 不在本算子范围；调用方须先保证骨架、顺序和坐标基一致。

原始输入编译器接受上述三种新增枚举，未知枚举仍拒绝。Godot 普通精度和高精度
raw sampler 都接入新算子。旧 Animation/Skeleton 专用类保留供既有使用者。
本批未改 UE 插件、资产或 frozen JSON，也未接通 Mantle gameplay。

## 验证

- 优化 Godot 构建成功，0 警告、0 错误。
- Core 新算子及旧 raw post-process 共 19 项通过：含方向/比例改变、相反方向、
  零长度、安全倒数、缺轨/VB、不修改输入、停用和非法尾部预检查。
- Import 编译器及真实 Mantle retarget 共 87 项通过。随后补充源文件 SHA256 与
  逻辑骨顺序断言，相关 2 项再次通过；未改容差。
- 原生导出的 3 序列、60 姿态、4,740 骨 TRS，在厘米和 FBX 米坐标各比较一次：
  位置最大差 0 cm、四元数分量最大差 `2.2204460492503131e-16`、scale 差 0。
- Godot precise Overlay：36 来源、848 姿态、66,992 骨与重试一致，最大位置差
  `1.4699351635497893e-7 m`、四元数差 `4.545902364027965e-16`，scale/curve 差 0。
- Godot float Overlay：同样 848 姿态、66,992 骨，最大位置差 `6.279325e-7 m`、
  四元数差 `2.5691003e-7`、scale/curve 差 0；2,304 次采样分配 0 字节，
  4 独立所有者单线程/并行各 17,280 次采样位值一致。
- 普通入口 headless 60 Hz、8 秒、480 相机提交回归通过，覆盖移动及倒地/起身等
  原场景检查；不是 Mantle 动作或渲染观感验收。

证据在 `artifacts/mantle-retarget/`：`retarget-core.trx`、
`retarget-import-final.trx`、`retarget-hash-final.trx`、`precise-source.log`、
`raw-overlay-source.log`、`demo.log`。

失败记录：默认 `raw_animation_source_smoke` 在采样前报
`Native oracle source closure differs`（`raw-source.log`）。生产加载器当前读取
`v4_recovery_movement_source_inputs.json`，测试默认 oracle 仍是旧的
`v4_movement_source_pose_native.json`。本批未修改加载器或测试闭包断言；
不能把默认移动全资产对照算作通过。Overlay 模式对实际同一修改后的采样器完成上述验证。

## 覆盖边界与下一步

真实 Mantle 比较以 UE 导出的 raw pose 作为算子输入，比较其重定向/锁根后的
原生结果，**不是从原始关键帧开始的全链路验证**。当前这三资产的源、目标参考
姿态相同，OrientAndScale 的非零方向/比例修正没有真实资产 oracle 覆盖；
非恒等分支目前只有基于原生公式的受控测试，不声称其已获得独立 UE 对照。

下一步建立 Refactored 完整动画资产编译与真实 manifest 绑定，串起原始关键帧、
11 个 Refactored 虚拟骨、曲线、PostLocomotion slot 和 Notify，再接 Mantle 探测、
运动源身份/时钟及中断/目标失效生命周期。不得借用 V4 身份或放宽骨架校验。
默认移动测试的旧闭包也需要单独更新为有匹配原生参考的集合。

其余未完成目标继续保留：复杂相机、物理稳定性旧 9/12、Flail 0/3、完整普通场景
观感验收和最终十分钟性能预算。头颈、道具物理和音频按用户要求暂缓。
用户的计划文件与头颈诊断文件未修改、未纳入提交。
