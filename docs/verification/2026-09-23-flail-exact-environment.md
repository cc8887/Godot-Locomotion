# Flail 原生参考场景的环境几何精度

本批修复 UE 对照工具重建环境时的精度损失。Godot/Core 运行时代码没有修改。修正后，同初态、同动画驱动的两模型在 30/60/120 Hz 共 **4200 帧**的身体位置、旋转、线速度、角速度与 UE 世界逐值相同，30 Hz AnimMan 第 20 帧的差异消失。

## 问题证据

`artifacts/flail-30-first-step-20260923/` 保留原始失败和修复后结果。

旧参考的第 19–20 帧：积分后和求解前 COM/P/Q/V/W、质量与惯量一致；四个采样帧的接触对集合、方向、求解顺序一致；点和摩擦锚点一致，但 AnimMan 第 20 帧右脚对地法线 Y 相差 `1.3646413208334707e-6`。不能据此直接修改求解器。

新增只读 `stepObservations.gjkCaches` 后，发现右脚对地的 GJK 支持点 B 使用了不同地面尺寸：

| 量（cm） | Godot/Core 与捕获输入 | 旧 UE 实际几何 |
| --- | ---: | ---: |
| 地面半宽 | 3186.182403564453 | 3186.182373046875 |

`AlsSceneContactSet.ExportNativeEnvironment` 把 Godot float 米值提升到 double 后乘 100；查询器使用相同精度。原生参考先经过 `FKBoxElem.X/Y/Z` 的 float 字段，悄悄量化了尺寸。此前的“同输入”描述对实际环境支持几何并不完全成立；60/120 Hz 身体轨迹相同并不能证明此处没有差异。诊断缓存中的 native 预积分列表为空时也不能当作实际查询前缓存，主要使用 preSolve 的有效缓存观测。

## 修正

- UE 仍通过 BodySetup/FBodyInstance 建立身体、碰撞过滤和材料；对捕获的环境 box，随后通过外部粒子 API 设置精确 double 半尺寸、零 margin 的 Chaos box union。没有修改 Godot 几何去迁就参考工具的舍入。
- 每个 case 记录实际求解端 `environmentGeometry`，对全部 box 强制检查实际 leaf bounds 与输入尺寸完全相等、margin 为零。此批每模型 13 个环境 box；不据此宣称任意凸包重建也已完成同等精度验证。
- `CompareFlailWorld.ps1 -RequireExactBoxGeometry` 在轨迹比较前验证环境观察完整、类型/索引/仿真开启、零 margin、精确尺寸。旧参考因没有观测而被明确拒绝；新三频率参考全部通过。
- GJK 缓存导出只读，不种入缓存或改变模拟。右脚第 19 帧缓存完全相同，第 20 帧最大权重差仅 `5.551115123125783e-17`；对应接触法线已完全相同。

## 完整回放结果

`comparison30.log`、`comparison60.log`、`comparison120.log` 各自遍历全部帧，恢复 JSON 中 float 的实际存储精度，再比较身体 P/Q/V/W；不是只取首尾或几个采样点，也没有新增误差容限。

| 频率 | 每模型帧数 | AnimMan 身体分歧帧 | Mannequin 身体分歧帧 | 原生休眠帧 A / M |
| ---: | ---: | ---: | ---: | --- |
| 30 | 300 | 0 | 0 | 未睡 / 163 |
| 60 | 600 | 0 | 0 | 220 / 未睡 |
| 120 | 1200 | 0 | 0 | 1087 / 476 |

`contacts-exact.json` 四个采样帧共 113 点的 shape world、局部点、法线、锚点、标志无差异，点数一致。

这证明当前受控十秒 Flail 场景的身体轨迹一致，不代表所有动画、初态、平台、任意几何或普通 ALS gameplay 已完成。独立动画目标仍有 double 末位差异，不宣称每个中间计算完全同位。

## 构建与导出验证

沿用 ue-diagnosing-plugin-build-load 的完整 Editor target 事务。最终构建和审计通过，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`，fingerprint `FA297529B2E8EB2013A2BA58910212F4512AD8498C1CB30836080415AA1E7660`。主目录导出器与 UE 项目镜像源码 SHA256 均为 `BA96DD7EBD652937A26F44FF903CACCE9084F33DAB6F48B834D8A57790967C3A`。

| 独立原生输出 | SHA256 |
| --- | --- |
| native-exact30.json | 163DE0DD5360667C3938318B5801183DA46836D6C844E9CBB8296EDB9A6AAC07 |
| repeat-exact30.json | 163DE0DD5360667C3938318B5801183DA46836D6C844E9CBB8296EDB9A6AAC07 |
| native-exact60.json | 8BB1F6BCB77686EEACFDBEBE6675C4802BAFB7A8AFE8DDCE12C1AA66BC8F3A45 |
| native-exact120.json | A49B090B00B6F9C6655F6D8B02459EF15F89147854BE096FB286D4800AF28494 |

冷启动复导 30 Hz 完全一致。DataValidation 退出 0、0 errors/3 旧 warnings。普通 Editor PID 18144 加载导出命令类并记录 `ALS_EXACT_ENVIRONMENT_EDITOR_RESTART_OK` 后退出 0；日志仍有两条旧 `Condition failed`，未声称修复旧启动问题。没有新 packaged build 要求。既有冻结资产未改写。

本批未重跑 C# 全量或静态矩阵，因为运行时代码未改；最近有效记录为 Core 2884 既定过滤通过、Import 2485+1 旧跳过、静态矩阵 9/12。

## 下一阶段

动态 Flail 稳定性门槛仍 **0/3**：30 Hz AnimMan 原生同样未睡，60 Hz Mannequin 原生同样未稳，120 Hz AnimMan 原生同样无法在十秒内保持睡眠一秒。与原生一致不能替代稳定性达标，保留这些失败，不延长用例或放宽门槛。

原先“先查 30 Hz 第 20 帧”的阻碍已消除。接下来推进普通角色 Ragdoll 生命周期：最后提交姿态 Seed、初始速度、胶囊停用、既有动画 owner 的 Flail 输入、骨盆跟随和限速、物理姿态显示及退出；随后 Get-up/Pose Recovery、Mantle、Camera、十分钟性能与稳定性验收。普通 demo 目前仍未接入 Core Ragdoll，以上目标全部保留。

用户 P4 文档未修改、未暂存，SHA256 保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
