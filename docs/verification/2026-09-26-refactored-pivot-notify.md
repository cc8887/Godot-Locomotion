# 实际方向通知、Pivot 和机器权重反馈

基线 `main / 5098921`，统一主目录实施；用户漫游角色、HUD、场景及 LayerBlending 等修改保留。

## 原因与实现

此前 roadmap 把缺口描述为“源 Notify→Pivot”不准确。原 Standing 方向图六条反向过渡生成 `ActivatePivot`（notify index0），原 EventGraph 的 `AnimNotify_ActivatePivot` 调用 `GetParent().ActivatePivot()`。方向组件已有事件候选，但完整宿主漏掉了消费。动画序列无需人为添加 Pivot 标记。

新增 `AlsRefactoredPivotNotify` 校验原事件图执行链接、Parent 接收者及方向资源身份；Standing 在 Parent PostUpdate 和 QuickStop 后派发实际方向事件。按 UE `FAnimInstanceProxy::AddAnimNotifyFromGeneratedClass`→`FAnimNotifyQueue::AddAnimNotify` 直接附加的语义，生成事件不套源动画权重、随机、LOD 或 follower 过滤，也不合并重复过渡。Parent 使用原严格速度门槛，恰好200 cm/s不激活。状态机下一更新读取已提交 Pivot，再由原节点相关性回调清除；隐藏、update-only 和取消重试保留角色事务。

新原生轨迹发现第二个缺口：完整宿主仍传固定 `StandingMachineWeight=1`，首帧会选 Run Start From Walk 而非原 Run Start。UE `GetInstanceMachineWeight` 读取代理上一缓冲；PreUpdate 清零写缓冲，状态机记录本次实际图权重。Standing 现在保存实际权重，首次/实例重建为0，角色全局隐藏帧成功提交后为0，取消不改变已提交值。组件的受控输入接口仍保留，完整宿主不再信任该占位字段。

普通 Demo 已消费这些修改；诊断发布累计 Pivot 通知数及 Movement Details 状态掩码。正常输入测试增加跑动前后换向，无测试输入直接 ActivatePivot。

## UE 参考与构建

扩展已有只读 `ExportStandingHostTrace`，可选 `dispatchGeneratedPivot` 仅用于新请求；旧参考模式保留。新 `export_refactored_pivot_trace.py` 使用原 Standing、Parent 和真实蓝图通知函数，30/60/120 Hz各8秒，共1680帧。包含连续和短间隔反向、First/Second Pivot、150/199/200/201 cm/s、稀疏姿态求值；输入 Pivot 始终 false。

对照起点显式调用原 `InitializeGrounded`。原因是 UE 设置网格动画类时可能先执行静止初始化，不能把已消耗初始化标记的 Parent 与新 C# owner 直接比较；未手写状态值或修改动画资产。仍是受控 Standing 边界，不是新全角色/Grounded/Locomotion 原生轨迹。

- 最终完整 Editor target 4 actions 构建通过，四项目插件审计通过；BuildId `2192dbcd-0924-430b-9a3d-1daff6c15a77`，fingerprint `4CE25A651115C8ABA1D5AA81FCFC0A97C638C958D714D73DABA212A75B25F83E`。使用 UE 自带 .NET，未变更系统运行库。
- 最终冷启动 PID3452、普通 Editor PID37944 均退出0，生成文件 SHA256 相同：`4DE53F05E4B41723B84FFB36C3444A0B38896B3AAB80A0524F9C6597677A0840`。原生源与 UE 镜像 SHA256 同为 `53A7FB7CADA61A8F432E3DDDB1D06D111441AA543AFB83EC440785972BFA478F`。
- 最终 DataValidation PID41920 退出0，0 errors、3条旧 warning。普通 Editor 两条既有 `Condition failed` 仍在，未声称关闭。没有打包验证或资产保存。

## 验证与失败

日志：`artifacts/tests/refactored-pivot-notify/`。

- 新原生六组（独立/共享各三频率）全部通过：3360对照帧、20540骨骼姿态采样、每轨迹8次真实方向通知。最大姿态位置差约6.77479e-6 cm、曲线2.38419e-7、clock/weight5.96047e-8，已比较Parent字段误差0，Pivot及机器状态完全同。保持位置2e-5 cm、其余2e-6门槛；通知帧及每17帧取消重试，原生输入不由预期结果反向驱动。
- 三频率完整 Locomotion 事务1680帧逐帧取消重试通过；最终 `pivot-regression.trx` 相关56项全部通过（含新10项、旧 Standing 原生六组及隐藏权重提交/取消门禁），0失败0跳过。最终 Optimize 构建0警告0错误。
- 普通30/60/120 Hz分别560/1120/2240帧通过，各实际4次方向通知、Movement Details mask63（六状态）、Locomotion mask31。60 Hz实际渲染1120帧通过，保留23张截图，连续检查850/885/895/915帧跑动及前后换向姿态；未见身体爆散，但截图不构成滑步或原版观感验收。
- 十角色完整脚部链路 single/parallel 各3621帧通过，均含2次取消、1次提交保持、520空中帧、600蹲姿帧、961足锁帧及6544次射线。姿态 `1F64623D69FD84A1`、根运动 `8B8AAD0E466EA3B5`、结果 `244477B613EB0601` 摘要完全一致。上述最终 Godot 日志无 ERROR/WARNING/WARN、BodyHistory 或异常记录；未进行全仓库回归、十分钟性能或人工验收。

保留失败：首次 UE 导出器误用203作为 Details machine，被类型检查拒绝，按实际compiled property117修正；首次新原生六组暴露固定机器权重错误；修正后六组暴露 UE 网格启动已更新 Parent 的起点差异，原输出留为 `pivot-before-boundary-reset.json`，改用真实初始化回调后原门槛全过。首 Demo 测试使用步行反向，仅通知发生而未进入跑步 Pivot，改为实际跑动换向后覆盖完整；没有放宽断言或修改生产速度门槛。

## 未完成边界

本批关闭的是生成方向通知/Pivot 和机器权重反馈，不是所有 Notify。序列通知仍有旧兼容时钟；真实 Rest 足锁/目标反馈、MovingSmooth 设置、平台相对位置、组件变换惯性、其余主图连续原生对照、上身/Overlay/最终脚部、完整动作/物理恢复/相机矩阵、十分钟性能与可复现交付继续按 ROADMAP R2–R7 推进。音频、道具物理和头颈专项仍暂缓。用户修改未纳入本批，不 push。
