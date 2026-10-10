# Kimodo Air 真实生成、默认接入与游戏验证

## 本轮结果

在主目录实际运行独立 `--scope air`，生成 Fall、Fall_Fast、Flail 三个候选，交付 16 个 V4／Refactored 身份。模型为 `Kimodo-SOMA-RP-v1.1`，100 步 diffusion，SOMA77；参数、条件、原版参考身份和候选输出均保存在各节点回执。三个生成节点均 `boundInput=false`，没有以参考或受控夹具代替扩散输出。

专用全身／Hips 三维条件、有界周期制作、纯脚本重定向、Root/Pelvis 分解、曲线、最终 FBX 全键回读、Godot 导入与实际运行已经执行。稳定契约见[独立 Air pipeline](../architecture/kimodo-air-pipeline.md)。

最终组合包为 `assets/generated/als_nonprop_20261010_air_combined_v1/resource-pack.json`，保留当前移动、攀爬及其他资产，替换本轮 16 个 Air 身份。默认指针已选择该包，沿用 `allowDiagnosticPack=true`。这是诊断默认选择，`productionReady=false`、正式 `activated=false`；60 个非道具缺口及原版回退继续保留，没有关闭全量生产门禁。

## 制作与交付证据

证据根为 `artifacts/kimodo_als/air-20261010_integration_v1/`。

- `authoring/batch-result.json`：源图 `authoring_complete`，32 节点、3 个实际生成候选、16 个交付身份；独立范围闭包完成。
- `generated-air-audit.json`：真实生成设置、源身份和六份目标 Air 检查；独立包 16/16 最终 FBX 全物理骨／时间键回读通过。
- `combined-v1/selection.json`、`conversion/pipeline-result.json`：当前默认包与新 Air 的明确源选择；123 个交付重新构建，Air／Gait／Mantle 目标门禁无失败，123/123 最终 FBX 回读通过。
- `combined-v1/delivery-audit-final.json`：1,536 个最终载荷摘要通过，107 个非 Air runtime raw 与旧默认包语义一致；70 个道具资产保持原字节，其中 V4 FBX 38 份、Refactored 原始载荷 32 份。
- 最终包 SHA256：`e5e80a78c643c6bfcf58d57659403ede54de0276bbac63b3b0c5d14d90f76b96`。运行 Root 曲线 34 份、分解前 Root 曲线 44 份；不为 Air 静态 Root 制造角色坠落轨迹。

源周期选取起点分别为 Fall 30、Fall_Fast 30、Flail 40 帧；原时长仍为 1 秒、1 秒和 0.667 秒。目标报告保留实际采样速度边界检查，首尾位置和旋转闭合不能替代速度检查。

| 动作 | 最大躯干倾斜 | 骨盆高度误差 | 骨盆竖直范围 | 平面残差 |
| --- | --- | --- | --- | --- |
| Fall | 15.443° | 3.451 cm | 5.787 cm | 0.968 cm |
| Fall_Fast | 11.614° | 3.524 cm | 5.954 cm | 0.966 cm |
| Flail | 6.132° | 1.647 cm | 2.018 cm | 0.426 cm |

六份 V4／Refactored 目标检查均通过；全物理变形骨首尾位置／旋转差为零，Root 平移／旋转保持参考原点。上述为 FK、采样边界和导出门禁，不是逐顶点蒙皮或连续 UE 图对照证书。

## 实际 Godot 与旧包对照

`fixture-absolute-build.log` 的最终 `dotnet build GodotALS.csproj -p:Optimize=true` 零警告、零错误。组合包导入、P2B 与六领域请求编译见 `combined-v1/runtime-v2/`。相关 Python 回归 `integration-regression-v1.log` 为 61/61。

`air-scene-final-v2/results.json` 最终 **11/11** 通过，包载荷与运行程序集前后摘要一致。包含 30／60／120 Hz 各单线程和并行的真实 30 m 下落、三个既有受控 Air 场景，以及单线程／并行两组 GPU 场景。实际测试通过普通 Demo 入口及其原有角色物理／提交链，只设置初始位置和移动输入，不注入动画姿态。

`final-air-summary.json`：八个实际物理／GPU 场景共 2,144 个观察帧、1,334 个空中帧和 22 张 GPU 截图。三种频率的单线程／并行逐帧记录完全一致；稳定空中且预测值 <0.1 的躯干最大倾斜 15.678°，Root 相对骨盆高度 0.889824–1.006324 m，平面残差峰值 0.008302 m。每张截图要求请求帧、已提交帧与已呈现帧相同。

实际场景覆盖高速／部分 Flail 的输入速度范围、预测落地和最终胶囊接地。为隔离普通 Fall，自动冲击 Ragdoll 在该夹具中关闭；这不是高冲击自动 Ragdoll 验收。全部混合权重分支、完整预测与取消重试由另三项受控场景验证，不能把输入速度覆盖表述为连续 UE 原生混合对照。

