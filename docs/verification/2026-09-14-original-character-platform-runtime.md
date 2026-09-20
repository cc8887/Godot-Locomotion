# 原 Refactored 角色、动画与物理平台运行（第 195 批）

## 结论与修复方向

原 `B_Als_Character`、`AB_Als`、`SKM_Als` 和 CharacterMovement 在真实
独立物理世界中运行，也会在持续倾斜平台上穿地。右鞋底晚期锁定窗口
最低 -34.159628 mm，120 帧全部超过 5 mm；平台停稳后，两脚晚期
锁定窗口均没有超过 5 mm 穿地。

这比此前“注入 V4 pre-rig 动画与移动输入的原脚部链”更完整：本批
由原角色自行计算运动、原动画图生成姿势/曲线、原脚锁保存历史。
因此不能继续把这个动态平台问题全部归因于 Godot 丢了动画曲线或
换用 Refactored 基础站姿就能解决。但本场景仍是独立单机物理世界，
不证明网络、所有原版地图、输入设备、镜头及任意坡面行为。

源码与运行结果一致：原移动组件 `bIgnoreBaseRotation=true`，角色与
网格的 up 轴始终直立，`bHasRelativeRotation=false`；原脚锁却仍读取
实际基座完整旋转，Rig 另外使用地面法线调整脚旋转。下一项应验证
这两步是否重复计入 pitch/roll：候选是位置保留完整基座变换、锁脚
朝向仅传递基座绕世界 up 轴的 twist，坡面 swing 由 Rig 统一处理。
这是待验证的接触稳健性修复，不是本批已经实现或证明的因果结论。
若采用，须保留原版策略与原生基线，明确区分等价移植和行为扩展。

## 实现边界

`ReplayRefactoredCharacterPlatform` 从捕获只读取平台变换、初始角色
放置、帧间隔与输入轴。360 帧中不注入 Godot 姿势、曲线、脚状态、
速度、后续角色位置或朝向。原 Refactored 运行配置和动画图保持。

创建带碰撞和物理模拟的独立 Game World，原角色正常执行组件初始化、
BeginPlay、Character/Movement Tick、AnimBP Update/Evaluate 及后处理。
世界本身逐帧 Tick，保留原 Tick prerequisites；观察前等待已有并行
动画任务完成。每帧记录原动画更新计数，要求确实每帧增加一次。

平台为 100×100×1 m 的真实 movable box。控制器按引擎接口初始化为
本地单机控制器，原生 AddMovementInput 接收命令。无视口时只提供
“最近可见”的环境条件，以执行原可见性策略，并强制全质量 LOD0。
不覆盖脚部计算或关闭原骨骼/物理后处理。

共用的原生 CPU 鞋底观察器现从连续脚锁对象中分离。参考组件只负责
选择固定顶点，没有覆盖实际角色的参考/当前姿势。原 SKM_Als 选择到
每脚 41 点；V4 网格的旧对照仍是每脚 83 点，不能按编号直接比较两者。
还记录 79 根实际原骨架组件姿势、全部属性曲线、脚状态、真实平台与
动画基座、角色/网格变换和速度。原 V4 的 11 根虚拟骨配对未因此完成。

新增脚本：

- `tools/unreal/replay_refactored_character_platform.py`：运行两组场景并
  验证真实基座、有效脚部和原生满锁覆盖；绝对路径、新输出、零资产保存。
- `tools/diagnostics/analyze_native_character_platform.mjs`：验证每帧更新、
  基座同帧变换、直立程度及实际满锁窗口的鞋底几何。
- `tools/diagnostics/compare_native_character_runs.mjs`：比较冷/Editor 的
  79 骨、状态、曲线 presence/value 与鞋底；保持原 0.001 cm / 0.02° /
  scale 1e-5 门槛，鞋底几何 0.1 mm，不跳首帧。

## 最终结果

两组各 360 帧，脚部有效 359 帧，真实 grounded/当前平台均为 360 帧。
原动画更新计数均 1–360；两脚晚期窗口各 120 帧为原生有效满锁。
原移动配置产生 375 cm/s 的跑步速度，没有把 Godot 速度写回原角色。
因此两种角色路径是同命令的独立行为实验，不是全角色逐帧数值等价。

| 原角色环境 | 左/右晚期最低 mm | 超过 5 mm 帧（左/右） | 最大平台倾角 |
| --- | --- | --- | --- |
| 持续倾斜 | -4.343275 / -34.159628 | 0 / 120 | 7.035770° |
| 倾斜后停稳 | -1.741423 / +1.973285 | 0 / 0 | 6.891371° |

角色和网格最大倾角均 0°。动画读取的当前基座原点误差为零，旋转差
小于 0.000004°。原数据已经证明脚部确实使用完整当前平台旋转，
没有把“没有发生实际平台运输”或“冻结的远端角色”当作通过。

