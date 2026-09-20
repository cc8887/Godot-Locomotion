# V4 源曲线进入 Refactored 输入链

日期：2026-09-13，第一百七十七批。原 P4 脚部输入完整性补完。

## 数据适配与写入位置

当前使用 V4 动画资产，不能假定它们具有 Refactored 资产的曲线命名。
正式 manifest 的 126 个动画中，Mask_LandPrediction 存在于 4 个动画，
FootLock_L/R 各存在于 15 个动画；对应新名字均未导出。

| V4 作者曲线 | Refactored 输入 | 语义 |
| --- | --- | --- |
| Mask_LandPrediction | GroundPredictionBlock | 预测屏蔽量，预测算法消费时使用 1-clamp01 |
| FootLock_L | FootLeftLock | 左脚锁定量 |
| FootLock_R | FootRightLock | 右脚锁定量 |

锁定参考的 AlsAnimationInstance.cpp 第 1156、1317、1325 行分别消费上述
Refactored 曲线。映射仅表示所选 V4 资产数据的用途对应，不表示两个版本
的预测/锁脚算法或作者曲线内容完全一致；独立 Refactored 算法保持不变。
Enable_FootIK_L/R 在动画源中没有作者曲线，不增加虚构的源曲线映射。
它们对应的 Refactored IK 量继续由已绑定的动画图写入点产生。

新增 AlsRefactoredV4SourceCurves，只对已验证的 V4 资产命名空间绑定三组
CurveId。原曲线仍保留，缺失源保持 absent，负值保持原值，重复作者曲线、
新旧名字冲突和错误资产命名空间拒绝。只有输出布局请求新名字时才启用。
目标名列表只读，采样热路径不进行名字搜索或新增分配。

float 与 double 原始采样器均在构造时绑定。目标姿势和加法参考姿势都先
采样别名，然后经过原来的相减、状态混合、Slot、分层和最终根混合。
没有在最终输出处复制 V4 锁脚量去覆盖 Refactored 图内写入。

生产 --refactored-pose-curves 入口在移动来源、Grounded 缓存及 Main 输出
布局加入这三条输入，最终根现可反馈真实 GroundPredictionBlock。原默认
入口不增加别名；默认完整分层入口与新 Rig 的启用仍待整角色验收。

## 验证结果

预先导出的 UE 原生夹具作为期望数据，用其原曲线值独立验证新名字，不用
Godot 的 V4 采样结果反过来充当原生期望。

| 场景 | 原生姿势数 | 别名值数 | 非零值数 | 负值数 |
| --- | ---: | ---: | ---: | ---: |
| 移动原始采样，float | 1648 | 664 | 508 | 28 |
| 移动加法采样，float | 1092 | 208 | 40 | 40 |
| 停止原始采样，double | 124 | 208 | 84 | 20 |
| 停止加法采样，double | 104 | 208 | 88 | 24 |

共 2968 份原生姿势，曲线最大误差 8.940697E-08。两组移动测试还验证了
生产 source wrapper、曲线先读再求姿势、每组 4864 次热采样零分配，
四个独立所有者各组 single/parallel 36480 次采样的全部姿势/曲线位一致。
停止精确采样逐份重复结果相同。未开启别名的原始移动 1648 份对照也通过。

实际生产 single/parallel 各 960 帧通过，包括世代替换。每组 82 帧最终
屏蔽为正，逐帧等于当前 V4 图的最终混合 Mask_LandPrediction，并检查
下一帧查询使用上一已提交反馈。各 52 帧空中预测为正；预测和 PoseState
摘要变为 EFC740AA927A17E5，两种模式相同，证明屏蔽数据实际影响预测。
此前无源屏蔽的摘要为 C330BE565B1894C0，不再作为本入口期望值。

result DB9FEFC95ADA4B15、pose EFEF274D127B1A99、
full_pose 765E1669B4501131、root 3C8B520C47ECA5C7 保持；
PoseMoving 摘要 AAB0325E448AAC0C 保持。这不是视觉改善证据：实际输出
仍由旧脚部 IK/pelvis 与已替换的 based lock 消费，新 Rig 尚未接入。

并行第 25 帧真实通知晚期故障验证预测、缓存 PoseState、动画最终反馈、
主线程反馈全部保持，候选事件不泄漏。主移动组件 float/precise 各 3360
帧、297 混合帧、18 输入拒绝与晚期失败重试通过；新别名输入防护和既有
预测/状态曲线 Import 测试共 21 项通过。最终 Godot 构建零警告、零错误。
首次构建的 OS 命名空间和曲线结构体/数值比较两处编译错误已修正。

日志均位于 artifacts/refactored-source-curves-177-*.log：
movement-raw、movement-additive、stop-raw、stop-additive、raw-original、
single、parallel、late-source、main-state。Import 结果为
artifacts/tests/refactored-source-curves-177-import.trx。

## 剩余边界

本批闭合三条源曲线适配，尚未证明整角色与 Refactored 原资产的效果相同。
下一项是将已验证的新脚部 Rig、锁定历史、环境查询按脊柱→脚→手顺序接入
同一生产候选与提交，再做支撑接触、起步、换向、平台和多帧视觉验证。
第 616 帧旧失败、默认完整入口、P3/P4 整角色及原 P5A–P7 均保持开放。
没有修改原生资产或 UE 插件，没有提交、回滚或改动已确认的键鼠逻辑。
