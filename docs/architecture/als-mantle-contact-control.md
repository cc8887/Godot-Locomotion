# Mantle 轨迹与固定支撑控制

Mantle 使用非周期的前移／抬升进度、剩余高度，以及两只手独立的接近、支撑、释放权重。支撑锚点固定在平台局部空间；身体移动时锚点不变，平台移动时由同一平台变换运输锚点。

## 烘焙合同

`tools/kimodo_pipeline/mantle_contacts.py` 读取最终 `refactored_mantle_animation_inputs.json` 和 `refactored_mantle_root_tracks.json`，保留原 FPS、关键帧时间和结束时刻，生成 `refactored_mantle_contacts.json`。每个实际 sequence 独立包含：

- `advanceCm`：沿终点水平位移方向的前移进度；`liftCm`：相对首帧的抬升；`remainingLiftCm`：当前位置高度减终点高度，为负的剩余高度。
- `leftHandSupport` / `rightHandSupport`：接近和释放各一帧线性过渡，完整支撑区间为 1，两只手允许分别释放。
- 左右手各一个固定 wrist 位置与 palm 朝向。锚点不随支撑时间重新采样。
- 两份最终输入的字节 SHA256、原版条件参考身份及摘要。运行时拒绝跨版本输入、缺失 sequence、不符时间轴和反向抬升／前移。

当前原版条件夹具的 ledge 位于 mesh 原生坐标 Y=50 cm、Z=名义平台高度；固定 wrist 锚点取已验证原版接触区间的平均位置／旋转，保留 wrist 到掌面的偏移。这是明确的 ALS 条件模板，并非对任意新资产自动识别支撑。Low_Left 的支撑手是右手，Low_Right 的支撑手是左手；High 的右手先于左手释放。新动作必须提供新的接触合同与几何验收。

数据属于游戏控制资源，不伪装成原生 AnimGraph 曲线。`PoseStanding`、`PoseGrounded` 等原生曲线继续使用原身份和采样语义。生成动画不以原版替换，Root 保留名义整体路径，Pelvis 保留生成动作的相对运动，手指仍来自资产。

High 的 `advanceCm` 额外以最终目标蒙皮烘焙身体避障：排除由手臂 IK 独立控制的手部顶点，计算每个关键帧在台面以下的身体前缘，保留 0.5 cm 几何余量。前移只能相对原 Root 延后，通过未来约束的反向累计最小值保持单调，并限制速度为 200 cm/s；无法到达原着陆终点则拒绝烘焙。`liftCm` 与 Root／Pelvis 轨道不因此改写，身体控制元数据记录目标 mesh 摘要、余量、速度和最大延后距离。

`bodyBlendReadyTimeSeconds` 取最终腕点首次进入台沿 20 cm 接近范围的时刻，让身体淡入在抓沿前完成；它只提前准备段，不改变完整支撑区间。`transitionLiftCm` 为过渡腕点的台面法线抬升幅度，当前烘焙为 3 cm。实际幅度为 `4 * amplitude * weight * (1-weight)`，接近／释放中点最大、两端为零，完整支撑仍严格使用固定锚点。旧数据没有此字段时取零。

目标资产的手指和接近／释放腕部可以在生产后做有界几何 IK，并烘焙回这些生成轨道；详见[手部蒙皮修复合同](kimodo-mantle-hand-skin-repair.md)。运行锚点控制与离线指节修复共同工作，不能互相替代最终蒙皮验收。

