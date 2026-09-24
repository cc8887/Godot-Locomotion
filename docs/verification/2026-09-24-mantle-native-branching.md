# Mantle 原生状态逐帧对照

本批补上上一批缺少的独立 UE 运行时证据，未修改 Godot 生产播放算法。Mantle 尚未接入普通 Demo。

## 原生取证

新增 `ExportMantlingBranchTrace`，在临时真实 B_Als_Character 的实际 AnimInstance 上调用引擎 Montage_UpdateWeight、Montage_Advance。使用受保护成员指针访问，不把对象强转为派生探针，不替换原生状态类。状态 Begin/End/Tick 由原生 Montage 自动执行；导出每帧动作标签、ActiveStateBranchingPoints、位置、权重、目标权重、淡出时长、播放及实例有效性。

六个实际 Montage × 30/60/120 Hz × 七种场景，共 126 条轨迹、10688 帧。场景为自然播放、输入、InAir、Aiming、Crouching、第四次更新前外部 .4 秒中断、首帧四秒大步长。输入通过反射设置临时角色字段；动作通知会应用 desired stance，故每帧重新捕获测试 stance。未推进完整 Character/动画图、场景物理或 Footstep 排队通知。

这是原生状态及其副作用的逐帧结果对照，不是插桩记录全部内部回调指令；不能凭它证明任意回调跳转、循环、同帧替换或通用子步调度等价。现有两个状态与限定资源的实现边界不变。

脚本 `tools/unreal/export_mantle_branch_trace.py` 只写参考 JSON，不保存 UE 资产。参考绑定现有 animation inputs 的 SHA256；冻结文件 `assets/config/refactored_mantle_branch_reference.json` 的 SHA256 为：

`D6DAFAEB0A53861906616AF91E0F34815076F8BEDB689D59127CFCC69D395305`

## 结果

- 原生 10688 帧与共享 Montage/branching runtime 对照全部通过；位置、权重、目标权重、blendTime 转为原生 float 后本次最大差为 0（测试容差仍为 2e-6）。动作标签、活动状态集合、存活和播放状态逐帧一致。
- Import Release Mantling 定向 63 通过，0 失败；TRX `artifacts/mantle-branch-native/import-final.trx`。
- Godot Optimize 构建 0 警告、0 错误。未运行新 Godot 场景或全量 Core/Import 测试。
- 完整 UE Editor 构建与插件审计通过，日志前缀 `20260924T151440298Z-0e8e2f9b726d4459974cdbcb783b65d9`，BuildId `95092497-5e8e-42c6-a8ac-62462d5e0f5b`，fingerprint `8807CB03DA577AE981F4F27FB7BC5C3D4BFA9BEBEBB9853E03F75FAC2FDD97AE`。
- 冷导出实际退出 0，0 errors/0 warnings；普通 Editor PID 11708 重启导出后实际句柄退出 0，两次参考字节一致。普通 Editor 两条既有 Condition failed 保留，未声称解决。
- DataValidation 实际退出 0，0 errors/3 条既有 warnings（旧 PawnActionsComponent 与 navmesh）。日志在 `artifacts/mantle-branch-native/`。辅助插件为 Editor-only，本批无游戏打包验证。

## 调试记录

首轮辅助工具编译因调用受保护方法、缺少 AnimNotifyState 头文件失败；修正访问方式和 include。该轮引擎 NetCore 构建更新 BuildId，随后审计拒绝旧项目 receipt；按技能将已确认生成产物备份至 `D:/AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260924T150915037Z`，完整重建恢复，不删除源码/内容、不改 BuildId、不复制 DLL。

首次冷导出用了不存在的 LookingDirection tag 导致 ensure。核对本机 native tag 定义，改为 Refactored 的 ViewDirection，并在设置时拒绝不存在的 tag。首次 C# 对照在动作结束时失败，因为 UE 空 GameplayTag 的字符串为 None，而 Core 使用空字符串；断言显式转换此序列化约定后通过，未改变生产状态算法或测试容差。最终冷导出无上述错误。初始失败日志与 TRX 保留。

## 后续

下一步处理序列通知与资源身份映射，再连接实际角色图、障碍探测、移动目标和 MantlingRootMotionSource。排队通知收集发生在 Early Tick 前，仍需在连接时保留这个顺序。替换/销毁/Ragdoll 转换和实际视觉验收仍待完成。

旧物理稳定性 9/12、Flail 0/3、复杂相机、原生非恒等 OrientAndScale 覆盖、旧移动 oracle 闭包问题及最终十分钟性能预算仍保留。头颈、道具物理和音频暂缓。用户 plan、project.godot、诊断文件和 uid 不纳入本批提交。
