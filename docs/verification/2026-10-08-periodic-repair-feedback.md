# 周期修复、Kimodo 回灌与 Walk 运行闭环

**前进 Walk 已完成受控闭环：离线修复、真实 Kimodo 回灌生成、原生 UE 重定向、目标骨架校正、普通 Godot 移动和四种支撑相位松键均已执行。** 最终 G 候选四相位全部通过，共原版／候选 8 个进程、4800 帧。该结果不等于整套非道具资源通过；后退／纯侧移仍有滑步，Run／Crouch 新母动作尚未完成。

[最终汇总](../../artifacts/tests/2026-10-08-periodic-repair-v1/closure-final.json)、[右脚支撑渲染对照](../../artifacts/tests/2026-10-08-periodic-repair-v1/right-lock-g/summary.json)、[其余三相位](../../artifacts/tests/2026-10-08-periodic-repair-v1/walk-phases-g/summary.json)。G 仅作为候选使用，未激活；默认指针仍为旧 v8，根运动仍默认关闭。完整 71 项本批未执行。

## 实际链路与候选选择

1. 从上一批真实 Kimodo Walk 候选 A 中选择有左右支撑的观测周期，对整周期局部旋转残差和髋部残差做周期 Fourier 拟合。使用实测可信接触区间、世界脚锚、固定骨长的两段腿 IK 和有限骨盆补偿进行修复。输出 B 明确标为确定性周期构造，不冒充模型自行生成了五个稳定周期。
2. B 通过独立作者检查。采用四阶拟合的最大姿态拟合变化约 7.44°，最大脚踝修正 3.46 cm，最大骨盆下调 3.39 cm。检查阈值没有放宽。
3. 从 B 构造周期边界邻域和左右脚锚，真实执行 Kimodo-SOMA-RP-v1.1、seed 42、100 steps、5 秒生成，得到 C。沿用同一组确切提示词的原生 embedding 缓存，以 CPU 推理避免重载完整文本编码器。C 也通过作者检查。
4. 对 C 再修复所得 D 没有通过作者检查，保留失败报告。选择器先要求作者门禁通过，再比较最差一侧的近地滑移中位数，本批选择 B；C 仍是通过的备选，未被误报为失败。选中的 B 进入目标骨架和运行时验证。
5. 实际 UE 5.8 Commandlet 首批重定向四个 Walk／Pose 身份，再重出当前普通行走关联的十个 Overlay／风格姿态表，共十四个身份。跳跃依赖尚未重生成，不将这批称为完整依赖集合通过。

对应证据：[B 修复报告](../../artifacts/tests/2026-10-08-periodic-repair-v1/walk-native-rest-k4.json)、[C 真实生成](../../artifacts/tests/2026-10-08-periodic-repair-v1/feedback-generation/)、[C 作者检查](../../artifacts/tests/2026-10-08-periodic-repair-v1/feedback-generated-audit.json)、[D 未通过的报告](../../artifacts/tests/2026-10-08-periodic-repair-v1/feedback-final-report.json)、[DAG 复用真实输入的执行](../../artifacts/tests/2026-10-08-periodic-repair-v1/feedback-dag-selected/)。DAG 复验使用本批实际生成文件作为显式绑定，不宣称该次复验又重新执行了推理。

## 新增实现

- `periodic_repair.py`：事件选周期、全周期拟合、接触计划、脚锚 IK、平滑骨盆补偿、姿态与位置修正上限，以及独立验收。超过可达性／修正上限时失败，不拉伸骨骼或修改检查阈值。
- `offline_repair.py --backend periodic`：完整命令入口，保存修复结果和失败证据；通过后可生成带周期邻域的反馈条件。原 native backend 继续保留。
- `gait_repair_feedback_rules.json`：显式串联 discovery → periodic_repair → 约束再生成 → gait_selection → gait_cycle。仅通过的参考才能回灌，选择结果和未选候选的检查结果均保存。
- `target_cleanup.py`：当前用于前进 Walk，按通过的作者接触计划做受限目标骨架脚锚／足底方向校正，保留骨长，重新生成 IK 辅助骨，再通过独立原版协议检查。记录目标相位偏移，不能将源骨架通过直接当成目标骨架通过。
- 起停检查脚本增加方向参数，沿普通输入执行前后左右和斜向。它仍是现有 71 项之外的专项证据，不能替代完整发布矩阵。

## 闭环发现并修复的三类工具链／契约问题

### 作者足底校准使用了错误的坐标基底

新增作者检查器此前将 `standard_t_pose_global_offsets_rots.p` 当作物理静止姿态。它实际参与 BVH 导出坐标转换；SOMA 的 `neutral_joints` 与原生 identity 局部旋转才是该 FK 的静止几何。

