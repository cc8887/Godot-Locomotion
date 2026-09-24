# Mantle 原生通知队列对照

本批补齐上一批队列实现的独立原生证据；没有修改 Godot 生产算法，也没有将 Mantle 接入普通 Demo。

## 原生取证范围

扩展既有临时真实角色探针。在原生 Montage_UpdateWeight/Advance 完成后直接读取 FAnimNotifyQueue 的 AnimNotifies、PostLocomotion 的 UnfilteredMontageAnimNotifies，以及原生随机流 seed。这里的 Unfiltered 仅指尚未按动画图 slot relevance 应用；原生服务器/权重/LOD/概率过滤已由引擎执行。

每条通知记录实际 Notify 对象路径、FAnimNotifyEventReference 的 CurrentAnimationTime 和 montage instance context。先核验 context 属于本次真实实例，再将进程全局实例号规范化为局部 1，以便重复导出字节一致。通知对象、位置或事件序列不由 C# 结果反向生成。

每帧只清空原生队列的数组并刷新 LOD，保留随机流；初始化时绑定的 World 不变。不调用脚步的音频/贴花/粒子执行，也不推进完整角色图。本批对照固定为相关 PostLocomotion slot、非 dedicated server、真实无 LOD 筛选事件；不能据此声称所有 relevance/服务器组合均完成原生对照。

六个 Montage × 30/60/120 Hz × 八种场景，共 144 条轨迹：原有自然播放、四个提前淡出条件、外部中断、四秒大步长，加上首帧两秒大步长并保持输入。新增最后一项特别覆盖 High 同帧经过第三个脚步后 EarlyBlendOut 停止实例的顺序。

## 结果

- 11001 帧的播放状态/活动状态/动作标签继续匹配原生参考。
- 306 条脚步事件的对象、顺序、所属实例完全匹配；每帧原生随机流 seed 一致，direct notify 数量一致。
- 位置、权重、目标权重、blendTime 与通知 CurrentTime 转为原生 float 后，本次最大差均为 0；数值测试容差仍为 2e-6，未放宽断言。
- Import Release Mantling/MontageNotify 定向 99 通过；Godot Optimize 构建 0 警告、0 错误。无新 Core 全量或 Godot 场景运行。
- 参考 `assets/config/refactored_mantle_branch_reference.json` 扩展保留状态字段并新增 queue/seed 字段，继续绑定 animation inputs 字节哈希。新 SHA256：`48056AE2C2F3CBC5064E2929DAEF32980A2263F411609219F6A528D5E06CDB2A`。

## UE 构建与重启

使用 UE 插件构建诊断技能要求的完整项目 Editor 构建与插件审计。首轮链接失败，因为 FAnimNotifyQueue::Reset 未从 DLL 导出；修为清空公共数组并更新 LOD，事件提取/过滤仍调用原生路径，没有替换为自算事件。

最终构建日志前缀 `20260924T153353224Z-fe0ae1a937084759a57481b05f4c4c17`，BuildId `95092497-5e8e-42c6-a8ac-62462d5e0f5b`，fingerprint `F828B170818ACB14853BFFF27B6B67FB392A4D8714BF72473A522A44187864BB`。

冷导出实际退出 0，0 errors/0 warnings；普通 Editor PID 33092 完成同样导出后实际句柄退出 0，两份参考字节一致。普通 Editor 两条既有 Condition failed 未修复。日志和 TRX 在 `artifacts/mantle-native-queue/`。辅助插件为 Editor-only，本批无游戏打包验证。

DataValidation 实际退出 0，0 errors/3 条既有 warnings（旧 PawnActionsComponent 和 navmesh），没有将这些旧警告记为本次修复。

## 后续与未完成项

下一步推进实际角色资源身份映射、PostLocomotion 图接入、障碍探测、移动目标和 MantlingRootMotionSource 宿主时序，再验收中断/销毁/Ragdoll 转换。贴花/粒子目前仅有配置引用，完整效果执行未完成；音频暂缓。

旧物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生覆盖、旧移动 oracle 闭包问题、最终视觉与十分钟性能验收仍未完成。头颈和道具物理暂缓。用户 plan、project.godot、头颈文件、既有 uid 未纳入提交。
