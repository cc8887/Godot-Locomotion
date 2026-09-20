# 上身分层、Aim 与实际 Rotate 的生产对照（第 188 批）

## 结论与原规划归属

本批继续 P3/P4 的整链修复验收，将原先 MainMovement/BaseLayer 的对照
延伸至 PostLayering/PostAim。三个真实 Godot Motor 输入回放各 1080 帧，
四个阶段的姿势和共享 V4 曲线全部通过，未排除后段，未放宽门槛。
本批增加的是诊断边界、覆盖检查和真实输入场景，没有调整生产动作算法。

这不能解释为整个 Demo 已经 1:1。V4 动画资产所用图与 Refactored 后处理
存在版本差异；完整入口还通过显式参数启用，尚未成为默认 Demo。
上身、换髋、起步、脚部属于 P3/P4 当前修复范围，不推迟到高级动作之后。
P5A 通用动作、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、P6
Ragdoll/Get-up/Pose Recovery/Camera、P7 十角色十分钟验收均继续保留。
音频仍暂缓。

## 实现与对照边界

- 原 UE 探针新增 captureUpperStages，必须同时 captureStages。
  PostLayering 捕获原 AnimGraph SaveCachedPose_16.Pose；PostAim 捕获原
  LocalToComponentSpace_1.LocalPose。按完整路径、GUID、编译索引和精确
  节点类型绑定，只包裹原有的一次求值，不增加 Update 或缓存求值。
- Godot 候选帧开放分层输出，采集相同两个阶段。已有 PreFoot 名称保留；
  在当前 Godot Refactored 管线中它等于 PostAim，不是 V4 手部 IK 之后。
- V4 顺序是 Aim → 手部 IK → Foot IK；Godot 当前顺序是 Aim →
  Refactored 脚部 → 手部。仅比较真正对应的共同边界，不把后续节点
  顺序不同的姿势当作等价输出。
- 对照报告新增逐阶段误差、实际 Rotate 控制覆盖、Rotate 来源 Tick、
  Add/LS/Aim/HandIK 权重范围及 Spine 曲线存在性。
- 视觉场景新增 --aiming，走真实输入适配。瞄准场景将转镜头放在第 661
  帧（站稳后）；普通场景的输入时序不变。首次尝试在减速期间转镜头，
  未触发 Rotate，保留 movement-188-aiming 产物但不计为 Rotate 验收。

## 已完成验证

| 场景 | 四阶段帧数 | 实际动作 | 最大位置差 cm / 旋转差 ° |
| --- | ---: | --- | --- |
| movement-188-upper | 1080 | Stop 86 次、Turn 111 次求值；末尾 284 帧待机 | 0.000076289 / 0.000072647 |
| movement-188-turn-start | 1080 | Turn 中起步，再次 Stop；共 233 次 Montage 求值 | 0.000076289 / 0.000072647 |
| movement-188-aiming-idle | 1080 | Rotate 控制激活 19 帧，左 Rotate 来源 Tick 51 次 | 0.000075108 / 0.000094179 |

姿势门槛保持 0.001 cm / 0.02°，缩放 1e-5，共享曲线 1e-4；三组
共享曲线最大差 4.76838e-7，Montage 时间和权重差均为零。三组冷启动
与普通 Editor 的完整 JSON 解析后相同，不仅比较少数摘要字段。

瞄准场景实际 Rotate_L 为第 661–679 帧；ALS_N_Rotate_L90 节点从第
661–711 帧 Tick，其中 12 次发生在 Stop Montage 结束后。2637 次全部
来源 Tick 时间差为零，权重最大差 2.98024e-7。转身中起步场景 3075 次
来源 Tick 同样通过。尚未覆盖右 Rotate 或蹲姿 Rotate/Turn。

三个渲染场景各 90 张 PNG，原视觉检查均通过，最大单帧脚旋转分别
13.266°、17.104°、13.264°。抽查瞄准第 672/720 帧可见旋转前后角色
姿态，画面与采集状态对应；这不是用户人工效果验收，也不证明支撑脚
接触精度。截图 HUD 的瞬时 FPS 不用作 P7 性能结论。

新增捕获前后，基准回放原始输入、生产结果和既有阶段不变；原 UE 探针
新增两阶段前后的既有输出也保持相同。真实并行事务回归 360 帧、两次
取消、一次提交等待通过，pose=51AEDD1A58842239，result=A033A3C197DDDF19，
保持原摘要。Godot 构建、Node/Python 语法和 diff 空白检查通过。

## 尚未覆盖，不能算作移植完成

三组 Enable_HandIK_L/R 始终为零；Enable_AimOffset 始终为一，左右手臂
Add/LS 范围为零至一。Enable_SpineRotation 历史曲线未出现，因此不能
宣称手部 IK、独立 Spine 分支或完整 Overlay 已通过整图验收。

六条 Refactored 专用曲线 FootLeft/RightIk、FootLeft/RightLock、
PoseGrounded、PoseMoving 没有 V4 同名输出。有限范围报告明确列出，
不把缺失视为零，也不宣称全曲线等价。最终脚部/手部、接触与平台、
UE Character Motor 独立积分、DynamicTransitionCheck 和其他玩法通知
仍超出本次对照范围。旧 Core 23 项失败及缓存测试 79/68 骨布局失败
没有在本批处理，不计为通过。

## UE 构建与产物

完整 Editor 目标构建及四插件审计已通过，使用
ue-diagnosing-plugin-build-load 技能的工作流。

- BuildId：8531669e-23fc-4bc3-9bab-fd5876472ee1。
- 构建日志前缀：20260913T183753743Z-22886e7bf9614d2bac16547144a43df4。
- 输入指纹：78C8DA320F0E32C3ECFDA91A9C49EBDBD677676C64CF4953C362D9E6BE076903。
- AlsAimStateTraceLibrary.cpp 仓库与部署 SHA256：
  25626AD0FAD87205E8E0773A584EF39B3CE174022AB6451EE4D2658770C7E533。
- DataValidation 退出 0，0 error、3 条既有 warning；普通 Editor 仍有
  两条既有 LogAutomationTest Condition failed，不称为干净日志。
- 独立 BuildPlugin 包 artifacts/unreal/upper-full-188-package 成功，退出 0，
  用时约 2 分 9 秒。UBA 曾因低内存终止编译进程并自动重试后成功，
  不是无重试构建。打包后实际项目四插件审计再次通过，包内源文件
  SHA256 与仓库/部署相同，DLL 和描述文件已生成。

产物位于 artifacts/movement-188-{upper,turn-start,aiming-idle}/，各自
对应 -native.json、-editor.json、-scoped.json 和日志；实际 Rotate 与
转身起步的来源检查分别在 -clocks.json。并行事务日志为
graph-dispatch-188-upper.log，资产验证日志为 movement-188-validation.log。

## 下一步

继续原完整性路线：优先覆盖实际非零手部 IK/Overlay 及独立 Spine 分支，
核对曲线消费者和骨骼空间；完成最终脚部、接触、换髋、平台的整角色
配对及人工验收后，切换默认完整入口。再推进 P5A 剩余通用通知/动作、
P5B 全 Overlay/道具玩法、P5C、P6 和 P7，不继续重复已通过的普通上身
回放来代替未完成分支。
