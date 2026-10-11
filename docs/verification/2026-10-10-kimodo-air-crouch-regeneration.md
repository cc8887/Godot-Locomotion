# Kimodo Air 中段蹲缩重新生成与导入（2026-10-10）

本批真实重新生成 Fall、Fall_Fast、Flail，沿用模型、seed、时长、100 steps 和原三维 Root/Pelvis 门禁，交付 16 个 V4／Refactored Air 身份。第二轮加入明确的中段腿部姿态条件，最终组合包已导入 Godot 并设为诊断默认，普通 Demo 无环境覆盖重启通过。最终 Air 11/11、相关运行案例 5/5、123 个最终 FBX 全键回读及 1,536 个载荷摘要通过。

## 原因与条件调整

第一轮只更新 prompt，证据在 `artifacts/kimodo_als/air-20261010_crouch_v1/`。三个实际候选均为 `boundInput=false`，与上一轮核对除 prompt 外的生成设置一致；源和目标门禁、16 个最终 FBX、导入和独立 Air 11 项通过。但作者阶段平均屈膝峰值几乎不变：Fall 约 100.920° → 100.907°，Fall_Fast 87.181° → 87.186°，Flail 92.205° → 92.112°。这说明本配置下的全身参考条件限制了文字改动的效果，不能把 prompt 更新当成明显新增动作。

因此第二轮在生成前增加 `air-midcycle-crouch-v1` 条件：每条腿在原生膝盖弯曲平面内，按周期 `sin(π·phase)²` 平滑增加髋部收腿 35°、膝盖弯曲 30°。两端回原姿态，Hips XYZ、骨长、上半身和原参考保持。参数进入生成节点身份，回执保存实际条件；不是在生成后覆盖姿态。稳定合同见[独立 Air pipeline](../architecture/kimodo-air-pipeline.md)。

第二轮证据位于 `artifacts/kimodo_als/air-20261010_crouch_v2/`。三个作者周期的平均屈膝峰值分别为 Fall 124.062°、Fall_Fast 110.430°、Flail 110.541°，相位为 0.633、0.633、0.55，均位于周期中段。详见 `generated-motion-comparison.json`，同时保留完整屈膝曲线、生成设置、源门禁和身份。

## 已完成检查与失败保留

新增回归通过实际 FK 验证双膝在条件时间轴上的增量、两端姿态、Hips 不变及原参考只读。真实 Kimodo SOMA30／77 的六组条件解析通过；Air、图／Runner、Root 所有权及离线编排相关回归 57/57。`crouch-conditioning-regression.log` 保留首次测试插入位置导致的缩进错误，修正后的独立日志为 `crouch-conditioning-regression-v2.log`；总回归见第一轮目录的 `conditioning-full-regression.log`。

第二轮 16 个源交付、六组目标 Air 门禁和 16 个最终 FBX 全物理关键帧回读通过。随后在主目录执行优化构建、Godot 导入、P2B 和六域源绑定。

第一轮通用 71 项矩阵在确定需要姿态条件后被主动中止，以保留 prompt 对照、验收最终版本；独立 Air 全组仍继续完成。中止的进程范围及原因保存在 `superseded-matrix-interruption.json`，已完成案例与失败日志保持，第一轮整体 `pipeline_complete_rejected` 不回写为通过。

第二轮独立管线运行选择普通入口、Flail 单／并行、Root 策略和自动落地恢复五项，并执行独立 Air 全组。这个范围不覆盖完整方向矩阵，完整 e2e 返回非零及 `Directional coverage not selected` 继续保留；不能将局部通过视为完整生产验收。

## 组合包与最终验收

组合包为 `assets/generated/als_nonprop_20261010_air_crouch_combined_v2`，以前一默认 `als_nonprop_20261010_air_combined_v1` 为基础，替换 16 个 Air 身份并重新导出全部目标。组合选择、转换、哈希与最终屈膝检查保存在第二轮目录的 `combined-v1/`。最终 `delivery-audit-final.json` 确认 107 个非 Air runtime raw 的语义摘要保持，70 个道具载荷保持字节一致；Gait／Mantle／Air 目标门禁均无失败，123 个最终 FBX 全物理关键帧回读通过，1,536 个载荷摘要全部有效。

六份 V4／Refactored 最终下落资产的中段平均屈膝峰值相同：Fall 100.499° → 124.062°，Fall_Fast 86.782° → 110.430°，Flail 88.811° → 110.541°。这里比较同一周期的中段，而非各自全周期最大值；完整曲线和峰值相位保存在交付审计，三种动作均比旧包中段增加超过 15°。这是本次蹲缩的任务检查，未替换或放宽原生产门禁。

组合包再次执行优化构建（0 warning／0 error）、Godot 导入、P2B、六域绑定和最终请求编译，见 `combined-v1/runtime/`。五项选定实际游戏案例全部通过；e2e 包装仍因未选择完整方向范围退出非零，报告保留 `overallRuntimeGatePassed=false`、`completeMatrix=false`。未重新执行不受本次 Air 修改影响的完整 Mantle 蒙皮／平台矩阵，也未沿用其历史局部通过作为本次新证书；此前 Root 开启／取消姿态及负对照的开放失败保持。

组合 Air 全组 11/11 通过，包含 30／60／120 Hz 单线程／并行真实 30 m 下落、三个既有混合／时钟／Lean 夹具和两组 GPU 检查。共 2,144 个物理观察帧、1,334 个空中帧和 22 张提交身份一致的 GPU 截图，包与程序集前后摘要保持。三 Hz 单／并行全部逐帧记录一致，分别为 119／238／477 帧；与上一默认包对照，这些案例的胶囊位置及速度逐帧变化均为零。稳定空中躯干倾斜最大 14.755°，Root 相对骨盆高度 0.891–1.006 m，平面残差最大 0.008312 m。详见 `final-air-summary.json`。

![同帧旧包与新蹲缩对照](../../artifacts/kimodo_als/air-20261010_crouch_v2/air-crouch-before-after.png)

实际检查帧 60／75 的同场景 GPU 截图，新包腿部收起更明显。图来自最终组合包，保留原截图和提交身份回执，不是示意图。没有逐顶点 Air 蒙皮、连续 UE 图、长时间或性能验收，截图上的 FPS 不能用于本次性能比较。

## 默认接入与普通启动

最终相关检查通过后才更新 `assets/config/als_resource_pack.json`，并验证旧指针仍与第一轮保存的字节备份相同，防止覆盖并发选择。选择回执为 `default-selection.json`，资源扫描按新默认重新收敛。

清除所有 `ALS_` 环境覆盖后，普通 `scenes/demo/als_demo.tscn` 运行 180 帧，退出 0、无 ERROR，日志确认新包、`replacements=123`、Root Motion 关闭；见 `ordinary-default-restart.log`。验证采用 Dummy 音频，不代表实际音频验收。

普通游戏窗口通过 GUI 可执行文件打开并置于前台，启动日志确认选择新包；就绪检查确认有响应且窗口句柄有效。回执为 `ordinary-default-direct-launch.json`／`ordinary-default-direct-ready.json`，日志为 `ordinary-default-direct.log`。首次 console 启动也记录了正确选择，但后续未取得活动窗口证据，保留其日志，不将它作为持续窗口就绪证据。窗口供用户测试，不代表人工验收通过。

最终包 SHA256：`f6d2bcbbfe05d69726f1108759b88348980a8beb1a41fe282a5e265935c97548`。当前仍缺 60 个非道具身份并使用既有回退，整体生产／完整运行／人工验收门禁保持开放。生成资产和证据留在主目录，未新增提交或推送。
