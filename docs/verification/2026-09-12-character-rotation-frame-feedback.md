# Character 旋转与动画曲线反馈（第 101 批）

日期：2026-09-12。工作区：`D:/GodotALS-p5a-events-actions`。
承接原 P3/P4 的最终 YawOffset 消费和 Character Blueprint 更新顺序。没有
commit、revert、merge 或清理用户修改；音频暂缓。当前仍是移植完整性修复的
阶段结果，不是完整 ALS、上身、交错步或滑步验收通过。

## 实现与数据边界

正式数据 `assets/config/v4_character_rotation_inputs.json` 来自当前 UE 项目的
ALS V4 Character Blueprint。新增严格编译器消费 19 个 authored 函数图及相关
初始化/历史图，检查输入计算、连接、执行顺序、局部变量作用域、枚举、CDO、
MovementModel 绑定及曲线。ALS-Refactored C++ 仍为架构参考，不混用两版规则。

导出三种 MovementModel 行、三个 RotationRate CurveFloat（每条 401 个原生
采样）、六个枚举及 320 组原生旋转插值样本。当前默认 Normal 行的六种
rotation mode / stance 配置由实际数据绑定。BeginPlay 的初始 Running 与
初始 CurrentMovementSettings 也来自 CDO/执行链，不预先替换成 Normal 行。

Core 新增双阶段旋转模型：先按 UE RInterpTo_Constant 更新 TargetRotation，
再按 RInterpTo 更新 Actor。覆盖地面 Velocity/Looking/Aiming、Sprint 目标、
静止 LimitRotation 后叠加 RotationAmount、Rolling 输入分支、空中朝向捕获
和 Aiming 回写历史。保留 1 cm/150 cm 严格阈值、30 Hz 曲线系数、0.001
死区、±100 度限制及原生零速率/零 Delta/180 度/近零插值行为。

Motor 在移动求解后、动画输入采集前消费上一已提交基础图的 YawOffset /
RotationAmount 与 Action；先用上一份移动设置判定 ActualGait，再选择本帧
旋转设置。动画 Worker 直接使用实际 Actor 朝向与 Gait，不再执行旧旋转。
输入保持已有键鼠行为。当前 Alt 是步行，未新增独立自由观察模式。

曲线反馈随姿势、事件一起提交；非法数值、未初始化却携带数据、未来帧、
异角色/代次反馈在 Motor 变动之前拒绝。Target/InAir/LastVelocity/LastInput
朝向、Gait 与前帧移动设置进入 Motor 恢复快照。角色换代传递已经旋转过的
输入与历史，只在验证来源后迁移代次，保留原 RetiredMotorInput 供诊断，
不会对同一物理帧再次旋转。

## 验证

| 检查 | 本批结果 | artifacts 证据 |
| --- | --- | --- |
| UE Editor 完整构建、插件审计 | 退出 0，既有构建有效，3 个项目插件通过 | `character-rotation-ue-build.log` |
| 最终冷导出 / 普通 Editor 导出 | 均退出 0，19 图，assets_saved=0，前后审计通过 | `character-rotation-native-final.log`、`character-rotation-editor-repeat-final.log`、`character-rotation-final-*-audit.log` |
| 两份导出语义编译 | 所消费连接、六组设置、曲线、320 插值样本通过 | `character-rotation-editor-semantic.log` |
| 最终 C# 构建 | 0 警告、0 错误 | `character-rotation-build-feedback.log` |
| Core 专项 | 103/103，含旋转、输入所有权、反馈校验及布局 | `character-rotation-core-feedback.log` |
| Import 专项 | 32/32，含有效原始图和语义变更拒绝 | `character-rotation-import-closure.log` |
| 单线程 / 并行真实 Motor | 各 600 帧，完整图、换代恢复、已应用旋转快照、Gait/曲线身份通过 | `character-rotation-single-final.log`、`character-rotation-parallel-final.log` |
| 晚期姿势 / 事件失败 | 候选回滚通过；新增已提交旋转反馈不泄漏断言通过 | `character-rotation-late-transaction.log`、`character-rotation-late-source-event-final.log` |
| 实际渲染横移 / 转身中起步 | 720 帧、120 张截图、44 帧移动与转身重叠，最大相邻脚旋转 14.348 度 | `character-rotation-turn-start-visual.log`、同名目录 |
| 真实键鼠事件到默认 Demo | 360 帧、Alt/A/D、4 次鼠标事件；180 帧步行、90 帧松 Alt 后移动、150 帧左移、120 帧右移 | `character-rotation-keyboard-mouse.log` |

单/并行 result=`DFA5F7A4F3296FA3`，full pose=`88AAD97FC78B8895`，
root=`A4F6C26CBAB8A0E7`；事件 28，lag/stale 0。键鼠测试直接派发
InputEventKey / InputEventMouseMotion，由生产 Demo 捕获，再进入 Motor、
Worker、Commit；277 帧 MovingLooking，247 帧实际 Actor 旋转，每帧确认
消费精确上一已提交帧的反馈，没有调用受控命令注入替代生产输入。

已查看 `character-rotation-turn-start-visual/movement-contact-sheet.png`
的 186–360 帧连续横移/换向图，以及 `strafe-directions.png`。截图仍显示
双臂较收拢。14.348 度是突跳检查，不是支撑脚滑移或换髋等价性的指标。
本批没有匹配输入的 UE 完整最终骨骼逐帧对照，也没有最终人工或十分钟验收。

冷/普通 Editor 非图数据一致，但 7 个函数图存在 Editor 重建文本差异；
原文哈希不同，因此以严格消费连接编译比较，不声称原文完全相同。最终正式
JSON SHA256=`CD16B0E01B22C78EB15975AB492D301ECC56DE087E200038AA07785783D5B641`。
普通 Editor 启动日志仍包含已有 UnifiedErrorTest 自测错误，不能写成日志零错误；
本批未改 UE 原生插件/资产，无新增原生编译内容，未重复 DataValidation/打包。

保留首错：首次导出缺少带空格属性名的曲线收集，后修正属性键归一化；
CurveFloat T3D 的 CRLF 解析正则导致首轮有效图编译失败，已修正；新增 smoke
局部变量重名导致编译失败；输入闭合验证把函数局部变量误判为 self 字段，
后改为准确作用域验证并补拒绝用例。首次失败日志均保留，不以拒绝测试单独
通过掩盖有效基准图失败。

## 下一项

1. 接完整动态 LayerBlending / Add / LS / Aim 与 Overlay 上身链。当前反馈
   来自 BaseLayer；未来最终层修改曲线后，必须改为真正最终层的提交反馈。
2. 补 Grounded/Crouch 曲线 presence、Foot IK/Lock、pelvis、平台最终消费者，
   在实际支撑窗口测脚相对角色/平台位移，继续验收起步滑步及交错步/换髋。
3. 继续 P5A ActionPlayback 摘要、GroundedEntry 类型化通知、通用多片段/section，
   再推进 P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、P6 物理恢复/完整
   Camera、P7 十分钟预算。当前 Motor 没有动画 Root Motion 驱动，旋转模型
   只有相应门控能力与测试；活动状态仍待真实驱动接入，不能从 Montage 有播放
   或位移非零推断完成。当前接线也不等于完整 UE CharacterMovement 物理已移植。