新包在 `publish stage` 完成最终配置后自动烘焙，曲线作为配置 payload 加入包字节摘要；正式激活额外检查曲线仍对应最终姿态／Root 输入。已有诊断包可以独立补烘焙，保留原包字节：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe -m tools.kimodo_pipeline.mantle_contacts --config assets/config
& G:/Github/kimodo/.venv/Scripts/python.exe -m tools.kimodo_pipeline.mantle_contacts --config assets/generated/als_nonprop_20261009_mantle_v4/config
```

输出为 `assets/generated/mantle_contacts/<pose-sha>-<root-sha>.json`。加载优先读取选中包的配置曲线，再读取摘要匹配的独立补烘焙文件；都不存在时明确报错。独立补烘焙不改变包的 `productionReady` 或正式激活资格。

## 身体避障前移曲线如何得出

`advanceCm(t)` 表示动画播放到时刻 `t` 时允许的累计前移距离。High 的身体前移可以暂时保持不变，同时动画继续播放、身体继续抬升；等腿和躯干越过台沿后再完成剩余前移。它属于游戏移动控制，不修改 FBX 的 Root／Pelvis，也不冻结 montage。Low 同样导出前移通道，但当前直接取名义 Root 轨迹，不执行 High 的目标蒙皮避障烘焙。

烘焙首先将最终 Root 相对首帧的水平位移投影到终点水平方向，得到原始进度 `P(t)`。随后通过最终骨骼 FK 与不可变目标模型的蒙皮权重，逐关键帧计算身体顶点位置；排除手部／手指权重之和至少为 0.5 的顶点，这些顶点由独立的手部接触控制负责。

当前模板使用原生 mesh 坐标 X/right、Y/forward、Z/up，单位厘米，平台正面 `Y=50`、High 台面 `H=200`。对 `Z < H-0.5` 的身体顶点求最大前缘 `Ymax(t)`；若没有此类顶点，本帧无需正面限制。允许前移上限为：

```text
excess(t) = max(0, Ymax(t) - 50 + 0.5)
limit(t)  = max(0, P(t) - excess(t))
```

对上限做反向累计最小值，让更早的前移不超过后续帧允许的空间，避免先进入平台再后退；随后限制相邻帧前移增量不超过 `200 cm/s × dt`。结果只能相对原 Root 延后。最终前移必须与原 Root 终点相差不超过 0.001 cm，否则报错，不能通过缩短落点使烘焙通过。

`liftCm` 独立保留原 Root 抬升。运行时将前移曲线重定标为从动作进入点到终点的 0–1 水平进度，再映射到实际起点／落点；抬升使用独立的高度进度。因此曲线中的厘米数是名义资产控制量，不是所有实际平台上的角色世界位移。实际高度超过剩余名义抬升时的准备段补偿是另一项运行机制。

这是针对既定目标模型与台沿模板的离线净空约束，不进行运行时障碍搜索，也不建模任意平台宽度、背面或装备几何。更换体型、蒙皮、装备、平台形状或接触合同需要重新烘焙／验证；前移控制不能替代手部 IK 或最终完整蒙皮门禁。

## Pipeline 与导入项目的双向交付契约

含生成 Mantle 的交付必须同时满足生产端导出控制资源和消费端识别控制资源。只导出／导入动画 FBX 不构成当前 Godot ALS Mantle 的完整交付；既有 Root 位移 sidecar、作者／目标阶段 `contact-curves.json` 和原生 AnimGraph 曲线也不能代替下列控制文件。

| 项目 | Pipeline 导出要求 | Godot ALS 导入／使用要求 |
| --- | --- | --- |
| 交付位置 | 选中包的 `config/refactored_mantle_contacts.json`，登记到 `resource-pack.json.files` 并保存字节 SHA256 | 经同一活动包的 `ConfigRoot` 加载，与该包姿态、Root、montage 配置配套 |
| 文件身份 | `schemaVersion=1`、`policy=mantle-fixed-ledge-contact-v1`、`maximumCorrectionCm=10`；`animationInputsSha256`／`rootInputsSha256` 绑定最终两个输入 JSON | `AlsMantlingContactCompiler` 校验版本、策略、预算和输入摘要，拒绝其他输入对应的旧曲线 |
| 逐资产时间轴 | 按实际 `source` 交付全部 sequence，保留最终 FPS、关键帧数、起点和结束时间 | 校验 source 集合、关键帧数、逐帧时间与动作时长；不能只按语义名套用一条共享曲线 |
| 逐关键帧字段 | `timeSeconds`、修正前 `rootAdvanceCm`、修正后 `advanceCm`、`liftCm`、`remainingLiftCm`、`leftHandSupport`、`rightHandSupport` | Import 消费既有控制通道并编译为进度／接触资源；`rootAdvanceCm` 供 Pipeline 校验原始距离独立保留，不用于覆盖已修正的前移 |
| 固定支撑与过渡 | `anchors.left/right.positionCm` 与 `quaternionXYZW`；当前包还导出 `transitionLiftCm`，High 导出 `bodyBlendReadyTimeSeconds` | 运输平台局部锚点并执行手部 IK；缺失过渡抬升时取零，身体准备时刻为可选字段 |
| High 身体避障证据 | `advanceCm` 必须经过上述目标蒙皮烘焙，记录 `bodyAdvanceControl`：策略、mesh SHA256、余量、速度上限、最大延后距离、身体顶点数与准备时刻 | 直接消费已经修正的 `advanceCm`，不从 Root 重建并覆盖它；当前 Import 不独立验证 `bodyAdvanceControl` 的 mesh 摘要或重算几何，发布端负责复核 |

生产端在最终姿态修复、Root/Pelvis 分解和配置写入后烘焙。之后若任何绑定输入改变，必须重新烘焙并更新包摘要。`publish.stage()` 在登记载荷前生成该文件；正式启用时 `validate_mantle_pack()` 检查文件登记与摘要，并基于最终输入重新烘焙比较。此几何复核要求独立于运行时字段／时间轴校验，不能将“JSON 能读取”当作避障算法认证。

消费端由 `AlsMantlingDemoResources` 加载，经 `AlsMantlingContactCompiler` 编译，再由 `Motion.WithContactProgress()` 绑定到实际 montage。Root 关闭且启用完整接触控制时，`AlsMantlingRootMotion.Prepare()` 将曲线用于角色水平移动，抬升独立求值；Root 开启保留既有原生轨迹路径。手部 IK 使用相同时间轴和角色提交事务。

完整接触路径先读取包内控制文件；只有包内文件不存在，才查找 `assets/generated/mantle_contacts/<pose-sha>-<root-sha>.json` 的诊断补烘焙文件。两者都不存在时加载报错；存在但版本／摘要／时间轴错误时编译失败，不降级为线性前移。独立补烘焙只用于匹配输入的诊断，不代替正式包内交付。显式 `--mantle-legacy-linear` 或 `--mantle-no-hand-lock` 是负对照路径：后者使用 Root 派生进度，不加载本文件的避障前移；不能以这些路径证明曲线已接入。

另一导入项目接入此 Pipeline 时，也必须实现等价的控制文件解析、身份／时间轴校验、前移／抬升独立应用和支撑锚点处理，或显式报告不支持并阻止此类 Mantle 的完整验收。新导出包含消费要求与已实现模式的声明，但没有自动探测消费项目能力的握手；是否支持仍需由项目集成与运行验证确认，不能假定任意 FBX 导入器都会识别该曲线。

## Distance Matching 与 Motion Warping 的导出判断

Distance Matching 用实际距离反查动画时间／推进姿势；当前避障曲线用 montage 时间驱动角色移动，两者方向不同。当前运行只使用 `remainingLiftCm` 根据实际高度匹配进入动作的参考时刻，随后为抓沿准备调整姿态起点；之后仍沿统一 montage 时钟播放，没有逐帧按实际距离推进动画。

High 前移曲线中的平台段允许身体继续抬升、提腿和释放手。同一水平距离对应多个时间与姿势，因此非递减前移不能保证唯一时间反查。整段直接按水平位移推进会停住或跳过这些阶段。未来持续距离匹配必须明确接近／支撑／上升／跨越各阶段使用的度量、平台段时间策略和上一帧进度，不能将 `advanceCm` 直接接作整段距离时间查询。

Motion Warping 在明确的窗口内，按实际目标修正原始 Root Motion 的移动与朝向；还需原轨迹、窗口、目标变换和运行时修正器。当前 Mantle 的目标插值、避障节奏和固定手 IK 不等于已接入 Root Motion Warping。避障曲线可以作为未来 warp 分配的约束，但对齐落点不保证中途路径安全，改变目标尺度后仍须检查实际蒙皮和支撑。

`tools/kimodo_pipeline/mantle_curve_contract.py` 将以上判断固化为 `mantle-curve-semantics-v1`。控制文件增加顶层 `curveContract`，资源包增加 `mantleCurveContract`，后者还指定 `controlFile=config/refactored_mantle_contacts.json`。声明为：

```json
{
  "playbackDriver": "montage-time",
  "distanceMatching": {
    "entryHeightMatch": true,
    "continuousPlayback": false,
    "advanceCurveTimeInversion": false
  },
  "motionWarping": {
    "rootMotionWarping": false,
    "warpWindowsExported": false
  }
}
```

`curveRoles` 分别标明原始名义 Root 距离、按时间控制的避障前移／抬升、用于高度入口匹配的有符号剩余距离和固定锚点接触权重。`requiredConsumerFeatures` 声明最终输入绑定、原时间轴、独立前移／抬升、固定平台局部手锚点和统一 montage 时钟。`rootAdvanceCm` 是已配置名义 Root 的修正前投影，不是 Kimodo 原髋轨迹，也不是运行时实际行进距离；完整 Root TRS 仍单独保留。

每个 sequence 的 `curveAnalysis` 记录是否严格递增、完整前移平台段的起止时间／前移量／抬升变化／左右支撑权重范围，以及 `wholeClipDistanceTimeInversionSupported=false`。平台识别使用相邻关键帧距离差不超过 `1e-5 cm`；即使某条曲线严格递增，也不能据此宣称尚未实现的持续距离驱动或 Warping 已支持。

烘焙后、stage 写包前、正式启用和完整 Pipeline 的运行前检查都会验证语义合同。独立校验器对照最终 Root 输入，检查原始距离未被避障值覆盖、抬升未变、前移只延后且保留落点、时间轴和接触值有效、平台段分析仍对应实际键值，以及包／控制资源声明一致。错误用途、缺失数据、旧摘要或将上述未实现模式声明为 true 都会失败。运行前失败写入独立 `runtime-result.json`，不启动 Godot。

这些字段以追加方式保留 `schemaVersion=1` 和原消费通道。当前 .NET Import 可以读取新文件，但不独立校验新增 `curveContract`／`curveAnalysis`；其语义门禁由 Pipeline 执行，几何重烘焙仍由正式发布门禁负责。这是能力范围声明，不是新实现的持续匹配或 Warping。旧活动包保持原字节和原游戏消费路径；旧包重新进入新的完整 Pipeline 时须生成新版本，不能修改旧包或旧补烘焙文件绕过门禁。实现／回归与实际 Import 对照见[曲线语义门禁验证](../verification/2026-10-10-mantle-curve-semantics-gates.md)。

## 运行职责与事务

Root 关闭时，`AlsMantlingProgressCurve` 使用已有 Distance Matching 的剩余高度反查抬升起点；当该点已进入支撑窗口，pose 起点提前到抓沿准备段，留出原有 montage 淡入所需时间。前移按准备段重定标，抬升仍按高度匹配点重定标，在该点之前为零；两者与手部条件使用同一 montage 时钟。首次播放即使用 pose 起点。Root 动画增量仍不提取／提交，进度不冻结 montage 时钟。`--mantle-legacy-linear` 保留旧线性负对照，`--mantle-no-hand-lock` 保留仅进度曲线负对照。Root 开启时保留原版轨迹与原生对照路径，新 IK 默认用于 Root 关闭策略。

当实际台面高于选中曲线的剩余名义抬升时，不能将额外高度均摊到整个动作；否则抓沿前的身体仍低于名义接触姿态。Core 将额外高度在准备段完成，准备段终点取身体淡入结束与接触接近时刻的较早者。之后按原名义抬升推进，着陆仍到同一实际目标。此适配由本次动作的实际高度和同一 montage 时钟求值，不改资产或接触窗口；普通高度匹配和缺少接触元数据的旧调用保持原采样。Godot 诊断进度调用相同的 Core 方法。

Godot probe 在开始时获取 ledge 位置与顶面法线，构建平台局部接触基底。Motor 将基底随既有目标平台运输，作为不可变 `AlsMantlingFrame` 数据交给 worker。目标失效、取消或运行失败即停止施加支撑。

Import 验证烘焙输入与布局。纯 .NET Core 的 `AlsMantlingHandSolver` 复用 `AlsComponentPose` 与 `AlsTwoBoneIk`，在同一角色最终候选姿态、既有最终写骨之前处理手臂。物理 montage 的实际 evaluation position 与权重控制接触，不另建时钟、反馈循环或独立提交。主线程初始化时捕获骨架相对模型的静态变换，worker 组合本帧模型变换，不能把模型根直接当作 FBX 骨架组件。

离线修复的 10 cm 预算只认证名义资产。运行时 IK 适配实际平台高度和组合后的姿态，求解真实锚点，以固定骨长限制可达范围；不能再次截断为离线预算而制造滑手。记录所需位移、实际骨长可达范围之外的距离和求解残差。支撑／释放权重由接触曲线控制，身体 montage 淡入不重复削弱已支撑的手。最终候选、取消回滚及角色身份沿用原提交事务。锚定 wrist 和 palm 不保证手指蒙皮、自由手、肘部、躯干与平台无交叠。

部分接触在骨架组件空间混合原腕点与平台锚点，再对这个目标执行完整双骨 IK；掌面朝向按同一接触权重混合。不能先求解全锚点再混合各关节，否则腕点沿关节弧线经过台面。过渡抬升沿平台局部法线运输，完整支撑和零权重的姿态不受该抬升影响。

## 验收边界

复用 `mantling_demo_smoke.tscn` 和脚本，不使用 computer-use。检查实际发布的手部位置与掌面朝向：完整支撑时锚点误差 ≤1 mm、旋转误差 ≤0.1°、可达残差 ≤1 mm；额外移动平台检查平台局部漂移 ≤2 mm，容纳观察阶段平台比发布姿态先移动一次的物理步。诊断朝向要携带 FBX 骨轴转换，不能将 actor 世界旋转直接当作最终骨骼旋转。

完整蒙皮／平台门禁仍为 2 cm。接触通过、角色落台或截图采集成功都不能覆盖蒙皮失败。实现和失败证据见[本批曲线／接触验证](../verification/2026-10-09-mantle-contact-progress-curves.md)。

默认完整 pipeline 对含生成 Mantle 的包，在通用 E2E 之后继续调用上述两份真实场景 runner；静态失败不会跳过平台／生命周期检查，两者失败均阻止 `runtime_passed`。身体运行成功不能替代该专用几何门禁。