错误用法甚至会把脚跟参考平面放到踝关节上方。已改为原生静止几何，并将检查策略版本改为 `soma-gait-authoring-v2-native-rest`。旧源级报告保留为旧版本证据，不能继续当成当前检查结果；既有目标骨架／Godot 蒙皮测量不依赖该错误代理。

### UE 增加末键后，按键数归一化破坏了姿态表

四帧 Stand／Walk／Crouch 姿态表经过原生导出得到五个采样键。旧发布逻辑把五键归一化成四键，使 walking 槽位采到约 1.333 帧，混入 crouch，部分腿部旋转相差数十度。

`sample_native_track` 现在按原生采样时间读取作者帧，保留离散槽位，额外尾键不挤压其他帧。真实数据对照中，Default walking 槽位与对应作者 Walking Pose 的腿部旋转差恢复到约 1e-14° 数值误差，位置差小于 0.00001 cm。增加了多余末键、不同 FPS、缺失尾部时间域的回归。

### Overlay 下半身基底与加法锁脚控制被错误制作

原 Refactored Layering 的基底是 Stand／Crouch。原版 Default 的移动槽位下半身与 Stand 相同；生成流程却把抬脚中的 gait Pose 全身放进该槽，导致右腿被持续叠加抬起。现有 graph 和发布组装均保持移动 Overlay 的 Stand 下半身，只把移动风格用于上半身；不替换实际移动循环。

扩大到四种松键相位后，又发现停止动画的加法 `FootLock` 被统一覆盖成 0～1 的高度置信度。原版存在负值解锁区间和延迟的正值锁定窗口；覆盖后会过早锁住摆动脚。当前保留加法控制曲线在原时域中的有符号窗口，后续新过渡动作仍需独立相位合同。修复后右脚支撑松键也通过，没有改运行时锁脚算法。

## 普通 Godot 的实际结果与截图

G 候选覆盖 Left Lock、Right Lock、Left Plant、Right Plant 四种实际输出曲线窗口。每段包含起步、稳定行走、松键和停止后观察，并独立要求双脚有效近地样本。

- 四相位原版／候选共 4800 帧全部通过。
- 右脚支撑的渲染对照共 91 张截图，请求帧、提交／显示、骨骼、实际输入均匹配。
- 新 Walk 稳态近地足底滑移中位数约 1.05～1.06 cm/s；当前严重回归拒绝线仍为 `max(25 cm/s, 原版同条件中位数 × 2)`。该门槛不是最终美术验收标准。
- 所有实际可见帧继续执行根运动 OFF、身体原点和接触检查。冷初始化显示规则沿用已有实现，没有隐藏后续失败帧或放宽穿地标准。

![原版与修复 Walk 的移动和松键](../../artifacts/tests/2026-10-08-periodic-repair-v1/walk-closure-comparison.png)

上排原版、下排 G 候选。稳定行走列使用同一物理帧；两列松键截图按各自实际释放事件的相同偏移对齐，因此帧号略有不同。图片仅固定裁剪／缩放，没有编辑角色姿态。[原图与帧来源](../../artifacts/tests/2026-10-08-periodic-repair-v1/walk-closure-image-sources.json)

## 仍未通过的范围

方向专项在 F 版执行：左前通过；后退、纯左、纯右的稳态中位数约 55.4、158.3、160.5 cm/s，均失败。G 修改的是加法锁脚控制，未重制这些方向母动作，不能据此宣称方向问题已关闭。

已从原版支撑区间量出方向契约：`Left_Forward`／`Left_Backward` 都是近乎纯左向行进，差异主要是髋部约 +46°／−46° 的朝向；右侧对应镜像。旧生成配方把它们解释成对角行进，是明确待修项。[原版方向数据对照](../../artifacts/tests/2026-10-08-periodic-repair-v1/direction-role-audit.json)

Run／Crouch 的新周期修复诊断仍未通过。低步幅 gait Pose 的完整语义改造、跳跃／停止动作的重新生成、所有方向的相位与步幅校准、完整非道具 71 项和最终人工观感验收仍开放。Blender 可视化修复后端尚未实现；本批使用 NumPy／SciPy、现有 IK 求解与真实 UE／Godot 完成验证。

所有中间失败包 A～F、日志和报告保留。最终 93 项 Python 回归通过，Optimize 构建零警告／错误；v8 的 815、v11 的 816、G 的 820 个包内 payload 哈希均通过检查。见 [测试日志](../../artifacts/tests/2026-10-08-periodic-repair-v1/tests-delivery.log)、[构建日志](../../artifacts/tests/2026-10-08-periodic-repair-v1/build-final.log)、[完整性检查](../../artifacts/tests/2026-10-08-periodic-repair-v1/asset-integrity-final.json)。未提交或推送。
