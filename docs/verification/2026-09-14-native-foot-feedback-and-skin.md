# 原生脚锁反馈与实际蒙皮鞋底对照（第 193 批）

## 结论与范围

同一份 V4 pre-rig 动画和角色移动输入下，原 ALS-Refactored 的连续脚部
状态、原 CR_Als 完整 VM，以及原 UE Mannequin 网格的蒙皮结果，均与
Godot 通过对照。原生连续链并没有消除倾斜平台穿地：右鞋底最低
-22.438935 mm，Godot -22.437513 mm；两边 120 个锁定窗口帧中都有
101 帧超过 5 mm 穿地。

这排除了本场景中“因没有保留原脚锁历史导致穿地”和“Godot 蒙皮转换
造成约 2.24 cm 穿地”的解释。不能反向宣称完整原版 ALS 示例本身已经
复现同一问题：这里仍使用捕获的 V4 动画、Motor/平台输入，没有运行
Refactored 原 AnimBP/CharacterMovement 整角色，也没有配对 11 根 V4
虚拟骨。下一项应核实版本组合和原版输入，而不是盲目调节已一致的公式。

本批继续原 P3/P4 完整性路线，没有把 P5A–P7 或默认完整 Demo 标成完成。
本批没有改变生产动画算法、权重、输入/镜头映射或接触验收门槛。

## 完成的实现

MovementPlatformGraphSmoke 的显式 RigCapture 新增真实 MotorInput、
Movement 属性、上一帧最终曲线、本帧最终曲线和最终局部姿势。取值全部
来自已提交的生产 GraphCapture；采集开关仍是 production-graph-capture。
倾斜新捕获与第 192 批旧字段逐帧相同（新增字段和进程局部平台 ID 除外），
原无约束漂移失败仍是 11.734021 mm。

ReplayRefactoredFootRig 增加默认关闭的 bClosedFeedback 参数。开启后：

1. 创建真实 USkeletalMeshComponent 与 UAlsAnimationInstance，绑定原
   V4 Mannequin 参考骨架，由原 NativeInitializeAnimation 初始化腿轴。
2. 每个连续帧保留原实例 FeetState，不注入 Godot 的 Previous 锁状态。
   按同份 Motor/平台采样更新组件、运动门控与基变换；随后执行原
   RefreshFeetOnGameThread、RefreshPose、RefreshFeet、GetControlRigInput。
   实际用于 VM 的锁目标、有效性和 pelvis allowance 由这些原函数生成。
3. 执行原 CR_Als，使用真实原生平台碰撞。将本次原 Rig 输出写入网格
   组件的读取姿势，由下一帧真实 GetSocketTransform 读取；将 Rig 曲线
   写回原 AnimationProxy 的属性曲线历史。没有逐帧用 Godot 锁结果覆盖。
4. Probe 使用单缓冲姿势发布来明确确定性帧边界，不模拟渲染线程调度。
   原生足部函数和 Rig 数学保持原样；帧重置仅第一帧 pending。地面模式
   之外立即拒绝，不伪造空中预测或假称完整空中消费者已覆盖。

速度/输入和平台状态仍由受控环境提供，因此是“脚部连续反馈”验证，
不是原 UE Character 完整物理模拟。曲线专项只检查六条实际消费者
FootLeftIk/FootRightIk/FootLeftLock/FootRightLock/PoseGrounded/PoseInAir
的数值；其原 GetCurveValue 缺失返回零，不据此宣称全部曲线 presence
合同一致。

## 原网格蒙皮对照

新增原 UE LOD0 鞋底观测，调用引擎自身 CacheRefToLocalMatrices 和
ComputeSkinnedPositions，不复写 Godot 的蒙皮公式。从原 SkinWeight
和 section BoneMap 选择 foot/ball 总权重至少 0.99 的顶点，固定保留
参考姿势最低 4 mm；验证完整网格的参考姿势重建误差不超过 0.1 mm。
不启用 morph、物理变形或额外接地控制。

每侧原 UE 与 Godot 都选到 83 个点。每帧转换到同一实际平台的顶面
坐标系，做双向最近点几何比较，门槛 0.1 mm；这不是按顶点编号强行
配对，也不把几何通过解释为接触通过。阴性点足以证明穿地，正性点
不证明整个鞋底或负载支撑完整。

| 场景 | 左/右最大点集距离 mm | 原 UE 左/右锁定最低 mm | Godot 左/右锁定最低 mm | 原 UE / Godot 超过 5 mm 穿地帧（左、右） |
| --- | --- | --- | --- | --- |
| 倾斜/升降 | 0.029299 / 0.029816 | -6.084372 / -22.438935 | -6.084681 / -22.437513 | (30,101) / (30,101) |
| 水平旋转 | 0.048592 / 0.055435 | +2.179668 / -0.368824 | +2.182066 / -0.369817 | (0,0) / (0,0) |

