# Transition 实际通知函数与连续 Slot 对照

## 本批实现

在主目录 `.` 的 `main` 收尾此前未完成的原生导出代码。新接口生成实际 `B_Als_Character` 和四种武器 Linked AnimInstance，调用原蓝图 `AnimNotify_RelaxedToReady` / `AnimNotify_ReadyToRelaxed`，通过真实父实例、原始 `AIS_Als_Default` 设置播放动态 Montage。

每帧先运行原生 Montage 权重/时间更新并冻结 evaluation，再以原始 Stand Pose 为输入求值真实 Transition Slot，最后调用通知函数和停止函数。记录实例身份、资源、位置、速度、当前/目标权重、混合时间、playing、Slot 权重以及 79 骨本地 TRS/曲线。导出不保存 UE 资产，也不分发资源中的音频通知。

原生对照发现共享 `AlsMontageRuntime.Begin` 拒绝 delta=0。现在允许零时间步，仍处理待重置的混合和结束实例，但不进入推进时间/自动淡出边界检查。与 `FAnimMontageInstance::Advance` 的无剩余时间路径一致。负值、NaN、Infinity 仍拒绝；加入零时间步、末端不自动停播、零混合替换、取消重试及非法输入回归。

## 验证结果

- 四种武器 × 30/60/120 Hz：12 组、3,360 帧、265,440 个骨骼样本。
- 每组创建 8 个实例，共 96 个；包含同帧重复通知、同组替换、移动/蹲伏/空 stance 门控、零时间步、默认/显式/立即停止、自然结束。多实例同时求值共 260 帧。
- 实例身份、来源、playing、播放时间、混合时间、速度、当前/目标权重及 Slot 权重一致，时间/权重最大差为 0。
- 本地位置最大差 `7.993605777301127e-14 cm`，四元数分量符号对齐后最大差 `8.049116928532385e-16`，缩放差 0；曲线存在性一致、最大值差 `1.1920928955078125e-7`。
- 预设位置容差 `2e-5 cm`、旋转/缩放/曲线/时间/权重 `2e-6`，未因失败放宽。
- 新原生测试 12 项通过，相关 Import 86 项通过，Core Montage 161 项通过；Godot Optimize 构建 0 警告、0 错误。
- 产物与初始失败日志保留在 `artifacts/refactored-transition-native/`。第一次编译测试时尚未生成参考，12 项 FileNotFound；参考生成后 12 项在零时间步入口失败，修复后全部通过。

冻结文件 `assets/config/refactored_transition_trace.json`，38,187,081 字节，SHA256：

`FC5E68CDF8C7C4631886A9C3D228711FC54F00FC6B3DF741390A98ADC70CD7E0`

## UE 构建与启动

依照 `ue-diagnosing-plugin-build-load` 流程完整构建项目 Editor 目标并审计项目插件，没有叶插件构建、DLL 复制或手改 BuildId。

首轮发现导出 C++ 的 const/non-const 成员函数重载选择错误，补显式成员函数指针类型。下一轮预检发现项目旧 receipt/插件 BuildId `4cd31a69-ae92-41ab-8118-ffba44340a1a` 与所选引擎 `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5` 不同；确认都是项目本地生成产物后，使用标准包装器隔离到可恢复的 `../AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260924T231532036Z/`。未删除源码或资产。

最终完整构建 10 actions，通过所有插件审计，fingerprint：

`A42BF62323C0023C9F77822B1D90731CA88C6E0DEE4517233F0B95D38CD06792`

冷启动导出退出 0；普通 Editor PID 36508 导出并退出 0，两份输出 SHA256 完全相同。普通 Editor 仍有两条既有 `Condition failed` 和已有 AI/NavMesh/材质/渲染警告，未声明已修复。DataValidation 退出 0，汇总 0 errors / 3 既有 warnings。项目无已配置的打包验证流水线，本批未打包。

## 覆盖边界和下一步

该对照直接调用实际通知函数，并控制父级 stance/moving 输入；不运行整个角色动画图，也不代表已验证引擎通知调度。输入门控的通知请求单独由已有 WeaponNotify 测试覆盖。Slot 上游使用固定原始 Stand Pose，尚非完整 Locomotion 输出。

本批没有运行 Godot 场景或人工视觉验收，普通 Demo 未切入 Refactored 整链。Overlay 整图仍 9/13；四武器状态源/完整姿态、worker 最后写入覆盖队列和 stopQueued 生命周期、真实移动图及统一角色宿主仍待完成。Ragdoll/Flail/Get-up/Pose Recovery 仍未整体验收，旧失败与性能预算继续保留。道具物理、音频和头颈诊断继续暂缓，用户未提交修改未纳入本批。