全帧最低值仍保留：停稳场景运动/初始阶段左/右为 -15.421295 /
-12.143402 mm。晚期窗口通过不等于所有帧接触通过，固定鞋底点也
不能替代完整表面与受力验收。

最终冷/普通 Editor 两组全部通过语义比较，曲线值与鞋底点集完全
一致；倾斜最大差为第一帧未有效 target 的 0.000013495 cm /
0.000030736°。没有排除该帧或放宽门槛，结果不是严格字节相同。

## 验证入口自身修复

首次失败日志 `platform-195-character.log` 保留：先 Possess、后组件
初始化导致原 ALS 在尚未绑定 AnimationInstance 时访问它，退出 3。
已改为先执行原 PostInitializeComponents，再接管和 BeginPlay。

第二次 `platform-195-character2.log` 有效动画 359 帧但移动为 0，按
覆盖门槛拒绝。当前引擎的无 NetDriver 控制器需要 LocalPlayer 或
显式本地初始化，不能凭 standalone 名称假设本地控制。已使用原
SetAsLocalPlayerController；没有开启无控制器运动或注入速度绕过。

第三次实际角色跑通，但冷/Editor 首帧姿势不同，影响到首次锁脚的
约 70 帧历史，两组各 7212 个语义差异，失败报告均保留。问题来自
初始化也参与原动画 relevance 帧缓存：原冷 commandlet 从全局帧 0
初始化，Editor 从非零帧初始化。对整个独立场景的初始化和 Tick
使用同一受控非零全局帧编号（结束时恢复）后，首帧和全部历史通过。
没有热身后裁掉失败帧，也没有手写初始姿势/脚目标。

最终代码仍保留原连续脚锁回放入口，共用鞋底观察器的提取不能改变
已有的输入、Rig 或原脚锁求解。Godot 生产代码、本轮默认入口与
全部 P5A–P7 范围不变；本批不宣称已修复 Godot 持续倾斜穿地。

## 构建与回归

按 `ue-diagnosing-plugin-build-load` 技能完成完整项目 Editor 构建、
四插件审计和准确 state 后才运行各版探针。最终构建日志前缀
20260913T221308847Z-0c31e997d7c945849dfd1cef46dc5719，
BuildId=46a169dd-cae4-425c-b41d-919fc53c0d88，
fingerprint=36420B3E6DB6E26CD14A2A87A54910F4E2299D1275F99363077F228096260B24。
所有本批 C++ 构建均退出 0，没有隔离或删除编译产物。

最终 `AlsFootRigReplay.cpp` SHA256：
C9236EC89D241364A5629591C1C20326E60A78739E889AB032D28A0373EE528A。
`AlsAnimationGraphLibrary.h` SHA256：
60B589D928D654A9EEB5DCB7F4D69F26C79B1B5B490460111ABB91E277D9899B。

旧连续脚部入口倾斜、水平旋转、停稳坡面各 360 帧重跑，与第 193/194
批的完整解析输出逐字段完全相同。鞋底观察器的提取没有改变原结果。
新脚本语法与 diff 空白检查通过；本批不重复计入此前 Godot/Core/
十角色测试，相关旧测试债务仍开放。

最终新原角色冷启动两场景退出 0，0 error/0 warning，零资产保存。
普通 Editor 实际进程退出 0，两场景通过上述语义门槛；它仍包含两条
已有 LogAutomationTest Condition failed 及旧 AI/NavMesh、引擎
LineSetComponentMaterial 缺失函数、MotionVectorSimulation 线程提示。
这些日志未删除，也没有被归入“全部无错误”的成功声明。

DataValidation 退出 0、0 error/3 warning，仍是旧 PawnActionsComponent
和 NavMesh 数据问题。含 ALS 依赖的独立 BuildPlugin 打包退出 0，
BUILD SUCCESSFUL，2 分 22 秒，产物
`artifacts/unreal/character-platform-195-package`。UBA 在内存压力下
终止并重试过一次编译，最终成功；该构建过程不是运行性能验收。
打包后四插件审计再次退出 0，BuildId 保持；仓库、实际部署和包内
probe 源码 SHA256 三者相同。最终没有遗留 Editor/UBT 进程。

## 产物与继续事项

最终原角色：`artifacts/platform-195-final-tilt.json`、`final-settled.json`，
各自 `final-editor-*` 重复、`*-report.json` 与 `*-repeat.json`。
初始失败日志、第三次跑通的早期捕获与重复失败报告保留，勿混作最终结果。

继续 P4 的旋转因果实验和正式接触修复，同时保留起步、换髋、上身与
默认完整入口的整角色验收；之后按原 P5A 通用动作、P5B 全 Overlay/
道具、P5C 动作运动、P6 物理恢复/完整 Camera 和 P7 性能推进。
