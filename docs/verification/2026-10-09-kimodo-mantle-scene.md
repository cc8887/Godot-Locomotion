# Low Mantle 可复用 Godot 场景验证（2026-10-09）

**后续复核纠正**：下述 v2 的 7/7 只覆盖当时的运行／截图采集断言，不能证明朝向或穿模合格。用户指出画面缺陷后，逐帧实测确认原版和候选均受 Root 关闭路径的 mesh 基底抵消遗漏影响，角色实际偏转约 82.5°；将这种偏转解释为动作本来的侧身是不成立的。Core 已修复，脚本已增加 actor yaw 和完整蒙皮／平台交叠门禁，保留旧通过与失败证据。当前结果及剩余穿模见[复核记录](2026-10-09-mantle-runtime-orientation-penetration.md)。

应用户要求，本次直接运行 `scenes/tests/mantling_demo_smoke.tscn`，不使用 computer-use。场景实例化普通 Demo，沿用角色时钟、真实主线程物理探测、生产 Montage／分层姿态、worker 提交及最终骨架写入；自动输入和静态 1 m 平台来自已有夹具。

## 复现入口

新增 [verify-kimodo-mantle-scene.py](../../scripts/verify-kimodo-mantle-scene.py)，使用标准库。先完成项目构建和 Godot 资源导入，再从主目录执行：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-kimodo-mantle-scene.py `
  --pack res://assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json `
  --output artifacts/kimodo_als/<fresh-run>
```

`--godot` 可指定已有引擎。输出目录必须全新；所有子进程显式选择候选包，原版进程使用 `ALS_RESOURCE_PACK_PATH=original`。不修改活动指针、动画设置或资产，不启动 UE，不自行生成／导入／构建资产。

测试保留场景原有断言：仅启动一次、选择 Low、AnimationDriven、Root 运动连续、完整结束、落到台面并 grounded、动画／相机错误数为零。脚本补充实际包选择、静态夹具胶囊沿 -Z 的检查、完整截图文件集合及输入字节完整性。无渲染进程使用 fixed-fps=60 加速模拟，场景仍分别采用 30/60/120 物理 Hz；GPU 进程保持真实时间、1280×720、gl_compatibility，使用 FramePostDraw 截图。

## 执行证据

证据位于 `artifacts/kimodo_als/mantle-scene-20261009-v1/` 和 `mantle-scene-20261009-v2/`。各目录的 `manifest.json` 保存包／场景／构建摘要及用例参数，`results.json` 保存逐进程命令、退出码、完整日志、成功标记、胶囊轨迹、PNG 路径／摘要与完整性结论。失败用例继续记录，既有证据不覆盖。

首轮 7 个场景断言和截图采集均通过，但测试期间三个运行程序集发生重新构建，最终 `inputsUnchanged=false`、`passed=false`、退出码 1。该轮只供失败取证，不能作为同一构建下的完整通过；没有放宽完整性检查。随后用当前构建在独立 v2 目录重新运行。

v2 使用当前 Godot 4.7.2 和现有构建完成 **7/7**，成功标记 `KIMODO_MANTLE_SCENE_OK`，退出码 0，`inputsUnchanged=true`、`changedFiles=[]`。本次没有修改 C# 或重新构建，程序集准确摘要记录于 v2 `manifest.json`。

| 场景 | 结果 |
| --- | --- |
| Low 30／60／120 Hz、并行、Root Motion 关闭 | 3/3，分别 active=30／59／117 |
| Low 60 Hz、并行、Root Motion 开启 | 通过，active=59 |
| Low 60 Hz、单线程、Root Motion 关闭 | 通过，active=59 |
| 候选 Low 60 Hz GPU 渲染 | 通过，60 张 PNG |
| 原版 Low 60 Hz GPU 渲染 | 通过，60 张 PNG |

所有场景均 starts=1，完整落地。候选和原版胶囊最终位置均为 `(-4.0000005,1.9010313,-0.7499999)`，前进沿 -Z，没有沿 X 的固定侧移。Root Motion 开／关的中段高度不同，不能把最终位置相同解释为整段轨迹完全相同。

包字节 SHA-256 保持 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`；829 个载荷、包文件、活动指针、动画设置、测试场景／源及三个运行程序集在 v2 前后均不变。活动指针仍为 Mantle v4，原版对照仅设置子进程环境覆盖。

从双方的实际 GPU PNG 查看第 6、24、30、36、42、48、54、60、66、72、78、84、90、96、108、120 tick，共 32 张画面。当时只比较候选相对原版是否有额外偏转，漏掉两者共有的运行时朝向缺陷和明显穿模；该观察结论已被上面的逐帧复核纠正。

`candidate-contact-sheet.png`、`original-contact-sheet.png` 保存完整观察帧；以下图只裁掉 HUD 并等比缩小，保留真实人物／平台画面，上排原版，下排候选：

![原版与候选在 42／60／84 tick 的真实游戏对照](../../artifacts/kimodo_als/mantle-scene-20261009-v2/original-candidate-comparison.png)

## 覆盖边界

候选为四个 Low 替换身份的 v4 诊断包，High 和其余动画为原版。普通 Mantle 消费器读取 Refactored Mantle 配置，此静态 Default 夹具不是四个 V4／Refactored、左右手身份的穷举。原版对照只用于比较同夹具姿态，不能将原版动作计作新 High 交付。

截图按既有场景的物理 tick 命名，本脚本验证文件集合／摘要，尚未新增 committed/presented 帧与骨骼记录的逐帧绑定门禁。胶囊朝向检查不等于最终蒙皮所有骨骼朝向数值检查；截图有限观察不等于手掌／脚底接触距离、人体动作风格、完整矩阵、长期、性能或人工验收。包的 `productionReady=false` 和既有 High 源失败保持。
