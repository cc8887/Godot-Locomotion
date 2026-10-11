# Kimodo 上游复用与缺失资源派生

`tools/produce_als_resources.py --scope derived` 使用显式上游执行 receipt 补当前包的缺失非道具资源，不运行新的 diffusion。默认仍执行作者节点、纯脚本重定向、Root/Pelvis 分解、曲线制作、最终 FBX 回读、Godot 导入和运行检查。候选包独立保存；正式启用仍遵循现有门禁。

## 静态姿态独立采样

默认方向规则的 `sampledGaitPoses` 定义四个采样节点：`Lean_Pose` 和 `Run_Forward_Pose` 来自 `Run_Forward` 候选，`Run_Left_Pose` 来自 `Run_Left_Forward` 候选，`Crouch_Walk_Pose` 来自 `Crouch_Walk_Forward` 候选。`Run_Right_Pose` 从采样后的左侧姿态镜像。共五个姿态语义，再通过既有 offset 节点生产五个 Lean 与四个 Acceleration。

条件化周期候选取第二周期的 1/4 相位，帧号为 `periodFrames + round(periodFrames * 0.25)`。普通 loop 候选沿用原裁剪起点与原生周期长度，取裁剪区间的 1/4 相位；采样不修改或闭合循环接缝。均先去除原数值条件的平面行进路径与初始平面原点，保留 Root 的竖直变化、平面残差与全部局部骨骼旋转，随后冻结为原资产所需键数。

采样输出始终为 SOMA 完整姿态。`pose-sampling.json` 记录源文件摘要、原生相位帧、时间、移除的平面位移和静态验收范围；交付 receipt 绑定实际输出。目标转换再次核对采样、上游文件及冻结姿态。采样姿态不再依赖缺失循环的目标原点；Lean/Acceleration 仍继承同一已交付基底，保证 additive 求差与原点一致。

循环的支撑、速度、滑移、接缝及周期修复门禁保持。一个候选可以用于静态采样，而其循环修复仍失败；静态采样 receipt 不构成循环、最终蒙皮或人工动作质量验收。没有批准的循环母动作时，不生成其镜像循环或跳跃过渡替代品。

## 显式复用

`--upstream-receipts` 可重复指定完整 `receipts.json`，或包含通过源节点 receipt 的结果列表。只复用匹配语义和数值行进路径的真实生成候选，以及带通过检查与轨迹的周期/镜像源节点。绑定输入本身不能作为真实 Kimodo 生成证据，原版姿态约束参考也不能交付为母动作。

所有输入文件、receipt、SOMA rig 字节摘要及 FPS 均验证；周期还验证检查绑定的运动数组摘要和原生时间域。复用节点在新图中留下原执行 receipt 与明确的依赖边界，不覆盖派生节点为任意 NPZ。原 seed、提示词等设置保留在上游证据中，复用不声称执行了当前规则的新 diffusion。

`--base-pack` 保留当前包已有源交付，校验包载荷、作者输出、身份和 rig 后合并新增资源，随后重新转换整个组合包。它不会修改当前活动指针。没有新增交付时，不用原包资源组成虚假的零新增成功。

派生范围将没有可复用输入的 generate 节点标为明确的缺失输入，继续处理独立资产。`--runtime-case` 可选择相关场景，省略时运行完整矩阵；部分场景检查必须如实记录覆盖边界。

实际运行与失败证据见 [本批验证](../verification/2026-10-10-kimodo-derived-resources.md)。

## 执行示例

在项目主目录使用新版本目录，读取当前活动包作为保留输入：

```powershell
$derivedStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$derivedBase = (Get-Content assets/config/als_resource_pack.json | ConvertFrom-Json).pack -replace '^res://', ''
G:/Github/kimodo/.venv/Scripts/python.exe -B tools/produce_als_resources.py run `
  --scope derived --base-pack $derivedBase `
  --upstream-receipts artifacts/kimodo_als/nonprop-regeneration-20261009-v1/receipts.json `
  --upstream-receipts artifacts/tests/2026-10-08-directional-gait-regeneration-v1/final-profile-results-v2.json `
  --output ("artifacts/kimodo_als/derived_" + $derivedStamp) `
  --pack-output ("assets/generated/als_nonprop_derived_" + $derivedStamp) `
  --kimodo-repo G:/Github/kimodo --python G:/Github/kimodo/.venv/Scripts/python.exe
```

示例明确选择本机已有两批源证据；新批次改用相应 receipt，不自动扫描所有历史目录。`inspect` 可先查看依赖与缺失输入。还有缺失母动作、源或目标检查失败时，完整运行保留证据并返回非零退出码。
