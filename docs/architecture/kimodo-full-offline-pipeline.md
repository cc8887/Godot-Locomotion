# Kimodo 完整纯脚本生产入口

生成请求默认使用 `tools/produce_als_resources.py run` 或 `scripts/produce-non-prop-als.ps1`，执行作者 DAG、目标资产转换、Godot 导入和运行验证。`plan` / `inspect` / `inputs` 保持只读规划；局部作者试验需明确 `--authoring-only --scope all --target ...`。

## 阶段和制品

1. 作者 DAG 逐分支执行，缺失或质量失败的依赖阻断下游，独立分支继续；保存具体失败和全部已执行回执。
2. 将成功的非道具交付写入带身份、原始 NPZ/BVH 摘要的 bundle。检查源 rig、每个输出的文件字节哈希；禁止道具混入转换。
3. `offline_retarget.py` 把 SOMA77 全姿态转换到导出的 ALS 68 根物理骨骼。源为米／Y-up／T-pose，目标为厘米／Z-up／原始绑定姿态。转换位置时明确乘 100，腿长比例先统一为米再计算无量纲比例；旋转使用基变换共轭，不把反射矩阵当作旋转。肢体中性方向对齐后，保留目标骨长和绑定变换；保留未映射 twist 的父级旋转关系。
4. 对已支持的平脚周期身份先用脚底竖直极值推断相位，再做有界接触 IK，随后复用目标空间 Root/Pelvis 分解，按 DAG 基底依赖继承水平原点，重建辅助 IK，保存 authored-root 和 ALS 原地 raw。每个包含根位移／旋转或声明 Root Motion 的资产导出独立轨迹文件；源 gait 去掉的行进轨迹单独保留。相位候选不要求修复前的目标支撑门禁通过；修复后严格门禁保持。见 [相位与 IK 合同](kimodo-target-vertical-phase-ik.md)。
5. `offline_fbx.py` 写出完整物理骨骼、原绑定姿态和原采样时间的 FBX，保留 ALS 导入绑定名 `Unreal Take`。骨骼名沿用不可变目标网格 FBX 的实际大小写，不能直接用 raw FName 的展示大小写。坐标声明与原始网格一致（right +Y／up +Z／front +X），原生 target 到 FBX 的位置和旋转应用同一 Y 反射，并在回读时逆变换。独立原网格对照检查全部绑定 TRS 和轴声明。不导出虚拟骨骼。`ufbx==0.0.5` 在独立依赖目录逐帧回读局部 TRS，验证所有物理骨骼／父级、绑定、采样帧；位置 ≤0.01 cm、旋转 ≤0.1°、缩放 ≤1e−4。代码中的 ufbx 列表包装必须保持存活，不能保留临时列表中悬空的子对象。参见 [ufbx 动画求值](https://ufbx.github.io/elements/animation/)。
6. 优化构建、Godot 实际导入／资源审计、六域运行图请求，并在 pack 内同步请求摘要。执行现有非道具场景矩阵及渲染截图；正式验收保持原门禁和原版对照标准。

整个转换不启动 UE，不依赖 UE/Blender/FBX SDK。已有 UE 原生转换入口仍可用于历史取证；离线回读记为 `offline-ufbx-readback-verified`，不能冒充 UE DataModel 原生回读。

## Mantle 控制资源与消费能力

含生成 Mantle 的包在最终姿态／Root 配置写入后，必须烘焙并登记 `config/refactored_mantle_contacts.json`。其中 High 的 `advanceCm` 按实际目标蒙皮与台沿模板延后身体前移，独立于 `liftCm`、固定双手锚点和支撑权重；它不会改写 FBX 的 Root/Pelvis。Root sidecar、最终 FBX 回读或中间 `contact-curves.json` 均不能替代该交付。

Pipeline 的运行验收依赖导入项目具备相应消费能力：读取选中包控制文件，核对最终输入摘要／sequence／原采样时间，将修正后的前移和独立抬升绑定到 montage，并按支撑权重处理平台局部手部锚点。当前 Godot ALS 已通过 Import／Core／Godot 三层执行此路径；完整接触模式缺失曲线时报错。其他消费项目必须实现等价能力或明确标为不支持，不能把普通 FBX 导入成功视为曲线接入成功。

正式发布要求曲线随包交付、记录载荷摘要，并在启用前重新烘焙比较最终输入；诊断补烘焙不改变生产资格。算法、字段、Root 开关及 Import 不独立重算几何的边界见[双向交付契约](als-mantle-contact-control.md#pipeline-与导入项目的双向交付契约)。

运行前另执行 `mantle-curve-semantics-v1` 检查，保留独立原始 Root 前移、检查真实前移平台段及包／控制文件能力声明一致。当前只支持高度入口匹配，前移是时间驱动；持续距离驱动播放和 Root Motion Warping 不得声明已实现。失败保存 `runtime-result.json` 并阻止启动，成功保存 `mantle-curve-contract.json`；此检查不能替代后续实际场景和正式启用的几何复核。旧包若没有新合同，需从对应最终输入构建新版本，不在活动包上补字段。见[曲线用途及门禁](als-mantle-contact-control.md#distance-matching-与-motion-warping-的导出判断)。

## 完整执行和质量验收

完成转换不等于动作质量合格。角度重定向保留骨长，已支持的平脚周期追加接触 IK，但支撑接触、步幅、脚部朝向和最终蒙皮仍须独立验收。目标检查或修复失败时，诊断包保留失败和未应用修复状态。检查器未支持的身份标为分析失败，不能当作通过。

源闭包不完整时，诊断包明确列出 `missingNonPropSources`，未替换身份使用原版回退，道具保持原字节。`publish stage` 的正式全闭包检查不变；单独的诊断模式允许生成可查看／可运行的部分包。`publish activate` 明确拒绝诊断包。完整入口仍以非零退出码报告源／目标／运行失败。

`verify-als-nonprop-e2e.py --diagnostic-partial-pack` 只用于这类包：验证替换和缺失身份恰好覆盖当前非道具合同，再执行相同场景、物理／脚部断言和截图检查。即使场景通过，也不生成完整替换证书；`completeMatrix=false`、`diagnosticPartialPack=true`，总结果保持失败。默认正式运行仍要求完整替换范围。

## 同批次可见性和恢复

源 generated/master 与目标 retarget/root/runtime/fbx 合并在同一批次 ID。合并前检查目标 NPZ 路径属于该源批次目录，拒绝把其他批次的资产接进来；Preview 记录每阶段真实路径及字节哈希。正式活动包由既有指针单独显示。

每次正式生产使用全新输出和 pack 目录，已有失败版本保留。显式 `--resume-conversion` 仅复用已验证的源 bundle、回执和 retarget 输出；修改转换算法必须从源重新转换，不能恢复旧 retarget。`--finish-existing-pack` 只完成未启用诊断包的最终导出元数据，不能修改活动包。
