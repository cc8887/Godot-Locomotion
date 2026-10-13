# Mantle 门禁复核、风格反馈与模型接触适配

本轮按“门禁是否合理 → 提示词模块 → Kimodo 条件 → 目标制作与运行适配”处理。长期操作方法和可执行命令见独立 Pipeline 的[换风格制作方法](../../../Kimodo-Pipeline/docs/architecture/mantle-style-generalization.md)；门禁分类与原失败复算见[审计记录](2026-10-13-mantle-gate-audit.md)。

实现和运行使用主目录 `F:/Github/Godot-Locomotion`，制作使用 `F:/Github/Kimodo-Pipeline`。会话所在工作树没有被拿来覆盖主目录或旧资产。保留既有修改、旧失败和资产摘要，不改原2 cm蒙皮、1 mm手端、0.1°掌面、源8 cm及目标10 cm修复门槛。本任务使用显式候选包；末段另一项经用户授权的蹲停合入更新了主目录默认指针及DLL，本任务保留该变更，并在当前main上统一重验。

## 最终候选与适用范围

本轮实际生成并完成两种High文案的制作和相关游戏验证，固定`Kimodo-SOMA-RP-v1.1 / seed42 / 100 steps / 30fps`。每包将一份High作者动作交付为V4／Refactored两个身份，复用159项作者来源；目标Mantle6/6、最终FBX161/161、Godot导入／P2B／六域绑定、所选E2E8/8、静态Low11/11、平台11/11均通过。四个Low保留既有姿态，新增High使用v2帧间手部验收。完整生产范围、多seed和人工风格辨识仍未关闭。

| 候选 | 当前main扩展检查 | 实际发布帧 | 最大完整蒙皮深度 | 全帧可达残差 | 新增支撑共线 |
| --- | ---: | ---: | ---: | ---: | ---: |
| [default：13cca1702cead705](../../assets/generated/als_nonprop_mantle_refined_13cca1702cead705/resource-pack.json) | 21/21 | 7,740 | 1.6075134 cm | 0 cm | 0 |
| [cautious：3ea94c6658234d02](../../assets/generated/als_nonprop_mantle_refined_3ea94c6658234d02/resource-pack.json) | 21/21 | 7,740 | 1.6075015 cm | 0 cm | 0 |

统一报告为[`final-current-main-v7/results.json`](../../artifacts/tests/mantle-generalization-20261013-v1/final-current-main-v7/results.json)。每包覆盖1.25m／1.5m／2m静态和1.8m移动平台×双模型×Root开关的16项，另加1.5m双模型单线程2项、girl 30／120Hz两项及真实GPU一项。共42/42、15,480帧，全部运行、独立姿态审计及整段输入冻结通过。最大手端误差约6.89e-7 m、掌面误差约5.80e-5°、平台局部漂移约0.1373 mm；最小显示支撑肘弯分别约19.70／20.43°。[两风格的12张关键渲染样本](../../artifacts/tests/mantle-generalization-20261013-v1/final-style-render-review-v7.png)已查看，但不以截图或数值差异认证风格表达已足够明显。

default Air11/11。cautious首次Air的11个功能案例虽通过，但外部合入在期间改写了DLL，完整性检查明确拒绝，原报告保留。随后采用当前main DLL `1bd966e8340d296930c4dce5086bc2f81ee580ce51ea23d31aca6c90215aa81f` 独立重验11/11（含两项GPU），输入未变，报告位于制作证据`current-main-v7-air-cautious/results.json`。42项统一矩阵也绑定这个DLL，不能拿被拒绝的首次Air汇总代替它。

当前main另外进行了新default包Low的配对正例2/2及独立失锁反例2/2，见[`paired-contact-result.json`](../../artifacts/tests/mantle-generalization-20261013-v1/paired-current-main-v7/paired-contact-result.json)。关闭手锁后，人偶／girl的独立锚点误差分别9.6206／29.5497 cm，掌面误差约0.686°，均被原接触阈值正确拒绝；此时蒙皮分别仅1.7424／0.8498 cm。旧“失锁必须穿模”的两项断言因此仍为失败，未改写为通过；配对检查使用正常运行的支撑时域与锚点，不能从关闭后的零权重推导预期接触。

最终相关回归为Python42/42（几何／曲线19、整片集成3、既有手修复含不可达负例及确定性复测6、反馈评分／入口14）、Release Core528/528、Import专项28/28；v6还完成12项故障回滚、旧包基础4项、Low频率2项及GPU一项。Core／Import程序集和受检Mantle源码在外部合入前后未变。最后补充的反馈评分只增加对子帧累计预算及满支撑可达性的失败量度，并以真实preflight报告验证；已交付的几何／`check_targets`实现未变，未将这份单测描述为又执行一轮diffusion。制作端汇总见`delivery-summary-v7.json`和`post-delivery-feedback-score-change.json`。

