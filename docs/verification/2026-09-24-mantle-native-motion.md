# Mantle 原生 RootMotionSource 对照与配置绑定

本批在 `.` main 完成；普通 demo 的攀爬生命周期仍未接入。

## 实现及独立参考

新增 Editor-only `ExportMantlingRootMotionTrace`：创建 transient 的实际 `B_Als_Character`，加载 7 套原始设置，播放真实 montage，直接调用 `FAlsRootMotionSource_Mantling::PrepareRootMotion`，读取 root motion、实际 Montage_GetPosition，并把返回速度/旋转应用到原生角色。无资产保存。锚点初始化按原角色代码的原生 FTransform 路径与 FRotator 存储建立。

7 设置 × 30/60/120 Hz × 3 模式 = **63** 条轨迹、**4,536** 帧。三模式是静态目标、运动基座、运动基座且 simulation delta 为 movement delta 的一半。每条记录一秒物理时间，基座平移/yaw/pitch/roll，固定非均匀 scale；再加零 delta 和目标弱引用失效，共 **126** 个清空样本。失效通过清除 source 的 weak reference 构造，尚非完整目标销毁事件→Ragdoll 流程。

生产数据单独从实际 montage 属性反射导出：6 个 BlendIn 均 **0.20000000298023224 秒 / HERMITE_CUBIC**，没有 custom curve。`AlsMantlingMotionCompiler` 绑定已有真实选择图、7 设置、原始 root 轨道和这些 blend 参数，生成可选择的 motion definition，按高度计算 start/duration/warp 设置。完整资源集合不匹配、重复、未知 option 或负时间会拒绝。

生产编译不读取 native motion reference。测试从初始姿态开始，之后一直使用 C# 自己的 time 和 actor history，不用参考输出逐帧重置。

## 结果及首轮失败

固定比较门槛：位置 0.001 cm、速度 0.01 cm/s、旋转 dot 偏差 1e-7、seek 1e-6 s。

| 指标 | 实测最大偏差 |
|---|---:|
| 初始锚点位置 | 4.2632564e-14 cm |
| 连续角色位置 | 2.7723483e-5 cm |
| 根运动速度 | 0.0033268178 cm/s |
| 旋转增量 dot | 6.6613381e-16 |
| 连续角色旋转 dot | 4.4408921e-16 |
| montage seek | 0 s |

首轮 4 过 / 1 失败，旋转 dot 偏差 1.51198047e-7。诊断记录累计第 126 帧 actor quaternion norm²=1.0000003023961177，而原生 delta norm²=1。原因是测试回放宿主累计四元数乘法未恢复单位长度；本机 `SceneComponent.cpp::InternalSetWorldLocationAndRotation` 经 Rotator cache 重建旋转。修复测试宿主写回为单位 quaternion，未改 kernel 算法、原生参考或门槛。后续正式宿主也必须保证输入 actor rotation 为单位 quaternion。归一化不是 native Rotator cache 的逐位复制，仅在当前轨迹内满足比较标准。

最终 Import Release **20/20**（本批 5 + root 7 + settings 8），日志 `artifacts/mantle-native-motion/mantle-native-motion-verified.trx`；首轮和诊断 TRX 保留。Godot Optimize 构建 0 warning / 0 error。Core kernel 未修改，本批未重跑 Core 全量或 Godot 场景。

## UE 构建与运行

按 ue-diagnosing-plugin-build-load 技能走完整 Editor 目标构建/审计。首轮辅助代码出现共享引用调用错误和 unity 匿名辅助函数重名，已修正。该失败构建还重建了引擎 NetCore 并更新 engine BuildId，旧项目 receipt 不匹配导致下一次预检拒绝；未手改 BuildId。

确认生成产物后通过 wrapper `-QuarantineStaleArtifacts` 恢复，旧项目 receipt、受影响插件 DLL/PDB/modules 被移动到 `../AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260924T132927015Z`，可恢复。之后完整目标成功。

- 最终构建日志前缀：`20260924T132928312Z-fd6863bf273d496c8b774e15b59ba8eb`。
- BuildId：`10fe1ab8-6888-437e-a5a8-0b6e73b8ae05`。
- 审计 fingerprint：`D98F80146ED037C94A3980B638367A792469BCD21021066BC657DD39A9FA9EC9`。
- 冷导出退出 0、0 error/0 warning：`artifacts/mantle-motion-first.log`。
- 普通 Editor PID 18580 完成导出、脚本请求退出，日志以正常 Exiting/Log file closed 结束，进程已消失；GUI 启动命令没有保留最终进程退出码，不能把启动 shell 的 0 当最终退出码。日志仍有两条既有 `LogAutomationTest: Error: Condition failed`，未称已修复。
- 冷导与普通 Editor 的参考 JSON 及 BlendIn JSON 分别字节一致。
- DataValidation 退出 0，0 error / 3 既有 warning（PawnActionsComponent/导航网格版本），日志 `artifacts/mantle-motion-data-validation.log`。插件为 Editor-only，本批没有游戏打包验收。

冻结文件：

- `refactored_mantle_motion_reference.json` SHA256 `336B98C0818895E8C06ABAE41E83B3D961D66E4D87B10C9D0FD1D495EECBC883`。
- `refactored_mantle_blends.json` SHA256 `AB74D8B6222F60DE44002A1ADF9F97EC99E458B48DE51E03AAA0C061103E1276`。
- 参考包含旧 settings/root 与新 blends 的字节摘要，测试会验证依赖一致性。旧根骨与设置文件未修改。

## 未完成范围

这是一秒控制轨迹内的 source 计算验证，不是完整 CharacterMovement 碰撞、网络预测、动作结束、动画图/Notify、可玩攀爬或视觉等价。实际自定义重力、锚点极端旋转、任意压缩 codec 仍未由此次原生轨迹证明。

下一步把 motion definition 接入具备动作/目标身份的宿主时序：完整 Mantle pose/slot/notify 绑定、障碍探测、运动基座、montage seek 的动画更新顺序、起止/中断和销毁转 Ragdoll；然后普通 demo 验收。

旧物理稳定性 9/12、Flail 0/3、复杂相机/视觉验收、整体角色物理缩放和十分钟性能预算继续保留。头颈和道具物理暂缓；用户 P4 修改与三份头颈诊断文件未修改或提交。
