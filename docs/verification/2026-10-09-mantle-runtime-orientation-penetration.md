# Mantle 运行时朝向与穿模复核（2026-10-09）

用户指出实际 GPU 截图中的穿模和约 90° 偏转后，复核确认此前“候选没有相对原版额外偏转”的比较遗漏了两者共有的运行时错误。旧场景只断言胶囊位移／落台，不能证明 actor 旋转或最终蒙皮正确。本次全程使用可复用 Godot 场景和脚本，没有使用 computer-use 或启动 UE。

## 90° 原因与修复

`AlsMantlingRootMotion.CreateAnchors` 始终将 mesh 相对 actor 的 −90° 基底加入起点和目标锚点。Root 关闭时传入 identity root，但该基底仍保留。旧 `Prepare` 关闭分支直接返回 identity，省略与锚点配对的 mesh→actor 还原，因此目标 actor 额外偏转 90°。经过后续角色旋转求值，实际静态 Low 中观察到约 82.5°。

修复将作者 Root 采样开关放在 `Reverse(mesh, root)` 内部：关闭时仍执行 `Reverse(mesh, identity)`。没有读取作者 Root，没有改变默认根运动策略，没有修改 FBX、动画 Root yaw、模型 presentation 或包文件。Root 开启分支保持原运算。

Git 历史已确认这是后续新增关闭策略引入的回归，不是最初原版迁移缺少补偿：`ef5ae8d0` 的父版本在 `Prepare` 始终执行 `Reverse(mesh, UnitScale(rootSampler.Sample(montageTime)))`，Godot Mantle 调用也始终使用作者根轨迹；该提交加入 `useRootMotion` 关闭分支后，将整个变换替换为 identity。诊断入口的 `SampleRootMotion` 在未显式配置普通 Demo／开关时仍保留作者采样，因而不能将早期原生诊断通过当作关闭分支验证。后来的关闭策略场景只断言动作完成／落台，未测 actor yaw；旧 v2 原版渲染日志也明确为 `enabled=False`，原版同样受该回归影响。早期正常原版与后来关闭路径故障并不矛盾，不能把回归归因于 Kimodo 源动画。

新增四组 Core 回归覆盖 actor yaw=0／0.4 rad、世界／平台相对坐标的起点、终点和中点；关闭分支传 null sampler，证明抵消 mesh 基底不依赖 Root 采样。旧代码四组全部失败，日志 `core-before-sdk8.log`；修复后 Release Mantle Core **33/33** 通过，日志 `core-after.log`。

## 实际蒙皮检查

`MantlingDemoSmoke --mantle-output=<path>` 记录每帧 actor 位置／yaw、物理骨骼 world 位置／旋转、committed/presented 身份。复用 `AlsSoleGeometryObserver` 已验证的 bind／权重映射，用已发布骨架重建完整 **22,650** 个蒙皮顶点，查询已知单位缩放平台箱体内部的顶点和最近箱面深度。bind 误差 0.00007372 m；五组独立对照各 360 帧的 committed/presented 均匹配。

这是实际皮肤顶点／箱体几何检查，不是骨骼端点代理。它能提供穿模证据，但不检测仅三角面穿越且顶点都在外部的情况，也不测支撑力或自碰撞。记录最深顶点、数量和最大权重骨骼，保留失败而不移动角色掩盖问题。

| Low 60 Hz，当前包 | 最大 actor yaw | 最大蒙皮交叠 | 最差 tick／主要骨骼 |
| --- | --- | --- | --- |
| 修复前候选，Root 关闭 | 82.500° | 22.090 cm | 81／foot_l |
| 修复后候选，Root 关闭 | 0° | 19.627 cm | 55／middle_03_l |
| 修复后原版，Root 关闭 | 0° | 22.329 cm | 56／middle_03_l |
| 修复后候选，Root 开启 | 0° | 9.077 cm | 79／middle_03_l |
| 修复后原版，Root 开启 | 0° | 3.989 cm | 52／ring_03_l |

证据目录为 `artifacts/kimodo_als/mantle-penetration-20261009/`。五组 JSON 和同名日志保留逐帧数据；`comparison-summary.json` 记录报告哈希、身份匹配和峰值。

修复只消除错误朝向，**穿模未通过**。Root 关闭路径不读取作者 Root，只按目标权重插值，实际中段与姿态的抬升不一致；切换开启后候选交叠下降，但仍高于原版，说明还存在独立的候选姿态／身体避障问题。不能通过开启开关、补资产 90° 或因为原版也穿模来判定质量合格。

上排为旧构建候选，下排为修复后候选，均为同一包、Root 关闭、实际 GPU tick 42／60／84，裁掉 HUD 并等比缩小。朝向恢复后中段穿模仍可见：

![修复前后实际 Godot 对照](../../artifacts/kimodo_als/mantle-penetration-20261009/yaw-before-after.png)

## 复用门禁与覆盖

`scripts/verify-kimodo-mantle-scene.py` 保留原场景断言、包／829 载荷和配置／程序集完整性、七组运行及候选／原版各 60 张 GPU 截图；新增逐帧 actor yaw 上限 1° 和完整蒙皮箱体交叠上限 2 cm。后者为新的静态夹具诊断标准，没有改变既有原生接触阈值。结果分开记录 `runtimePassed`、`quality.orientationPassed`、`quality.skinPassed`，任一质量失败都使总结果非零，原版也照实记录失败。复现命令：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-kimodo-mantle-scene.py `
  --pack res://assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json `
  --output artifacts/kimodo_als/<fresh-run>
```

当前修复后的完整局部轮证据写入 `artifacts/kimodo_als/mantle-scene-20261009-v3/`：七组进程原运行断言 **7/7**、actor 朝向 **7/7**，但蒙皮交叠 **0/7**，总结果 `KIMODO_MANTLE_SCENE_FAILED`，runner 返回 1。三种物理 Hz 的候选关闭路径峰值均约 19.627 cm，单线程与并行一致；开启路径约 9.077 cm；渲染候选／原版各保留 60 张 PNG，分别约 19.627／22.329 cm。全部 2,700 个诊断物理帧的 committed/presented 身份匹配。所有进程 exitCode=0，失败原因均为几何门禁，不是引擎或场景中断。不能将旧 v2 的 `_OK` 当作新质量门禁通过。

v3 的 `inputsUnchanged=true`、`changedFiles=[]`，829 载荷及包／配置／程序集未变；包 SHA-256 为 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`。manifest 保存构建身份，results 保存每项失败、逐帧报告哈希与截图摘要。首轮 baseline、旧 v1／v2 的证据保留。

优化 Godot 构建通过，0 警告／错误，SDK 为用户安装的 .NET 8。额外实际 Godot moving／high／cancel Mantle 和 Root 关闭策略场景通过，独立日志保存在本次证据目录；High／moving 使用包内原版回退，不代表新 High 生成通过。Import Mantle Release 过滤 **93 通过、5 失败**，五项均因原始 manifest／player binding 摘要不匹配，日志 `import-after.log`；没有跳过或放宽它们。

活动包仍为四个 Low 身份替换的 v4 诊断包，`productionReady=false`。未重新生成资产，未完成四身份穷举、新 High、完整矩阵、长时间、性能或人工验收。下一步需要修正程序化抬升／姿态同步与候选释放、提腿阶段的障碍约束，完成逐帧蒙皮及手掌／脚底接触验收后再进入完整发布矩阵。