两组各 360 帧。原 UE 新增 CPU 蒙皮观测前后的骨骼、脚锁、曲线、
变量及输入输出全部逐帧完全相同，说明观测没有改写求解历史。

## 连续脚部数值验收

- 两组各 359 个有效脚部帧，原生左/右满锁覆盖 213/204 帧。有效性、
  首次有效、锁量、世界/平台/组件/最终锁目标、实际 socket 采样、
  pelvis allowance 和六条曲线的上一帧/最终值全部通过。
- 倾斜锁状态最大位置差 0.000061274 cm；水平最大 0.000143993 cm。
  对照门槛仍是 0.001 cm / 0.02°，锁量 1e-6、曲线 1e-4。
  原脚锁读取的基变换也与实际平台核对：倾斜各帧原点误差为零、
  最大旋转差 0.000002958°，没有用上一帧平台作为通过替代。
- 全 68 根已映射骨的完整 Rig 输出通过：倾斜最大
  0.000042211 cm / 0.000059151°；水平最大
  0.000146875 cm / 0.000149681°，缩放差为零。没有排除异常帧。
- 受控旧入口继续通过；最终普通 Editor 的受控入口与此前冷启动结果
  完整相同。两组最终连续反馈/鞋底结果也分别与普通 Editor 完全相同。
- Godot 水平平台场景退出 0；倾斜仍按原门槛退出 1，漂移
  11.734021 mm。没有删除断言、重新定义支撑窗口或关闭脚部消费者。
- 本批 Godot 构建 0 警告/0 错误；Node/Python 语法与 diff 空白检查
  通过。生产算法没有变化，没有把上一批十角色/20 项专项重复记为
  本批新测试。旧 Core/骨架布局测试债务继续保留。

## UE 构建与产物

按 ue-diagnosing-plugin-build-load 技能完成完整项目 Editor 构建与四个
项目插件的审计。最终日志前缀
20260913T211327714Z-5cc11c66f2eb440a9fa5d031f3f8b464，
BuildId=36a817f8-73f7-45a0-9324-c15ac22b7443，
state fingerprint=822D43C17DF544D93A8D2FB961F2706760B1A5C74DE7600B8E5CC351A242BCA5。

最终 AlsFootRigReplay.cpp SHA256：
DF7F6A1324ED3F56D9496E84E9CE9F1F5C47BE5E78BB854727328AA9E1A94DFC。
头文件 SHA256：
71EA6A976F54A4CC832A3631F1E98E1DD0297F62742D4D5C0809FE4F2A50EC9E。
两次完整构建均退出 0，本批没有隔离、删除或移动旧编译产物。

冷 commandlet 退出 0、0 error/0 warning；普通 Editor 用实际进程等待
捕获退出 0，三组结果均完全一致且 assets_saved=0。普通 Editor 既有
两条 LogAutomationTest Condition failed 单独保留，不以导出成功声称
全部日志干净。DataValidation 退出 0、0 error/3 warning，仍为原 AI
PawnActionsComponent 缺失与旧 NavMesh 警告，没有改写这些资产。

含 ALS 依赖的独立 BuildPlugin 打包退出 0，BUILD SUCCESSFUL，耗时
2 分 19 秒，输出 artifacts/unreal/foot-feedback-193-package。UBA 曾因
机器内存压力终止并重试一次编译，最终 UBT/UAT 都成功；不能把这个
打包过程作为 P7 运行性能证据。打包后引擎/项目 receipt 的 BuildId
一致，仓库/部署/包内的 probe 源码 SHA256 三者一致。

主要产物：artifacts/platform-193-tilt.json、horizontal.json；原生连续
结果 closed-skin.json、horizontal-closed-skin.json，对应 editor-skin.json；
skin-comparison.json、horizontal-skin-comparison.json，以及
closed-skin-feedback.json、closed-skin-rig.json 和对应 horizontal 报告。
早期不含鞋底的原生结果仍保留。

## 下一项

1. 保持同一网格、同一平台运动与相机/输入基准，对比原版 Refactored
   动画与当前 V4 pre-rig 来源，并区分静态坡面、水平转台和持续倾斜。
   对照 IK target 骨、参考足高、原曲线生成/混合位置与锁定瞬间，确认
   两版组合差异或原版行为限制，不能仅凭当前受控链宣布任一方正确。
2. 若确认数据适配缺失，补到正式来源/骨架/曲线合同；若确认原版在
   相同原生输入下也有边界问题，明确记录后再形成可对照的修复，不能
   偷加脚底偏移或把接触失败变成通过。
3. 继续起步/停止、换髋和上身的整角色人工与多帧验收，完成默认完整
   入口；随后原 P5A 通用动作、P5B 全 Overlay/道具、P5C 动作运动、
   P6 物理恢复/Camera 和 P7 十角色十分钟性能。音频继续暂缓。
