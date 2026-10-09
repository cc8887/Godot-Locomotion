# 用 Kimodo 生产完整 ALS 动画资源的设计与逐资产约束

本设计以 2026-10-07 当前主目录的 ALS 图、原动画评价策略、采样配置及本机 Kimodo 接口为依据。目标是制作一套风格一致、能在当前 Godot ALS 组合链中工作的**新资源**，保留 ALS 的资源语义与运行机制；不是继续把同名生成动作直接覆盖到原 FBX。

完整逐资产清单：[253 个动画引用身份](kimodo-als-asset-catalog.md)；完整机器可读规格：[kimodo-als-asset-contracts.json](kimodo-als-asset-contracts.json)。每项有作用、提示词草稿、生成约束、后处理、验收、原时序、精确加法参考及图引用。当前已实施[依赖图作者流程](../../tools/kimodo_pipeline/README.md)，完成部分真实候选与受控回读，尚未形成完整可运行新套件。JSON 含符号化 pose/socket 目标，不是可直接传给 `--constraints` 的文件；执行器在依赖完成后提取实际姿态并构造数值条件。

普通移动的数值约束、事件选周期、支撑曲线协议和 DAG 验收节点详见 [步态生成约束与分阶段验收设计](kimodo-als-locomotion-constraints-and-gates.md)。三前进试点已实现对应生成条件和门禁，实际候选尚未通过，详见 [闭环试验](../verification/2026-10-08-kimodo-gait-closure.md)。

## 1. ALS 资源是相互依赖的组合系统

```text
统一目标骨架 + 人物风格 + Stand/Crouch/Lean 固定基底
  ├─ gait master → Walk/Run/Sprint/方向/站蹲/低步幅参考
  │                └─ 支撑相位 → Jump/Stop/Transition/Turn
  ├─ 同一基底 → Idle/Lean/Acceleration/着地加法
  ├─ Overlay 状态姿态表 → 精确 NoCurves 副本 → 站蹲 Aim
  └─ 平台/恢复空间 → Mantle/Roll/GetUp
       ↓
target rig 后处理 → 加法语义/曲线/Sync/Notify/Montage/BlendSpace
       ↓
同一角色帧内的组合输出 → 最终蒙皮与普通 Demo 验收
```

ALS 通过速度/方向/步幅混合移动；通过相位同步拼接相关步态；通过 BasePose 求差、局部或网格空间加法叠加细节；通过 Layering 权重组合上身/手部/腿部；通过 Foot IK/Lock 适配实际地形。因此单个动作自然不代表组合结果自然。

此前 idle 诊断已经证明：SecondaryMotion 的首尾突变和 Overlay/Aim 基底不一致同时存在，hash/时序 PASS 不会检测这些语义错误。见 [诊断证据](../verification/2026-10-07-kimodo-idle-diagnosis.md)。

## 2. 本项目的完整清单边界

| 命名空间 | 当前快照 | 本方案用途 |
| --- | ---: | --- |
| `/Game/AdvancedLocomotionV4/...` | 126 AnimSequence 身份 | 原替换树、V4 Overlay/Aim、回归/兼容调用 |
| `/ALS/ALS/Animations/...` | 127 AnimSequence 身份 | Refactored 主移动、状态过渡、现有/后续 Overlay 资源 |
| 非 AnimSequence 身份 | 200 条目录记录 | 包括 Montage、BlendSpace、配置、曲线、rig/材质等；目录记录不是物理文件数 |

253 是**引用身份数**，不是必须独立生成 253 次。许多 V4/Refactored 资产同义，可以由同一 master 经各版本骨架、曲线名称和评价策略适配派生。另有 Refactored 新增站/蹲普通和动态 Transition，不能遗漏；V4 的蹲伏步枪资源也不能因另一版本没有同名就删掉。清单覆盖两套完整快照，未把每项都称为当前普通路径生产可达。

