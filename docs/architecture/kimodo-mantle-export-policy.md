# Kimodo Mantle 生成与导出契约

资产按 `family` 分派。Locomotion 继续使用既有事件选周期、足底相位 IK 和目标步态门禁；Mantle 是非周期接触动作，使用 `als-mantle-contact-v1`，不执行步态 loop、平面 Root 平滑或低攀爬的通用左右镜像。

## 生成参考与手部约束

不可变 Refactored ALS 原始动画及骨架只作为生成条件。参考载荷、骨架和索引摘要进入缓存身份；不能直接把 `native_mantle_reference` 节点当作生成交付。

| 动作 | 支撑手 | 原版抓沿窗口 | 名义 Root 终点，cm |
| --- | --- | --- | --- |
| Mantle_High | 双手，独立释放 | 左 0.600–1.933 s，右 0.600–1.267 s | (0,65,200) |
| Mantle_Low_Left | 右手 | 11/30–22/30 s | (0,65,100) |
| Mantle_Low_Right | 左手 | 11/30–22/30 s | (0,65,100) |

支撑侧来自原版轨迹，不能从 `Left/Right` 后缀猜测。低攀爬两个方向分别生成和检查。

参考姿态通过出向重定向的中性基准逆变换进入 SOMA。SOMA 和 ALS 的肩部/手臂比例不同，因此不能按腿长比例直接换算原版手部坐标。先取逆变换参考在整个接触窗口内的平均手部位置，再求一个整个窗口可达的固定锚点；锚点适配上限为 5 cm，保持固定骨长。不可达时拒绝参考，不能逐帧截断目标制造滑手。

Kimodo `left-hand/right-hand` 条件同时包含 FK 后的全局位置和旋转。抓沿窗口提供最多 19 个条件帧；开始、抓沿、释放、接地过渡和结束提供全身条件，并将全身条件之间的间隔限制在 0.25 s 内。结束支撑阶段另约束左右脚位置/朝向。生成低攀爬可以使用 2 s 的模型输入，交付前统一映射回原版 1.5 s，再独立检查最终帧，禁止交付后重新变速。输入时长由整数采样数除以 FPS 得到，避免 `playLength + 1/FPS` 的浮点误差令 High 少生成一帧。

髋部另使用原生 `end-effector` / `Hips` 条件，在最多 19 个均匀采样关键帧限定参考 XYZ 与朝向；各约束类型均遵守稀疏关键帧预算，Hips 不属于密集二维 Root 条件的例外。High 在落地后 73/30 s 增加全身稳定条件，约束接地后的下沉。其余关节和帧仍由模型生成，作者阶段不以参考 Pelvis 覆盖生成轨迹；后续 Root/Pelvis 分解独立执行。

保留 SDK 原生后处理，其手脚约束修复仍有效；但它未给通用 `end-effector` / `Hips` 建立后处理掩码，竖直轨迹修正只读全身关键帧，不能把 Hips 条件视作硬保证。有限间隔全身条件和推理后的严格检查共同约束身体路径。关闭原生后处理的同种子对照保留为失败证据，不作为默认路径。

## 修正与门禁

作者阶段在最终交付帧执行手臂两段 IK。最大手部位置修正 8 cm、旋转修正 30°；保持原始肘部弯曲平面，不拉伸骨骼、不平移 Root/Pelvis。修复后抓沿位置误差不超过 2 cm、掌向误差不超过 12°、窗口漂移不超过参考漂移加 1 cm。Pelvis 全程最大参考偏差 15 cm，起止偏差 5 cm；结束脚部位置偏差 3 cm、朝向偏差 12°。失败保留 `mantle-validation.json`，阻断对应交付，继续独立成功分支。

目标阶段使用 ALS 原版组件空间手部轨迹再次做有界 IK，保持目标骨架骨长、局部位置与缩放。此新类型的目标手部修正预算为 10 cm / 30°，与源作者阶段的 8 cm 预算分开；它用于适配两套手臂比例，不能替代修复后 2 cm / 12° 的质量门禁。完整高攀爬参考转换的左手需要约 8.94 cm 目标修正，不能套用 locomotion 足底修正预算；既有步态算法和原生对照阈值保持。不能以源门禁替代目标门禁。目标检查包括每只手独立支撑窗口的位置/朝向/漂移、Pelvis 路径与起止位置、垂直残余范围，以及 `PoseGrounded` 完全接地后的双脚位置/朝向。结束脚部位置上限 5 cm；Pelvis 垂直残余范围至少为参考的 50%。超出修复幅度仍保留原姿态与失败，禁止通过大幅修正掩盖坏源动作。

`target-mantle/<source sha256>/` 保存作者证书、目标质量、修复前后 raw、接触曲线；质量报告绑定最终 raw 的语义摘要。诊断包可以继续 FBX 导出和运行检查并记录失败；正常 stage 与正式启用必须通过完整逐资产 Mantle 门禁，正式启用还验证报告文件摘要和最终 raw 身份。

High 的目标 Mannequin 左手 wrist 在原版标记上方固定偏移 1 cm，使不可由指节独立移动的拇指根部获得顶面空间。该偏移计入上述 10 cm 修复预算，并继续对未偏移原版参考检查 2 cm 位置误差；烘焙运行锚点使用同一固定偏移。它只属于该目标网格的适配，不改变 SOMA 生成条件、骨长或 Root/Pelvis 轨迹。

