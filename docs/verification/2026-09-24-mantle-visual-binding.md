# Mantle 到实际蒙皮骨架的适配

日期：2026-09-24。主目录 `D:/GodotALS`，分支 `main`。

`AlsMantlingVisualBinding` 将已完成的原生厘米姿态转换到 FBX 骨局部坐标
`P=(X,-Y,Z)*.01, Q=(-X,Y,-Z,W)`，在 Main 写入实际 Skeleton3D。
它按骨名和父级绑定全部 68 个实体骨，并检查源参考姿态与实际模型 rest 的兼容性。
位置/scale 容差 `.0002`、旋转 abs(dot) 偏差 `1e-5`；这是导入/rest 兼容检查，
不是任意体型重定向。超出范围明确拒绝，不能宣称支持不同骨架比例的模型。

Refactored 的 11 个虚拟骨仍由自身姿态求值器维护，不把它们当成 V4 虚拟骨。
本绑定只输出蒙皮所需的实体骨，要求目标正好是完整 physical skin skeleton。
不生成 V4 动画图的逻辑姿态缓冲，也不替代图中虚拟骨、插槽或 IK 处理。
宿主使用前必须停用原来的动画写入者，避免两套系统争写。

构造和 Apply 均限制在 Main；Apply 检查节点存活/树内状态、骨数、骨名和父级，
在全部实体姿态验证/转换后才写场景。位置米制转换尽量在 double 中完成，
四元数在 float 输出边界归一化，scale 保持独立局部通道。
`AlsMantlingPoseSource` 新增只读 Parents/ReferencePose，未改变关键帧求值。

## 实际 Godot 验证

新增 `scenes/tests/mantling_visual_smoke.tscn`，加载实际 Mannequin 和 AnimMan，
三段真实 Mantle 动画各采样 31 个时刻（含首末），共 186 个模型姿态。
实际 Skeleton3D 的 component-space TRS 与源姿态独立层级组合后换基的结果比较：
12,648 骨全部通过，最大位置误差 `5.749995e-7 m`。
这验证场景写入/坐标/层级适配；参考使用已通过 UE 对照的生产 sampler，
不是新增原生整场景或皮肤顶点 oracle。

24 项边界检查通过：每个模型/动作检查 worker 禁止写入、最后实体骨出现 NaN 时
即使前面的 root 有新值也不部分写入、改名后拒绝旧 binding、错误 rest 拒绝新 binding。
最终 headless 日志 `artifacts/mantle-visual/trs-final.log`，退出 0。

渲染模式也完成相同数值检查和截图，日志 `artifacts/mantle-visual/final.log`，退出 0。
已查看 High 动画 20%/50%/80% 的双模型截图：

- `artifacts/mantle-visual/final/mantle-0.png`
- `artifacts/mantle-visual/final/mantle-1.png`
- `artifacts/mantle-visual/final/mantle-2.png`

截图显示举臂、前倾支撑、收腿阶段，模型蒙皮随姿态变化；这是独立锁根展示，
没有障碍物、胶囊位移或 Mantle 运动源，因此不作为完整攀爬观感验收。
初版取景较远，最终只调整诊断相机得到较清晰截图，未改动画数据。
后补 scale 断言的最终 headless 复跑通过；截图来自此前已含位置/旋转检查的渲染运行。

Godot 优化构建最终 0 警告/0 错误。首次编译误写 C# Basis.GetScale，改为 Scale
属性后通过；本地 scale 按列构造，输出不依靠分解推断 signed scale。
无 UE 修改/启动、无新 Core/Import 全量、未改变普通 Demo 入口。

## 下一步和保留项

继续 PostLocomotion slot、Notify/Notify State、完整动作资源绑定，再把 pose、curves、
motion 接入真实探测、动作身份/时钟、移动目标和中断生命周期。
普通入口 Mantle 尚未完成；本视觉 binding 还未用于正常角色的最终播放图。
旧移动 oracle 闭包不匹配、非恒等 Orient 原生覆盖、旧物理 9/12、Flail0/3、复杂相机、
最终视觉及十分钟性能预算继续保留。头颈、道具物理和音频继续暂缓。
用户 plan/project.godot/头颈诊断/.cs.uid 修改均保留，不纳入本次提交。