完整套件包含 Default、Feminine、Masculine、Injured、HandsTied，以及手枪单/双手、Rifle、Bow、Torch、Binoculars、Box、Barrel 的姿态/动作组合。Godot 当前路线优先级仍以 ROADMAP 为准，资源规格不等于这些功能都已正式验收。

普通入口当前从固定 `assets/config/refactored_*` 读取主移动和 Layering 基底，V4 raw override 则控制 Overlay/Aim 等域。制作新套件时必须给**两类 reader 同一个新资源版本**，保持角色级统一时钟/事务，不能只切 `ALS_ASSET_ROOT` 就宣称全资源替换完成。

## 3. Kimodo 能控制什么，必须另做什么

本机源码依据：`G:/Github/kimodo/kimodo/constraints.py`、`kimodo/scripts/generate.py` 和 `docs/source/user_guide/constraints.md`。官方说明见 [JSON 格式](https://research.nvidia.com/labs/sil/projects/kimodo/docs/user_guide/constraints.html)、[约束类型](https://research.nvidia.com/labs/sil/projects/kimodo/docs/key_concepts/constraints.html)、[最佳实践](https://research.nvidia.com/labs/sil/projects/kimodo/docs/key_concepts/limitations.html)。

| 控制 | 实际表达能力 | 本项目使用方式 |
| --- | --- | --- |
| text prompt | 动作/风格语义，随机候选 | `A person...`，一个或两个行为，适度细节；不在文字中堆全部关节要求 |
| duration / seed / CFG | 帧数与可复现候选/条件权重 | 固定设置比较，再选 seed；时长从动作事件确定，不直接用原 2/4-key 资源长度生成 |
| `root2d` | XZ 路径及可选 heading | 行进速度/方向、原地转身路径、固定 idle 根 |
| `fullbody` | sparse FK 关节位置目标 | 基础姿态、起止、离地/着地、爬上平台的里程碑 |
| `end-effector` / 四个手脚简写 | 末端目标，及相关 hips/朝向约束 | 支撑脚、握点、翻越触边等稀疏目标 |
| Kimodo postprocess | 约束/滑步优化 | SOMA 候选启用，然后仍在目标 rig 上做脚底/握点校正 |
| 接触条件输入 | 模型描述支持，但当前公开 UI/Python API 没有该条件接口 | 用末端稀疏目标+后处理+接触测量；不伪造 `contacts` constraint 类型 |
| ALS 层权重、FootLock、Sync、Notify、物品骨、精确手指 | Kimodo 不直接制作 | target rig 上确定性派生/编辑；再绑定 ALS 元数据 |

`fullbody.local_joints_rot` 是构造 FK 位置目标用的 axis-angle，**不是把每个局部骨旋转精确固定**的开关。因此武器手型、twist、骨盆朝向和加法零差要在目标骨架上检查/校正。

约束统一使用米、Y-up、XZ 地面；`first_heading_angle=0` 对应 +Z。`root_positions.Y` 是 hips 绝对高度，不能设成 0 充当地面 root。`global_root_heading` 每帧是 `[cos(theta),sin(theta)]`，不是度数/弧度标量。`local_joints_rot` 是 `[T,J,3]` axis-angle radians，不能填 UE quaternion 或 FBX Euler。SOMA77 的约束进入模型前可被转换到 SOMA30；模型不保证手指/目标 rig 的所有辅助骨。

除 dense root path 外，每类约束使用少于 20 个 sparse 时间点。每段 prompt 最大 10 秒；候选通常生成 2–4 秒再按动作/周期裁剪。CLI `duration*fps` 会转整数帧，实际生成末 frame 是 N-1，不得把时长边界当合法索引 N。`--bvh_standard_tpose` 只改 BVH rest basis，不会插入一个 T-pose 首帧。

## 4. 统一主合同

每个 master 固定：characterStyle、model/skeleton、seed/settings、rest-pose 版本、forward/up/unit、鞋底几何、手腕/grip socket、相位定义，以及它引用的 BasePose 哈希。Default 风格先定为自然、稳定、轻呼吸，不主动晃髋/耸肩；Feminine 等风格是显式分支，不与 Default 混用。

新 Stand/Crouch/Lean 基底可以通过 Kimodo 候选选择，然后做 rig 校正并冻结。它们不必沿用旧表演，但选定后所有依赖必须共享。骨架绑定参考与动画 StandBase 分开：更换动作不能偷偷改 skin bind/rest pose。

| 基底 | 作用 | 必须成立 |
| --- | --- | --- |
| `ALS_N_Pose` / `A_Als_Stand_Pose` | 固定站姿、idle/观察等加法参考 | 双脚/骨盆/肩对齐；重复静态键；所有依赖求差使用同一帧 |
| `ALS_CLF_Pose` / `A_Als_Crouch_Pose` | 固定蹲姿、站蹲混合基底 | 真正蹲伏，脚锚与 standing 相容，不能生成持枪站姿代替 |
| `ALS_N_Run_BasePose` / `A_Als_Lean_Pose` | 地面 Lean 的参考 | 来自新 run 风格的统一姿态；不同方向共享该参考 |
| `ALS_Mannequin_T_Pose` | rig/参考资产 | 从骨架确定性生成，不用随机动作生成器 |

## 5. 按作用定义生成合同

### 5.1 Idle / SecondaryMotion

`ALS_N_SecondaryMotion` / `A_Als_Idle` 是局部加法循环。提示词草稿：

> A person stands still with subtle breathing, keeping both feet planted and the arms relaxed.

root2d 固定 XZ/heading；用同一 StandBase 的 start/end fullbody，必要时加入稀疏双脚目标。生成后去掉恒定姿态偏置、解目标 rig 的足底接触、闭合位置/旋转及速度，再相对该 StandBase 求 local additive。不能给 BasePose 和 Idle 分别随机生成站姿后仅复制原 additive metadata。

真实循环要测原始/目标/final pose，不能仅强制最后一帧等于第一帧。最后一帧硬替换可能把原跳变移到前一帧。

### 5.2 Walk / Run / Sprint / Crouch 与方向资源

40 个基础循环引用（两套合计），方向包括前后和左右斜移、蹲伏及 V4 蹲伏步枪。提示词分别表达 gait/方向/stance，保持 torso facing forward；side/backward 不等于把整个身体旋转后前走。

当前设置的动画速度为 Walk=1.5、Run=3.5、Sprint=6.0、Crouch=1.5 m/s。作为新 master 的初始目标，root2d 定义 `pXZ(t)=v*t*direction`，随后按可实现步幅/节拍和项目速度配置校准。

| 后缀/方向 | Kimodo XZ 方向 | 共享要求 |
| --- | --- | --- |
| F/Forward | `[0,1]` | 同组左/右接触相位 |
| B/Backward | `[0,-1]` | 身体朝向保留前向 |
| LF/Left_Forward | `[sqrt(.5),sqrt(.5)]` | 与前/侧样本混合时脚相位一致 |
| RF/Right_Forward | `[-sqrt(.5),sqrt(.5)]` | 同上 |
| LB/Left_Backward | `[sqrt(.5),-sqrt(.5)]` | 与后/侧样本混合时脚相位一致 |
| RB/Right_Backward | `[-sqrt(.5),-sqrt(.5)]` | 同上 |
| L/R 的纯蹲伏横移 | `[1,0]` / `[-1,0]` | 源 foot anchors 与 target rig 重新验证 |

这里的左右按 SOMA 人物自身定义：本机原骨架零朝向时 LeftLeg.x≈+0.1004、RightLeg.x≈−0.1005，正 heading 为朝人物左侧旋转。不能套用相机画面的左右；target rig 转换后还需重新验证。

生成自然带位移动作，选取完整周期，统一 Left/Right 支撑顺序，再移除全局行进轨迹供物理角色移动。足底必须在世界/平台局部空间测量：不能给所有帧 foot 恒定 pin 与行进 root 同时施加互相矛盾的条件。

WalkPose/RunPose 是 BlendSpace 的参考/低步幅样本。**key count 不等于动作类型**：原 WalkPose_F/A_Als_Walk_Forward_Pose 虽有 35 keys，实测所有局部旋转静态。新资产应从对应 gait 风格选择参考姿态，按原 sampler 所需时域复制，不能生成“1.13 秒步行”替代。

Sprint_Impulse / Sprint_Acceleration 的末姿态要进入 sprint master 的同一相位；初始姿态/加速轨迹由当前 gait 决定。保留或调整 0.6 秒域必须同时重制事件/曲线，不能只时间压缩。

### 5.3 Lean / Acceleration

Lean 的五个样本是 neutral、forward、backward、left、right。V4 ground 参考 `ALS_N_Run_BasePose`，air 参考 `ALS_N_FallLoop`；Refactored ground 参考 `A_Als_Lean_Pose`。实际所有依赖都见 JSON 的 originalEvaluation。

候选提示词表达“保持跑步/悬空姿态并朝指定方向倾斜”；root 无行进，脚/非目标关节保持参考。neutral 应输出零修正，四方向成组共享尺度和基底。目标 bone mask 外的 residual 必须为 identity，避免 Lean 顺带重写肩/腿姿态。

Acceleration 四方向是加速度驱动的 body detail；根据原图/采样轴制作参数样本或动态修正，不能仅看到 Accel 就生成一段起跑。各项 originalTiming、sourceBindings 和 baseFrame 已逐项保存；必要的动态与静态求值在生产 job 中分开。

### 5.4 Overlay 姿态表

26 个姿态表身份、12 个 NoCurves 副本。每个表按原 graph 读取的精确帧索引制作，每帧可以由 Kimodo 候选+rig 编辑得到，但**打包表由工具完成**。

| 表 | keys | 角色 |
| --- | ---: | --- |
| Default/Normal、Feminine、Masculine、Injured、HandsTied、Box、Barrel | 4 | Refactored frame0 站立非 walking，frame1 walking/空中分支，frame2 crouching；第4键及各版本时间/曲线保留，完整引用见附录 |
| Binoculars / Torch | 7 | 持物的地面/空中/瞄准站蹲姿态；站 Aim base=3，蹲 Aim base=6 |
| PistolOneHanded | 10 | Relaxed/Ready/Aiming 与站蹲分支；站 Aim base=5，蹲 Aim base=9 |
| PistolTwoHanded | 11 | 双手 grip 的状态表；站 Aim base=6，蹲 Aim base=10 |
| Bow | 12 | Relaxed/Ready/Aiming、draw 相关状态；站 Aim base=7，蹲 Aim base=11 |
| Rifle/M4A1 | 13 | 状态/站蹲/跑冲刺/空中组合；站 Aim base=5，蹲 Aim base=9 |

keys 数来自原资源，不表示“4/7/13 秒动作”。图有固定帧，也有分数秒采样，不能只填几帧然后无视其余。完整清单末尾列出各表 fixed frame consumers 和 additive dependents；JSON 保存具体 source graph/node。

例如 Refactored Rifle 中 Ready 读取 frame2/3/7，Aiming 读取 frame4/5/8，Relaxed 读取 frame0/1/6/9/10；外围还读 frame11，而 Aim_Crouch 的参考是 NoCurves frame9。它们是**多种图上下文**，frame9 不应只解释成一个状态就自由重做。

`Poses_NoCurves` 必须由同一表删除曲线派生，骨姿态逐帧一致。保持 Step/原分数时间策略；不要让整段“拿枪随意站立”压缩成这个表。手枪/步枪/弓/望远镜的实际 grip/sight/弦 target 来自道具尺寸/socket，不能用提示词代替数值目标。

### 5.5 两种 Aim 参数资源

**基础 Look 三行**：`ALS_N_Look_F/D/U_Sweep` / `A_Als_Look_Forward/Down/Up`。固定 pitch 行分别 0/-90/+90，time u 表示 yaw `-180+360*u` 的图接口。需要中性参考、左右观察的参数表，根不转动；F 的前视中心与 StandBase 零差。D/U 不是“先平视，然后向下/向上看”的时间动作。解剖范围和躯干分配按目标 rig 求解，不把参数极端值当成单颈骨必须转 180°。

**持物 Aim**：站/蹲分别制作，time u 表示 pitch：u=0 为 +90，u=.5 为 0，u=1 为 -90 的接口。fullbody 和 hand/prop end-effector 明确方向目标；中性参数与自己的 `Poses_NoCurves[baseFrame]` 一致，转换为 mesh-space additive。参数序列可以用稀疏 Kimodo 候选插值/rig 解算组装，时间不需要代表真实瞄准速度。

本机输入映射证据：`AlsAimingInputModel` 的 AimSweepTime/ForwardYawTime；资源匹配证据：`AlsAimSamplingCompiler`、`AlsRefactoredWeaponEvaluators`、`AlsRefactoredPropOverlayCompiler`。

### 5.6 Rifle 全身与 Arms 配对

Run、Sprint、Sprint Impulse 的整身/Arms 版本必须从同一次 master 派生，保留同样长度/相位。Arms 是 Layering 消费的资源，不是另一个“只摆手臂”随机动作。手部需维持同一 grip，腿部/骨盆影响按 graph mask 保持，通用 gait 与 weapon source 同组同步。

### 5.7 Jump / Fall / Land / Flail

走/跑起跳的左右变体按 gait 的起跳相位制作，左/右标签表示相位身份；实际支撑/蹬地脚以原参考/新 master 接触记录确认，不仅凭名称猜。起跳、顶点、空中循环、接触/缓冲、回到 base 的 sparse fullbody anchors 必须相接。

Fall/FallFast/JumpLoop 是空中姿态循环。可以从完整带高度运动的候选裁剪，但全局上升/坠落位移要归运行时物理，不能再次从 animation hips 作用到显示网格。空中不使用地面 foot lock。

Light/Heavy Land 与其 Additive 版本采用同源表演、同一接触时刻，按**各自 exact basePoseType/baseFrame**求差；不得四次独立生成，再靠名字配对。Flail 是空中/物理混合资源，不能用普通站立晃动或地面锁脚替代。

### 5.8 Turn / Rotate / Stop / Transition

Turn 90/180 左右、站/蹲成组；root2d 固定位置并约束 heading，支撑脚用 sparse end-effector。生成后分离 gameplay yaw 与 pose residual，并按累计角度/接触制作 RotationAmount/相关曲线。原启用 Root Motion 的资产列表不等于所有转身都开 Root Motion，禁止由名字推导开关。

Rotate 是持续旋转时的踏步模式，去累计朝向后的局部 pose 应闭合；不要用一个普通 turn 片段任意 loop。

Stop L/R Down 是支撑相位相关停止；Transition 与 DynamicTransition 用于静止脚距/脚锚修正及对应 Slot 调用。Refactored 区分站/蹲普通/动态左右，V4 清单没有完整同名对应。支撑脚保留原锚、移动脚到目标，初始 gait phase 和结束 base 明确；部分仍使用 StandBase 作为 mesh-space additive 的参考，不能因为动作看起来蹲伏就自行改成 CrouchBase。

StandToCrouch/CrouchToStand 则用 shared fixed poses 做 start/end，保持足底锚、骨盆和胶囊高度的匹配，独立制作站蹲过渡。

### 5.9 Mantle / Roll / GetUp

低翻越左/右手和高翻越需要平台几何数值目标：approach、手触边、骨盆越边、脚登顶、末站姿。root2d 仅控水平，fullbody.root_positions.Y 负责竖直目标，ledge hands 用 end-effector。高度“1m/2m”只是旧资产身份，新候选必须适配实际 Mantle settings、target-relative motion source/start offsets/correction curves。

Roll 约束入滚/肩背接触/出滚，重制碰撞与通知窗口；actor root 只消费一次。GetUp Back/Front 分别从仰卧/俯卧恢复到规定站蹲姿态，起始姿态要和 ragdoll recovery snapshot 相容，支撑手脚也需约束。不能用通用“站起来”替代实际恢复位置/朝向合同。

`Bow_Draw` 是独立弓骨架的参数变形，来自人体 draw/握点/道具 rig，不是人体 Kimodo 输出。

## 6. 可直接使用的 root2d 示例与必须补齐的数值

3 秒/30 FPS 前走候选，root 路径目标速度 1.5 m/s，合法末索引89：

```json
[
  {
    "type": "root2d",
    "frame_indices": [0, 30, 60, 89],
    "smooth_root_2d": [[0, 0], [0, 1.5], [0, 3], [0, 4.45]],
    "global_root_heading": [[1, 0], [1, 0], [1, 0], [1, 0]]
  }
]
```

提示词：`A person walks forward at a steady pace, keeping the torso facing forward.` 本例只规定路径，不保证周期相位/足底接触；生产 job 要补 gait master 的 sparse 姿态/支撑目标。

fullbody/手脚约束不能提供假矩阵：必须先有可用的 SOMA 30/77 rest basis 下的 `[T,J,3]` axis-angle 和 `[T,3]` root_positions。来源可以是批准的新 pose master，或从目标 rig 姿态经已验证的反向适配取得；不能直接塞 UE 的 q/Euler。每个 grip/ledge 也要先转换到同一 canonical frame。这是把 JSON 规格落成实际 `constraints.json` 的前置工作，当前尚未制作。

## 7. 工具链必须完成的制作步骤

1. **创建 master/reference pack**：统一 skeleton、BasePose、各 grip、平台规范与 gait 相位；保留完整 NPZ、root trajectory、contacts、设置/seed/hash。
2. **按作用生成候选**：离散表逐状态提取；连续循环按 master 生成并裁切；对应 Arms/NoCurves/Additive 派生，不重复抽样。
3. **在目标 rig 修整**：rest-pose 对齐、骨长、twist、手指/道具握点、足底接触、膝极向与 root 分解。Kimodo/SOMA 侧 postprocess 不代替这一阶段。
4. **制作 ALS 元数据**：常量 layer mask 可按同状态继承；FootLock/EnableFootIK/RotationAmount/Gait/Mantle correction、Left/Right markers、footstep/action notifies 按新运动重制。改变动作时长时同步改 Montage sections/segments/blend windows 和采样配置。
5. **导出和原子版本绑定**：V4 传输树、raw bank、Refactored catalog、嵌入的 base pose inputs，以及图/设置 digest 指向同一套语义资源；不得只更换 FBX 或一个 source directory。保持现有角色事务，所有骨/曲线/道具来自同一 committed frame。
6. **逐阶段验收**：NPZ → BVH → 第一段 target rig → ALS rig → raw → 组合 final pose；分别报告 drift、足底、方向、相位、加法偏差，保留失败日志。旧原生数学对照阈值保持；新美术资源测量是另外的质量门，不用新表演与旧表演逐骨必须相等作为风格门槛。

## 8. 制作顺序与验收门

| 顺序 | 资产组 | 进入下一组之前 |
| --- | --- | --- |
| 1 | Stand/Crouch/Lean base、Default 状态表、Idle、Look | idle 无恒定髋肩扭偏/循环跳变，站蹲和中性 Aim 零差成立 |
| 2 | 前向 Walk/Run/Sprint + pose samples/Accel/Lean | 同相位混合、stride/速度匹配、足底接触稳定 |
| 3 | 六方向、蹲伏、起停/脚修正/Turn/Rotate | 左右对称与换向/停止相位组合可用 |
| 4 | 单/双手 Pistol、Rifle pose/Aim/cycle/Arms | 握点、瞄准、站蹲/跑冲刺组合无相位/基底冲突 |
| 5 | Jump/Fall/Land/Roll/Mantle/GetUp/Flail | 接触、物理移动与 root 唯一消费、动作前后接续可用 |
| 6 | 其他 Overlay/props 全矩阵 | 精确 frame 表、加法引用、道具锚和转换稳定 |
| 7 | 普通 Demo / single+parallel / 多角色与地形 | 按 ROADMAP 当前门禁执行；视觉/长时性能另签收 |

每个质量门至少同时测：source/retarget/最终蒙皮接触漂移与穿透、循环 P/Q/导数、graph 参数中性/端点、同相位混合、actor 与 mesh 的 root 重复消费、同一帧装备挂点一致性。阈值应在 master/目标 rig 尺寸明确后制定并冻结；此设计没有放宽既有 native 对照，也没有把约束文字当成已通过验收。

## 非道具发布契约

非道具范围使用 `non_prop_rules.json` 与 `--scope non-prop`：183 个人体动画身份替换，70 个武器/道具相关身份保留。风格 Overlay 仍属于替换范围。Default/Normal/Feminine/Masculine/Injured/HandsTied 的 frame3 为 frame2 的末端填充，不增加第四种状态。

风格生成只提供一个上身 master；程序按 Spine1 子树传递相对共同 Stand 的局部旋转残差到 Stand/Walk/Crouch，保持各基底根、骨盆和腿。Mirror、NoCurves、Arm mask 和 Additive 引用沿用 DAG 的同源派生。

`retarget_kimodo_resource_pack.py` 写版本化 UE 子树及逐任务 BVH/FBX/raw 回执。`publish.py` 验证原生回执和目标 physical bind、制作目标 root/IK/接触/转向/事件、同步嵌入姿态和生产输入哈希；旧 native reference 不随新包改写。保持原时长和采样接口，曲线常量继承对应状态含义。接触初值是最终目标脚部高度包络，精细接触与美术接续继续要求独立验收。

版本包包含 V4、raw 与 Refactored 配置，`AlsAnimationResourcePack` 在现有 reader 上统一选择，验证文件字节后进入原角色时钟和事务。stage 不改变默认入口；实际运行检查通过后 activate 原子更新默认包指针。完整流程见 [执行脚本](../../tools/kimodo_pipeline/README.md)。

`publish stage` 必须在最终目标 root/IK 与原时序确定后，对每个含根运动或合同声明根运动的资产烘焙独立 `motion_curves/<edition>/<OriginalAssetName>.root-motion.json`，登记包索引及文件哈希。普通 ALS 默认关闭根运动采样，离线曲线仍保留完整轨迹；开关与距离/旋转曲线语义见 [根运动策略](als-root-motion-policy.md)。这一步属于每次生产的发布流程，不依赖事后人工提取。

## 9. 本地依据与复核方法

- `assets/generated/als_v4/als_manifest.json`：126 序列及 V4 Montage/BlendSpace/AimOffset/曲线/事件信息。
- `assets/config/refactored_animation_sources.json` 与引用 payload：127 序列、图节点/固定帧、逐序列 raw/evaluation/curves；nativeText 中的额外 metadata 仍需后续 production job 解析。
- `AlsRefactoredDefaultOverlayCompiler` / `AlsRefactoredDefaultOverlay`：frame0/1/2 状态混合与 Idle additive。
- `AlsRefactoredWeaponEvaluators` / `AlsRefactoredPropOverlayCompiler`：各姿态索引与 PitchAmount 采样。
- `AlsAimSamplingCompiler` / `AlsLeanSamplingCompiler` / `AlsAimingInputModel`：加法类型、方向参数、精确参考与输入映射。
- `AlsRefactoredMovementSettings` 和原配置：150/350/600/150 cm/s 的动画速度合同。

逐项 evidence hash 保存在 JSON；生成器只读这些来源并写分析文档，不改动画资产。复核命令：`python tools/unreal/build_als_kimodo_asset_contracts.py`。本批的覆盖/失败/限制记录见 [规格分析验证](../verification/2026-10-07-kimodo-generation-contract-analysis.md)。