**保留范围必须区分来源与最终姿态。** 四个Low最终轨道逐值相同；另外155项作者来源复用，但当前共同pipeline对它们重建了八条叶端twist旋转，主骨旋转、位置和缩放未变。本批密集基线及两种稀疏风格都使用这一全161项twist策略，不能称相对较早的6项twist基包“其余155姿态完全不变”。详见制作证据`default-retention-before-runtime-v6-complete.json`及谨慎包对应比较。

两包顶层仍为`pipeline_complete_rejected`：基包缺22个身份，且所选8项E2E不构成完整方向覆盖；候选没有获得整包生产资格。当前默认由用户授权的另一任务选为`als_nonprop_crouch_stop_final_20261013_v5`，本任务未将Mantle候选激活。下文保留各阶段的失败、诊断及算法演进，当前结论以本节范围为准。

## 门禁结论

接触精度、固定骨长、有限值、资源身份、最终FBX回读和事务一致性对不同风格仍必要，但不能证明关节与皮肤自然。原版Pelvis的15 cm管道、固定支撑时域、目标修复预算属于当前ALS兼容合同，不是任意风格的生理真理。本轮保留这些合同，以反馈约束使新候选重新通过，没有把旧失败改名为通过。

只读审计新增“输入仍弯曲、显示支撑臂被求解到数值共线”的拒绝项，数值容差取float32运算裕量，未强制所有动作一个固定肘角。关闭女模型手锁时，原穿模反例未触发，但独立支撑锚点测得约30.6 cm悬手；这应由接触负对照检出，不能要求每种身材都必须穿模。

## 实现

- Pipeline：`mantle_prompt.py`提供动作／接触／过渡与可选风格／修饰模块，修复Mantle硬编码忽略通用覆盖的问题，图记录最终文案、模块与摘要。
- 约束：`mantle_constraints.py`构造稀疏事件条件；`mantle_constraint_feedback.py`按连续失败区间选择极值，从候选自身最小投影姿势后重新生成。达到时间键预算后可升级同阶段已有事件，不复制原版整姿势。
- 目标：`mantle_target_preflight.py`在源通过后检查真实目标转换；目标比例失配通过保留源腕的躯干／肩／肘平面条件反馈解决，原目标预算保持。
- Core：目标真实臂长与源当前肘弯形成软姿态偏好；实际接触、原身体预算及目标身体蒙皮可行域优先。体型修正不移动胶囊、不拉骨长。
- 时间：逐手接触窗口缓存于原曲线，使用同一montage时间／rate作0.2角色秒C2身体预备，实际手权重与锚点时域不提前。释放复用Core惯性化，再投影到硬接触／几何约束。
- 事务：`AlsRetargetContactState`进入原`AlsRuntimeState`候选／检查点；恢复取消复用动画所有者信号。原姿势检查点额外保存实际显示模型的局部位置／旋转／缩放与诊断，失败后精确恢复，并参与`RollbackVerified`。Ragdoll显示不再被错误地重新作普通腿IK。

模型绑定详见[稳定契约](../architecture/als-character-model-binding.md)，控制时域详见[攀爬控制](../architecture/als-mantle-contact-control.md)。

## 当前模型修复的实际证据

证据根目录：`artifacts/tests/mantle-generalization-20261013-v1/`。这些运行显式选择既有 `als_nonprop_mantle_twist_20261012_v2`，用于隔离运行算法；不能代替新风格包的交付验证。

| 独立目录 | 结果与范围 |
| --- | --- |
| `contact-diagnostic-v1` | 只保留源肘弯后，Low／High双模型4项中High girl失败：头部约4 cm穿台。失败保留，促成加入实际身体几何约束 |
| `contact-diagnostic-v2` | 加身体几何后4/4；High girl降为0.4225254 cm，但接触建立仍有身体突变 |
| `contact-diagnostic-v3` | 加同钟身体预备后4/4，Low／1.5m High、双模型、Root关、60Hz、并行 |
| `rate-diagnostic-v1` | girl Low／1.5m High，30／120Hz，4/4 |
| `transactions-v1` | 支撑中／释放中各Root开关×单并行8/8；Ragdoll进入／活动期故障×单并行4/4，共12/12 |
| `rendered-v1` | 同v3四案例真实GPU4/4，240张PNG；[24张关键样本](../../artifacts/tests/mantle-generalization-20261013-v1/render-review-v3.png)已查看 |

上述通过目录的冻结输入均未变化。故障验证恢复68根实际显示骨骼和完整模型过渡状态，重试后清除中断接触。Ragdoll验证仍沿同一动画所有者，物理继续推进，不能把它描述为冻结整个物理世界。

