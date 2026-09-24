# Camera 支点、平台跟随和统一候选

本批在 `.` 的 `main` 补相机空间计算层，并将它与已通过原生图对照的曲线运行时组合。普通 Demo 仍未接入，实际骨骼插槽读取、碰撞查询及穿透恢复由下一步 Godot host 实现。

## 原生顺序与实现

依据本机 ALSCamera `AlsCameraComponent.cpp` 的 TickCamera、GetThirdPersonPivotLocation 和位置/旋转/trace 辅助函数实现 `AlsCameraFollow`。全程使用 UE 世界厘米和 double 旋转/位置，曲线与时间仍为 float；不持有 Godot/UE 对象。

- 输入为已解析的两个 pivot socket、第一人称 socket、选定肩部 socket、mesh world rotation/Z scale、胶囊底部及移动基座变换。支点取两个位置均值；仅在 host 明确标记“模型 detached 且第一支点为 root”时，以胶囊底部替代 root。
- 基座身份包含对象 ID 和 bone。变化时从上一帧世界镜头/支点历史重新建立基座局部历史；同一旋转基座移动时先搬运历史，再按视角轴逐轴阻尼。输入基座需稳定身份，不能只比较矩阵是否变化。
- 第一帧禁用阻尼，随后按原生 200 cm 等资产设置判断瞬移。PivotOffset 按 mesh rotation 与 Z scale 变换，CameraOffset 按平滑后的相机完整旋转变换，二者坐标空间不能混用。
- 追踪起点在肩部和覆盖起点间混合。覆盖起点为未滞后的 pivot target + pivot offset + 世界空间 TraceOverrideOffset；最后一项不随 mesh 缩放。球半径随 mesh Z scale 缩放。
- 场景回调返回修正后的追踪起点和球心位置，允许穿透恢复改变起点。距离平滑使用修正后的起点；向内立即收缩，向外逐渐放开。这里仅消费查询结果，没有虚构 Godot 的穿透解算器。
- 第一人称部分权重混合最终位置及 FOV，第三人称路径先用可选 FOV override 再加 offset/clamp。完全第一人称则直接用插槽位置与目标视角，并按原生早返回：不查询、不阻尼、不执行第三人称 FOV offset/clamp，也不擅自重置 trace ratio 或刷新同基座局部历史。

`AlsCameraRigDefinition` 从原始 JSON 编译跟随设置与五个 socket 名称，保留 IgnoreTimeDilation 和 NativeTraceChannel。后者是 UE 枚举值，绝不能直接作为 Godot mask；时间膨胀和通道映射仍需 host 接入。

`AlsCameraRuntime` 拥有图和空间历史。Prepare 先求当前候选曲线，再执行跟随和只读场景查询；失败会丢弃图候选。Commit 同时发布图帧号、曲线和空间结果；Discard 两边都不推进。外部只能读取 committed 状态，不能单独提交内部图。它要求独占 owner，未声称同一实例可并发调用。

## 验证

- Core 相机 18 项通过，`artifacts/camera-follow-tests/follow-second.trx`；包含新增 socket/坐标轴、detached root、缩放、第一人称早返回/FOV、旋转平台搬运和切换基座、三频率碰撞/瞬移/失败重试及四元数奇异角边界。
- 随后补充调整 trace start 的测试单项通过，`artifacts/camera-follow-tests/adjusted-start.trx`。不是重新声称 19 项全套跑过。
- Import 相机 27 项通过，`artifacts/camera-follow-tests/follow-import.trx`。新增三频率共 630 帧图/空间联合故障注入，每帧异常后重试、主动丢弃重试与正常 owner 输出精确一致；前批 3,114 帧/29,701 值的独立 UE Camera AnimGraph 对照仍通过。
- Godot Optimize 构建通过，0 警告/错误。首次 Core 测试编译遇到项目 Math 命名空间遮蔽，改为 System.Math.Abs 后通过；没有因此改变断言或容差。
- 没有新 UE 导出/插件更改、Core/Import 全量测试、真实场景碰撞或渲染验收。旧原生图参考只覆盖曲线图，不是本批跟随位置层的独立原生 oracle；平台/位置层当前证据为源码对应及受控数学/事务测试。

## 后续

继续 Godot host：按实际资产解析插槽、以完成的动画/物理姿态采样、解决 native/Godot 世界坐标及 mesh 轴、读取真实基座/胶囊、球扫及初始穿透恢复。接入普通 Demo 后须验证鼠标 control yaw 不被显示阻尼污染，覆盖墙边、平台、Ragdoll/起身、换肩和第一人称多帧截图；再补完整组件原生位置对照及性能验收。

原有静态稳定性 9/12、Flail 0/3、Mantle 和最终十分钟性能预算继续保留；头颈与道具物理按用户要求暂缓。用户 P4 方案和三个头颈诊断文件未纳入提交。