三个受控场景保留原断言，分别通过：

- Air 混合 2,520 帧、22,680 曲线检查、12 个端点和 18 个拒绝／恢复检查。
- 共用源时钟 3,360 帧、18 个事件、674 个冻结帧、1,266 个混合帧、12 个失败注入／重试；额外六项 guards 通过。
- Lean 2,562 帧、26 个端点、7,686 个曲线检查及两个身份。

`previous-pack-drop.json`／`previous-pack-render.json` 使用旧默认 Kimodo 组合包运行相同场景，均在稳定空中检查被拒绝：躯干倾斜 74.26°、骨盆高度约 0.113 m。`previous-vs-new-physics.json` 确认新旧前 50 帧的胶囊位置与速度完全一致；显示骨骼显著不同，将该可见缺陷定位于资产／姿态处理链。这个对照对象是旧 Kimodo 组合包，不是原版 ALS。

![旧默认包、新 Air、快速下落与预测落地的真实截图](../../artifacts/kimodo_als/air-20261010_integration_v1/air-before-after-contact-sheet.png)

截图来自实际 GPU 输出；原始完整截图及包身份保留在对应目录。截图复核不能替代用户对动作节奏／美术的人工验收或逐顶点蒙皮门禁。

## 相关回归与保留失败

`combined-v1/runtime-v2/e2e-matrix/summary.json` 的选定 16 项运行断言全部通过，包括普通入口、Flail 单线程／并行、Root 关闭、站姿／方向移动、Low／High 三 Hz 攀爬、移动攀爬及自动恢复着陆。载荷、设置与输入保持。因未选完整方向质量范围，汇总仍返回 `Directional coverage not selected`，退出失败；本轮不是完整 71 项矩阵证书。

同目录的 Mantle 静态检查 9/11；平台／生命周期的运行、接触、身份检查全部通过，蒙皮 9/11，两组输入保持。原 2 cm 阈值未改，仍保留：

- Root Motion 开启的 Low 蒙皮约 4.843 cm，以及支撑手高度检查失败。
- 原版 Low 负对照约 2.063 cm。
- 攀爬取消后的姿态，以及关闭手锁的负对照，仍超原蒙皮阈值。

组合编排 `runtime-result.json` 保留 `runtime_failed`。其中当时的 Air 夹具失败已经由最终独立 Air 全组复测关闭，不回写旧报告为通过；其他上述失败、源闭包和完整矩阵门禁保持。未执行本批长时间、性能、完整逐顶点 Air 蒙皮、连续 UE 图或用户人工验收。

## 中间失败与修正

独立包已完成生成、转换、最终 FBX、构建、导入及六域绑定。其完整矩阵后续为集中验证实际组合包而中止，原已完成结果和 `standalone-runtime-interruption.json` 保留；独立包结果不作为完整运行证书。

首次新增场景未进入已启动的构建，首个启动无法实例化，见 `first-drop-v1.log`。新增夹具的命名空间和 `Environment` 名称歧义导致组合 `runtime-v1` 构建失败；已修正。一次直接启动发生在六域请求完成前，见 `first-drop-v2.log` 的源绑定身份失败；完成既有请求编译后 `first-drop-v3` 实际下落通过。

旧 Air 场景先因固定原版配置与生成动作集的同步标记不一致而失败。改为读取选定包配置后，又暴露物理骨缓冲与现有逻辑骨接口不匹配；迁移到 `MovementSources` 的完整逻辑参考姿态／采样接口。Lean 的绝对预期使用 `rawBonePose: true`，避免把 additive 差值当成绝对姿态。修正只涉及夹具适配，原断言和生产求值器未放宽；`fixture-preflight-v1`、`falling-lean-absolute-v1.log`、最终 Air 各组日志均保留。

## 默认接入

最终 Air 全组通过后才修改 `assets/config/als_resource_pack.json`。旧指针字节备份为 `previous-active-pointer.json`，选择回执为 `default-selection.json`；资源扫描范围按新默认重新收敛。

清除全部 `ALS_` 进程环境覆盖后，直接运行普通 `scenes/demo/als_demo.tscn` 180 帧，退出 0、没有 ERROR，日志明确选择新组合包、`replacements=123`、Root Motion 关闭，见 `ordinary-default-restart.log`。使用 Dummy 音频，不覆盖实际音频验收。没有提交或推送。

随后打开普通 Demo 游戏窗口，确认窗口有响应且存在有效窗口句柄；无环境覆盖的启动日志再次确认选择新组合包，见 `ordinary-default-launch.json`、`ordinary-default-window.json` 和 `ordinary-default-interactive.log`。窗口已供用户查看，这不代表用户人工验收通过。
