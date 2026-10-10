# Mantle 类型分派、接触约束与导出验证

实现合同见[Kimodo Mantle 生成与导出](../architecture/kimodo-mantle-export-policy.md)。本次实现覆盖 High、Low_Left、Low_Right 三个母动作及其 V4/Refactored 六个身份。

## 本次实现

- `graph.py` 为 Mantle 建立不可变参考、位置/旋转条件、独立作者门禁与交付节点；低攀爬两个方向分别生成，不借用步态镜像。高攀爬左手支撑到 58/30 s、右手到 38/30 s，避免只检查早期共同抓沿窗口。
- `mantle.py` 在 SOMA 生成条件中求整个窗口可达的固定手部锚点；作者阶段和目标阶段分别做有限幅度手臂 IK，检查掌向、位置、漂移、Pelvis 路径、结束双脚支撑、手臂 bind offsets 与单位缩放。SOMA/ALS 比例适配与最终质量门禁分开记录。
- `root_ownership.py` 要求显式名义 Mantle Root XYZ profile，保留 Pelvis 垂直残余和身体 FK。缺少 profile 时拒绝，禁止将 Pelvis 全部 XYZ 抽到 Root。
- 发布保存源/目标门禁、前后 raw、各手接触曲线与目标位置/旋转，以及既有 Root 曲线。证书绑定不可变参考和最终 motion/raw；正式启用必须有六个逐资产目标证书。Viewer 支持读取 Mantle 作者门禁。
- `--scope mantle` 进入完整转换、最终 FBX、Godot 验证链；当前全局非道具闭包要求及既有 gait 原生对照阈值保持。

## 数值与 FBX

证据目录：`artifacts/kimodo_als/mantle-pipeline-20261009-final/`。`run_controlled.py` 使用 SOMA 参考条件加 1 mm 平滑横向扰动作为受控输入，经正常作者门禁、NPZ/BVH 交付、纯脚本重定向、Root 分解、曲线、最终 FBX 写出与 ufbx 回读。它没有执行 Kimodo 模型推理，不能计作新模型生成成功或生产替换验收。

受控包为 `assets/generated/als_nonprop_20261009_mantle_final/resource-pack.json`，保留 `diagnostic` / `productionReady=false`。六个目标质量检查及六个最终 FBX 逐物理骨骼逐键回读通过，Root 曲线与独立提取曲线各六份；177 个其他非道具身份保持原版 fallback 并在缺失清单记录。关键证据包括 `controlled-result.json`、`conversion/pipeline-result.json`、包内 `target-mantle/` 和 `root-ownership/*/fbx-readback.json`。

High 的目标左手最大修正约 8.936 cm、右手 6.267 cm；Low_Left 的右手约 6.317 cm，Low_Right 的左手约 6.263 cm。修正预算是此新类型的有限比例适配，不是最终质量允许误差：修复后仍执行 2 cm / 12° / 参考漂移加 1 cm 门禁。大幅偏移、错误掌向、伸缩骨骼和支撑窗口后半段反例均被拒绝。

普通当前 v2 诊断包的三种生成 Mantle 用作真实负例，均未通过新目标门禁，见 `existing-assets-negative.json`。失败包括手部位置/掌向/漂移、Pelvis 起止及路径、结束脚部位置/朝向；未对坏源做超幅度修正来通过。

Python 整套管线 **175 项测试通过**，最终结果见 `python-tests-release.log`；额外覆盖真实 Kimodo `SOMASkeleton30` 的条件加载，而非只检查 JSON 形状。SOMA77 条件经官方 77→30 转换后，所有手部条件保留非空位置和旋转索引；所检条件窗口手部漂移最大约 0.301 cm，低于测试的 0.5 cm 上限。这仍不代表实际扩散推理或自动视觉质量通过。

## Godot 运行

真实 Godot 4.7.2 Mono 导入、`dotnet build GodotALS.csproj -p:Optimize=true`、P2B 编译及六域源请求刷新通过。首次受控验证漏掉编译/源请求刷新，出现缺失 compiled resource 与 manifest/player 绑定不匹配；失败保留在 `mantle-pipeline-20261009-controlled/runtime-mantle/`。按完整链补齐后，用新目录重新验证，未覆盖失败日志。

最终完整支撑窗口版本的 `runtime-mantle/summary.json` 为 **12/12 场景通过**，载荷无变更、Root Motion 设置文件无变更：

- 低/高攀爬，30/60/120 Hz，Root Motion 关闭：6 个场景。
- 低/高攀爬，60 Hz，Root Motion 开启：2 个场景。
- 移动目标、取消、销毁、阻挡：4 个场景。

这些场景使用普通 Demo 的真实角色、物理查询和提交链。矩阵明确是 `diagnosticPartialPack=true`、`completeMatrix=false`；命令整体退出为拒绝，因为未选择方向质量覆盖且非道具闭包不完整，不应把 12 个运行场景通过改写为完整生产矩阵通过。

## 覆盖边界与后续

本次完成类型分派、数值条件、接触 IK、Root/Pelvis 特化、曲线/证书、FBX/Godot 受控验证。没有运行新的 Kimodo 扩散批次，没有执行完整 71 场景、渲染截图或人工视觉验收，也未实现 GASP `attach` / Traversal Motion Warping runtime。

当前活动指针继续选择 `als_nonprop_20261009_idle_arms_v2`；夹具 Viewer 批次明确标注无 Kimodo 推理，不作为最新真实生成批次。后续使用 `run --scope mantle --keep-going` 生成实际候选，保留独立成功/失败结果并跑完整管线，再根据目标门禁和视觉结果决定是否交付。实际推理失败不能用受控参考夹具替代生产资产。