## Root/Pelvis 与曲线

当前 ALS 资产合同按原版名义 approach/lift 路径分配 Root 的 XYZ 和旋转。随后对所有 Root 直接子骨骼做逆变换，保留生成身体 FK，因此弯腰、蹬起和重心起伏留在 Pelvis 残余。禁止把 Pelvis 全部 XYZ 或完整高度变化当作 Root 路径。未知 Mantle 类型或缺少显式 Root profile 时拒绝，不能退回通用 locomotion 分解。

名义路径是已配置 ALS 1 m/2 m 资产合同，不是任意障碍高度的自动推断。生成的身体路径必须通过独立参考偏差检查；Root 轨迹本身不能证明动作质量。后续自定义高度/时序需要新 profile 与验收，不能复用旧接触窗口。

既有逐资产 Root 位移、旋转、距离 sidecar 仍从最终 Root 烘焙。新增 `contact-curves.json` 提供左右手支撑和结束双脚支撑时段；它是管线元数据，尚不是 ALS AnimGraph 或 Motion Warping 的输入 API。`PoseStanding/PoseGrounded` 维持原版语义和通过检查的原版时域；不捏造 GASP/ALS 未确认消费的曲线名。

### 与导入项目配套的游戏控制曲线

发布阶段还必须交付 `config/refactored_mantle_contacts.json`；它与上述中间 `contact-curves.json`、Root sidecar 分开，是 Godot ALS 实际读取的控制资源。每个 sequence 的最终时间轴包含前移 `advanceCm`、抬升 `liftCm`、剩余高度、双手支撑权重及固定平台局部腕点／掌向。文件绑定最终姿态与 Root 输入的字节摘要，并登记到资源包载荷清单。

High 的 `advanceCm` 需要基于最终目标蒙皮烘焙：计算台面以下身体顶点的前缘，按平台正面保留 0.5 cm 余量，延后会导致穿入的前移；通过未来约束和 200 cm/s 增量上限保持单调，并严格保留原落点。Low 当前保留 Root 派生前移。烘焙不改 Root/Pelvis 和抬升通道；High 记录 `bodyAdvanceControl` 的模型摘要和算法参数。更换最终输入后必须重烘焙，不能沿用旧曲线。

这构成生产端与消费端的配套要求：Pipeline 必须导出，导入项目必须识别并应用。Godot ALS 的完整接触路径验证版本、摘要、sequence 和时间轴，将烘焙的前移／抬升绑定到实际 montage；不能用原 Root 轨迹覆盖修正后的前移。没有对应控制文件时明确失败。任意其他项目仅能读取 FBX，不代表支持这一 Mantle 合同；接入前需实现等价消费或明确拒绝完整验收。完整字段、计算公式、诊断补烘焙规则和现有校验边界以[双向交付契约](als-mantle-contact-control.md#pipeline-与导入项目的双向交付契约)为准。

新导出额外保留修正前 `rootAdvanceCm`，写入 `curveContract`／逐 sequence 的 `curveAnalysis`，并在包中登记 `mantleCurveContract`。`mantle-curve-semantics-v1` 明确区分原始距离、时间驱动的避障进度和固定支撑；当前仅声明高度入口匹配，持续距离驱动播放／前移距离整段反查／Root Motion Warping／warp 窗口导出均为 false。平台段分析随真实关键帧烘焙，发布和运行前检查拒绝角色混淆、旧数据或超出实现范围的能力声明。非递减曲线不自动意味着唯一反查或完整 Warping；详见[固化判断](als-mantle-contact-control.md#distance-matching-与-motion-warping-的导出判断)。

本实现没有添加 GASP 的 `attach` 骨骼，也没有扩展目前 Emote 专用的 Motion Warping runtime。手掌/手腕端点与 GASP 的 ledge warp 标记是不同合同。

作者／目标阶段的抓沿窗口质量不能替代运行时完整蒙皮与实际障碍的检查：非支撑手、释放／提腿阶段和 Root 关闭时的程序化抬升都可能穿模。`verify-kimodo-mantle-scene.py` 对已知 Low 夹具增加 actor yaw 与完整蒙皮顶点箱体交叠门禁，当前候选与原版均照实记录几何失败；该局部诊断尚不等于四身份／High 的完整发布矩阵。见[运行时复核](../verification/2026-10-09-mantle-runtime-orientation-penetration.md)。

## 入口

仅生成 Mantle 并继续完整转换/FBX/Godot 验证：

```powershell
& G:\Github\kimodo\.venv\Scripts\python.exe tools/produce_als_resources.py run `
  --scope mantle --keep-going --kimodo-repo G:\Github\kimodo `
  --output artifacts/kimodo_als/<fresh-batch> `
  --pack-output assets/generated/als_nonprop_<fresh-batch>
```

Mantle-only 是部分非道具包，缺失身份照实记录，不能作为完整生产包启用。`--scope non-prop` 的普通批次自动使用同一 Mantle 分派。只有明确诊断才使用 `--authoring-only`。实现及本批覆盖见[验证记录](../verification/2026-10-09-kimodo-mantle-export-policy.md)。