Release Core专项与事务／大值类型／零分配相关测试 **473/473**，日志`core-tests-v3.log`；Import motion/root/contact/montage专项 **23/23**，日志`import-tests-v1.log`；Optimize `build-v3.log`零警告／零错误。没有将此前较广Import中的5项摘要失败改写为通过，本轮不以旧报告充当全量Import复测。

按固定旧接触事件窗口比较，实际手权重和锚点逐值保持，v2→v3驱动骨架也逐值保持。数据在 [`v3-audit/comparison-summary.json`](../../artifacts/tests/mantle-generalization-20261013-v1/v3-audit/comparison-summary.json)：

| girl观测 | 修复前／中间版本 | v3 |
| --- | ---: | ---: |
| Low由弯曲输入新增共线的单侧满支撑样本 | 17/23 | 0/23 |
| 1.5m High相应样本 | 48/99 | 0/99 |
| Low建立窗口身体修正最大单步 | v2 14.6705 cm | 2.7153 cm |
| High建立窗口身体修正最大单步 | v2 11.0131 cm | 3.6196 cm |
| Low释放窗口身体修正最大单步 | 旧11.5976 cm | 4.1819 cm |
| High左手释放窗口相应值 | 旧10.4153 cm | 3.9239 cm |

这些是额外身体适配分量的有限差分，不是总Pelvis位移或已校准的人体速度门槛。High为净空作了最大约13.77°的屈肘偏好让步，不能称为完整姿态原样保持。13,131个身体子集点无穿入，仍不能替代32,276个完整显示顶点检查。

Low完整蒙皮峰值为 **1.9963503 cm**，来自释放阶段`calf_l`，距原2 cm上限仅约0.0365 mm；30Hz和120Hz分别约0.8631／1.9960 cm。保留这一小余量，不能据此宣称所有新Low风格或任意高度都稳健通过。High三Hz所见峰值不超过约0.7145 cm。

## 真实风格生成与逐层反馈

制作证据根目录：`F:/Github/Kimodo-Pipeline/workspace/runs/mantle-generalization-20261013-v1/`。固定 `Kimodo-SOMA-RP-v1.1 / seed42 / 100 steps / 30fps`；三个初始对照为默认文案＋旧约束、默认文案＋稀疏约束、谨慎文案＋稀疏约束。轻快文案已提供可执行plan，本轮没有把它写成已生成或已验收。

默认＋旧约束源通过。两个初始稀疏结果均失败：接近阶段髋部偏差约33 cm，末支撑肩腕不可达约18 cm。按失败区间投影候选姿势、收紧内部可行余量并升级已有事件，R5两个风格通过源门禁；时间键并集仍19，fullbody从旧约束18帧降为12帧，其中10帧来自失败候选自身的投影。原手脚、身体门禁保持。

目标预检随后发现R5左腕转换后需约13.12／13.10 cm修正，超过原10 cm，旧结果明确拒绝。增加实际目标FK观察器及源腕保持的目标投影后，R6两个风格均再次通过源及V4／Refactored目标门禁；目标修正约 **9.9333／9.9464 cm**。只升级既有f48并重新投影f54，时间并集仍19、fullbody13；帧由当前失败区间自动选择，算法没有把这些帧号写成规则。

这些目标预测与实际`offline_retarget.convert`的双腕FK对照误差小于5e-13 cm。投影姿势标记为非交付条件，只有重新生成的候选进入作者／目标验收。正式无mock的SDK分支以同输入回放，生成及作者NPZ与成功实验逐字节一致；正式五轮选帧策略的十组条件也逐项复算一致。回执和代码快照保留每轮来源。

两个R6作者动作并非同一份数据：81帧／77关节的局部旋转差RMS约0.774°、最大11.685°，Root差RMS约0.317 cm、最大1.382 cm，见制作证据的`authored-style-difference-r6.json`。这些数值只证明已生成不同动作，不证明“谨慎”风格已足够明显；固定单个seed和当前时域的两份文案不能代表任意风格或多种子成功率。

最初目标反馈调用错误的两份`too many values to unpack`记录保留在`target-feedback-v1`；修正为读取实际world-pose三元组并增加双风格完整条件构建测试后，`target-feedback-v2`执行了真正R6生成。源／目标阶段的停滞评分分别管理，避免从源阶段的低错误基线误阻止目标阶段反馈。

## 交付与覆盖边界

基线候选 `als_nonprop_mantle_refined_73acd32e809f99c2` 完成目标Mantle6/6、最终FBX161/161、实际构建／导入／六域绑定，所选E2E8/8、Air11/11及既有Mantle静态／平台验收。整体仍因22个既有身份缺口和未选择完整方向范围保持拒绝；默认未切换。它是密集约束基线，不能作为两种新稀疏风格已经完成交付的证据。

