# 完整 linked Layering 原生逐帧对照

在 `.` 的 `main` 完成实际 AB_Als_Layering 图的第一组原生运行对照。本批没有修改 C# 生产混合算法。

## 原生探针范围

新增 UE 只读接口 `ExportRefactoredLayerGraphTrace` 和 `export_refactored_layer_trace.py`。使用原始生成类、节点、连接和缓存，临时 SkeletalMeshComponent/linked AnimInstance；Parent 显式指向新建的 UAlsAnimationInstance，而非修改 CDO。两个 LinkedInputPose 的缓存由真实 Stand/Crouch 原始动画采样填充，保留原始 79 骨架布局。图内两个 SequenceEvaluator 仍由原生节点自行采样。

给临时 Parent 注入全部 22 个 LayeringState float，以及 PoseState 站/蹲值；执行原 proxy Pre/UpdateRoot/Post 和 ParallelEvaluateAnimation。刻意不执行 Parent 的角色状态更新，也没有播放 Montage，七个 Slot 均走源透传。没有替换内部混合节点或把 C# 结果作为 UE 输入。

三个连续轨迹 30/60/120 Hz，各一秒，共 210 帧。六阶段覆盖 0/.25/.75/1/负数/大于1的分层控制、左右手 LS/MS 切换、站蹲比例及零总权重、linked 输入姿态互换、曲线存在性与 reset/accumulate/override。PoseState 取上一帧输入生成的最终 PoseStanding/PoseCrouching 曲线值；C# 对照使用自身已提交曲线历史。其他 LayeringState 是受控父状态，不是完整 RefreshLayering 的闭环角色模拟。

## 结果

- 实际原生图与 C# 完整图：210 帧、16590 个骨骼结果，含虚拟骨。
- 最大位置差 `5.8292005590241718E-06` cm，四元数分量差 `2.4680153887235434E-07`，scale 差 `7.4505803304703022E-08`。
- 曲线 presence 全部一致，最大曲线数值差 0。
- 预设容差位置 1e-3 cm、四元数/scale 1e-5、曲线 1e-6；未放宽。不是 bit-exact 姿态声明。
- Import Release `AlsRefactoredLayer|AlsRefactoredBasePose|AlsLayerBlending`：64 通过、1 既有条件跳过、0 失败。新增原生对照首次即通过。
- Optimize 构建 0 warning、0 error。没有新的 Godot 渲染/普通 Demo 场景验收或全量 Core/Import。

## UE 构建、启动及资源证据

使用 `ue-diagnosing-plugin-build-load` 技能流程。首次完整 Editor 构建成功，但辅助静态函数 Update 隐藏基类虚函数，产生 C4263/C4264；改名 UpdateRoot 后完整构建和插件审计通过，无该 warning。最终日志前缀 `20260924T172928030Z-c71bf62016ef4fdf9cbc3ec0104eefe6`，fingerprint `C1E13C8BE16C15A0E4427F4D37F21FB14262557178C0FCAC14AC46DF96161D8C`，BuildId `7fb8adce-a7f2-4be3-9d02-f8b2ae766ac2`。

- 冷命令行实际退出 0，无 Error/Warning，输出 `ALS_REFACTORED_LAYER_NATIVE_OK traces=3 frames=210 assets_saved=0`。
- 普通 Editor PID 11580，等待真实句柄退出 0，相同导出标记；输出与冷启动字节一致。两条既有 Condition failed 仍在，另有既有 AI/导航/材质/console/CrowdManager 警告，未宣称普通 Editor 日志全绿。
- DataValidation 实际退出 0，0 error/3 既有 warning。未执行打包构建。
- `assets/config/refactored_layer_trace.json` SHA256 `D28E20BC8F8760D480C8F0987224A879D60FF90D4085846BFD7F2FAC6C1FA3EE`。
- 请求 `refactored_layer_trace.request.json` 文件 SHA256 `0207AD35277523D3A259DB1244FCCCB649E4F8AB9A08D7B88198200BB94519A6`，其内部 requestDigest 对应不含该字段的规范请求。请求同时绑定原图、编译清单、基础姿态输入的哈希。
- 日志、重复导出和 TRX 位于 `artifacts/refactored-layer-native/`。无测试失败；首轮 UE warning 日志保留。

## 剩余工作

这个证据支持完整 Layering 混合图在上述输入范围下的一致性，不覆盖实际 Montage Slot 覆盖、普通宿主上游 Locomotion/Overlay、完整 Head/View/Control Rig/Ragdoll 或场景视觉。下一步区域 Slot 的真实分发及覆盖对照、生产 Refactored 骨/曲线布局适配与 Head/View，再完成普通 Mantle 接入。

既有物理稳定性 9/12、Flail 0/3、复杂相机碰撞、Mantle 探测/motion 生命周期和最终十分钟预算仍未关闭。用户 project.godot/P4 规划/头颈诊断/uid 保留；头颈拉伸、道具物理、音频继续暂缓。
