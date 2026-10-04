# 原武器 AnimBP 连续对照与独立 Montage bank

2026-10-02，直接在 `.` 推进。人物继续使用 ALS 68 skin / 81 logical；三种枪械拥有自己的七/八骨逻辑布局和七骨网格。本批补齐原武器姿态图及物理 Montage 的连续执行依据，并实现可接角色的独立 bank。完整 Lyra 目标仍进行中。

## 原生依据和资源修正

新的可选 `LyraWeaponMontageOracle` 使用原 `ABP_Weap_Pistol/Rifle/Shotgun`、原网格和六个原 Montage。真实临时 SkeletalMeshComponent 创建原 AnimInstance，读取实际编译 Root/Slot/RefPose 节点及链接，执行原根节点 Initialize/CacheBones/Update/Evaluate 和原 Montage 权重、推进与代理冻结。没有替换输入节点、复制 Slot 混合算法或保存资产。

三个实际图均为本地 RefPose → DefaultSlot → Root，源姿态不强制更新，RootMotionMode 为 RootMotionFromMontagesOnly。取样明确使用原 DataModel/RAW；输出采用网格实际七骨 RequiredBones，不能把 Skeleton 的全部骨直接当物理网格。步枪和霰弹枪的 Skeleton 有八骨，网格没有 Slide。

连续采集发现 Mesh RefPose 与 Skeleton RefPose 有实际差异：手枪 Slide/Trigger/Magazine/Barrel，步枪 Trigger/Magazine/Barrel，霰弹枪 Magazine/Barrel 的变换不同；手枪 Door 还存在极小四元数分量差。这直接改变待机、淡出基础及没有动画轨迹的骨。新增独立合同保留实际 Mesh RefPose，运行 catalog 按名称和父骨映射覆盖完整逻辑参考；原默认 Skeleton 采样接口继续用于既有源资源对照。

原 AN_PlayWeaponMontage 的首装备/首 Actor 选择、手枪 Reload 1.5 倍速以及未连接 MontageFollower 引脚的依据保持上一批原对象采集，详见 [资源验证](2026-10-02-lyra-weapon-resources.md)。本批没有加入 follow。

## 连续轨迹与执行范围

三种武器 ×30/60/120Hz，每组14秒，共9轨迹8,820帧。覆盖六个资源自然播放/结束、相同资产重播、Fire/Reload 中断、stopGroup=false 重叠、超额 Slot 权重归一化、非零起播位置、反向播放、0.75/1.5/2倍率、显式停止/缩短淡出/零淡出、零delta和稀疏求值。

每帧保留原代理冻结的资源顺序、位置、当前/上一delta记录、完整 Blend alpha/range/startAlpha/option/weight、Slot/Source/Total权重。7,560帧实际求值，共52,920骨；没有曲线或动画属性。命令在本帧代理冻结及根求值之后执行，下一次tick生效。**这验证指定命令阶段的武器图，不证明角色 Notify 与武器组件的真实 tick 前后关系。** 普通角色最终挂接及动作消费仍需按原组件依赖核对。

两个独立 UE 命令进程均退出0、成功标记唯一、结构内容一致；837旧JSON、706原资产包和项目描述/Config SHA保持。可选插件临时在 GASP58/Plugins 构建，退出后移回 artifacts，未更改宿主模块 manifest 或项目描述。原普通启动的历史 Condition/插件诊断不在本批修复范围。

## Godot 实现

`LyraWeaponMontageCatalog` 校验实际原图、网格父序、参考姿态、单段 DefaultSlot/DefaultGroup 和六个资源合同。`LyraWeaponMontageBank` 每武器独立持有现有 `AlsMontageRuntime`，复用物理实例/组仲裁/冻结/Slot姿态合成；没有第二份手写播放时钟，不进入人物五Slot bank或ALS81布局。

Prepare 创建带角色/武器代际及不可恢复 preparation serial 的候选。播放/停止命令只改变下一帧物理历史，不改变已冻结姿态；Validate/Commit/Cancel/Dispose 拒绝外国角色、错误代际、跨bank、重复准备、旧候选、重复提交和退役实例。只更新帧仍推进必要时钟并接受命令，禁止发布空姿态。

原生重叠权重1和0.75、Total=1.75暴露单精度舍入差异：新原武器路径对应逐项除法，已有 ALS 优化原生 fixture 对应共享倒数乘法。全局替换会破坏旧ALS严格对照，因此 Core 新增显式 `AlsMontageWeightNormalization`：默认保留旧路径，武器宿主指定 IndividualDivision。独立期望 `1.0000000298023224` 来自原生变换和 float 权重，不调整误差门槛。两条路径都保留独立原生门禁，不能据此宣称所有 UE 编译环境计算方式相同。

运行模型验证使用三个真实导入FBX及原发布器，每物理帧先取消再重新求值，预校验 bank 和 skin 后提交/发布。材质仅沿用上一批导出的 diffuse/normal 预览；原 UE 材质等价仍开放。

## 最终验证

最终证据由 `tools/verify_lyra_weapon_montage.py` 汇总到 `artifacts/lyra-analysis/weapon-montage-verification.json`。

| 门禁 | 结果 |
| --- | --- |
| Debug及实际ExportRelease Optimize构建 | 均0警告/0错误，明确 `-p:Optimize=true` |
| 武器原图/构建 | 各8,820帧、7,560姿态、52,920骨；P/Q/S最大差均0 |
| 状态和取消/构建 | 8,820逐帧retry、79,389拒绝、1,260 update-only；385反向记录、36零delta记录、393超额权重、3,995参考姿态帧、4,350无源权重帧 |
| 三网格/构建 | 180物理帧/540发布/540retry/1,620拒绝 |
| 旧资源/构建 | 六clip/30原生采样/230骨0差，原三网格发布门禁保持 |
| Core Release | 11通过0失败0跳过，包含旧ALS两种独立native口径和新除法回归 |
| 普通入口/构建 | 十角色60Hz各480帧，共4,800人物发布，六换类/六同类复用；两构建报告逐值同 |
| 实际GPU | 三次FramePostDraw截图，完整原图/模型门禁同时执行 |
| 资源保护及加载 | 837旧JSON/706原包/项目Config保持；记录实际加载三个DLL SHA，Optimize后六Debug DLL/PDB逐项SHA恢复 |

本批原图误差门槛仍为P1e-8cm/Q1e-10/S1e-12，状态和权重逐位比较；没有放宽、跳过或用fixture姿态驱动运行求值。

## 保留的失败和下一步

首次可选模块构建因静态Update名称隐藏、TObjectPtr转换和lambda返回类型问题失败，失败包/日志保留；修正后实际构建成功。首轮Godot连续对照在重叠时出现5.16e-8尺度差，按原生舍入修正。两轮全局/部分除法替换使旧ALS两项native严格失败，失败TRX与日志均保留；最终改为宿主显式策略，新武器0差、旧ALS门槛不变。

下一步将原 AN_PlayWeaponMontage 作为 typed 消费者接角色已提交 callback 顺序，按原首装备/首 Actor 寻找活跃独立 bank，再核对原组件tick依赖、最终 weapon_r 变换及原WID挂接变换；覆盖普通装备切换、销毁、取消、多角色和实际Fire/Reload动作。

**本批未接普通人物的武器消费者或最终挂接，不是武器装备玩法验收。** 整个Main连续native、完整NotifyState dispatch、MotionWarping、RootMotion生产碰撞、Shotgun完整Main、原材质/复杂地形/性能仍开放；音频、道具物理和头颈专项暂缓保持。没有提交或推送，用户已有修改保留。