两种R6风格最初续跑的源／目标预检不能代替后面的FBX和游戏结果；最终新目录及统一扩展验收见文首。原版Low自由指节、完整Overlay／地形／装备、连续蒙皮／关节形变、人工风格辨识、十分钟性能和整包生产仍有独立验收边界。

完整stage随后在R6 default的建立前双手f11–14发现新的实际目标蒙皮失败：原手臂清障已用到10 cm，仍有超过2 cm的手指／手部穿入，最大约3.77 cm。这份失败保持，不能用R6腕点预检通过覆盖。

固定指姿的Pareto／球面斜向清障搜索只解决其中一帧，不能推断全部允许自由度无解。后续实测“先有界移动手臂，再在移动后的姿势上优化指节”解决全部六个失败点：累计腕位移不超过10 cm、指节相对原输入不超过45°，Root和wrist世界朝向保持；候选的全手实际LBS深度不恶化。该末次步骤已接入`mantle_hand_repair.py`，不是固定f11–14补丁，任意帧只在既有修复仍失败时触发。两种完整81帧片段的相关回归2/2（首次约110.6秒），原6项包含不可达负例的回归6/6通过；强目标求解设置了有限尝试，另记性能telemetry。新的最终交付仍以重新执行的完整链结果为准。

## 扩展运行发现与后续修复

post-arm default续跑`als_nonprop_mantle_refined_810ab82066423f06`已完成目标6/6、最终FBX161/161、构建／导入／P2B／六域绑定、所选E2E8/8、Air11/11、Low静态11/11。平台矩阵的五个High案例仍因右手不可达拒绝，固定平台约0.9023 cm、移动约1.8113 cm；全skin通过且输入摘要未变。报告位于制作目录`target-feedback-v2/default-sparse/round-6/continuations/run-001/authoring/full-pipeline/runtime/`，不能把前面通过的阶段合并为整链通过。

逐帧检查又发现两个消费问题。物理`.94s`对应的姿态／手曲线却为`.9433333s`：预定位只减一个未乘倍率的delta，随后montage按资产RateScale推进。`AlsMantlingFrame.PositionBeforeAdvance`现扣除实际实例的EffectivePlayRate，继续使用同一个物理时钟；新增不同物理／资产倍率、30／60／120Hz及失败重试检查，时钟相关9/9通过。v4四项实际诊断时间已一致，右手残差仍约1.04／1.95 cm，证明时钟不是全部根因。

原生High montage的`ViewBlock`在`.4333–2.0s`为1，当前V4上身图却没有接到相应阻断。新增`AlsRefactoredV4ViewMask`将原曲线转换到`Mask_AimOffset`及`Enable_SpineRotation`，保持已有阻断和原反馈／提交机制。首次v5接线因基图缺少目标曲线布局而拒绝，六份失败日志保留；补齐明确布局后的v6构建零警告／错误。`.46s`脊柱局部旋转与交付raw的差异从约2.984°降到0.000020°；新包名义2m人偶皮肤峰值从5.367降到1.6075 cm。支撑残差仍约2.90／3.10 cm，继续拒绝，未增加臂长或身体修正额度。

制作侧同时复算出两个独立问题：右手f13与f14各自安全，但原raw在f13.5插值穿入6.8618 cm、f13.8为2.8059 cm；相邻帧分别选择前缘外退与向上退出，形成切角。旧身体曲线还把f74脚尖低于台面0.6245 cm转为整段33.403 cm延迟；这是内部正净空偏好与全片非回退条件的结果，不能假定与运行支撑天然兼容。原失败和只读候选记录保留，后续以通用相邻关键帧联合清障及v2身体可行域修复，最终结果见文首。

v6旧twist包Low／1.5m High双模型4/4，新增支撑共线均0，Low girl完整skin峰值为1.907897 cm；上文1.9963503 cm属于v3阶段。Low girl另验30／120Hz 2/2、GPU 1/1，峰值分别0.8495271／1.907599／1.907897 cm，[12张关键渲染样本](../../artifacts/tests/mantle-generalization-20261013-v1/low-render-review-v6.png)已查看；支撑／释放／Ragdoll的12项故障回滚全部重验通过。目录分别为`baseline-diagnostic-v6`、`low-rates-v6`、`low-rendered-v6`和`transactions-v6`，输入摘要均未变。

Core扩大到相关测试类后发现存储字段清单漏列本轮新增`ModelContactState`，补入准确字段顺序后528/528通过，日志`core-tests-v6b.log`及同名TRX；Import原专项加视线适配共28/28。当前记录不以v3证书替代新时钟和视线适配后的完整验收。
