# Lyra 武器通知原行为与独立资源

本批关闭武器通知的原行为调查、三套独立武器资源导出和 Godot 模型发布组件验证。普通 Lyra 角色仍使用 ALS 的 68 根蒙皮骨与 81 根逻辑骨；武器拥有独立动画骨架和未来的独立 Montage bank。**武器消费者尚未接入普通角色，本批不关闭整个迁移目标。**

## 原通知实际行为

新可选模块 `tools/unreal/LyraWeaponNotifyOracle` 在实际 UE 5.8 / GASP58 中读取原蓝图引脚，并对原九个 `AN_PlayWeaponMontage` 对象执行 `Received_Notify`。真实 Pawn、原 EquipmentManager、原 ShooterCore WID / WeaponInstance / SpawnedActor 与实际武器 AnimInstance 均参与调用，没有替代通知类或复制蓝图算法。

四种场景分别为一件装备、没有装备、两件装备和首武器没有 AnimInstance，共 36 次调用。两个独立 UE 进程均正常退出 0，第二次读取与已保存 JSON 结构完全相同。通知选择第一件 WeaponInstance 的第一个 Actor 的 SkeletalMesh；两件装备时第二件没有播放。原函数始终返回 false。手枪两条换弹通知的 RateScale 为 1.5，其余为 1。

**原 `MontageSync_Follow.MontageFollower` 输入没有连线，没有默认对象。** 36 次实际调用的同步 leader 均为空；本机 `UAnimInstance::MontageSync_Follow` 对空 follower 立即返回。因此原资源只播放独立武器 Montage，移植不得额外实现角色与武器的同步跟随。此前依据 DSL 节点名称推测存在跟随的说法在此纠正。

原装备定义在 `/ShooterCore/Weapons/{Pistol,Rifle,Shotgun}/WID_*`，挂点均为人物 `weapon_r`，相对 yaw 约 -89.999988 度。载荷和原精度变换保存在 `weapon_notify_v1_policy.json`。这里尚未执行 Godot 普通角色的实际武器挂接。

## 导出与模型身份

`assets/generated/lyra_als/weapon_resources` 保存六个原武器 Montage 的元数据、六个 Sequence 的原始 DataModel keys / Skeleton / root-lock 设置 / 五个原生采样点、三份原网格 FBX 与十二张原纹理 PNG。武器动画不重定向到 ALS，DefaultSlot 也不混入人物五个 Slot。六个 Montage 均单轨、单段，没有通知或 root motion。武器 AnimBP 的历史编辑图是 LocalRefPose → Slot，当前原类读取亦为三个节点、没有 Sequence source；完整武器图连续原生姿态对照仍待。

手枪动画骨架七根；步枪与霰弹枪动画骨架八根，但原网格导出的物理骨只有七根，缺少未参与蒙皮的 Slide。FBX 导入还会重排同级骨。`LyraWeaponModelBinding` 从已校验哈希的原 FBX 读取物理骨集合，按骨名绑定 Godot skin，并严格校验父骨。采样保留完整七/八根逻辑骨，只发布物理骨；没有更改原网格 bind matrices 或 ALS 骨架。

`LyraWeaponResources` 验证 JSON 依赖、六个 clip、三个 FBX 和十二张 PNG 的字节哈希。每个 sampler、模型、候选帧和发布历史独立。取消不发布，晚期失效、跨模型候选和重复发布拒绝。`LyraWeaponModelBinding` 是可复用组件，但尚未连接角色装备生命周期。

## 实际验证

- Debug 与实际 `ExportRelease -p:Optimize=true` 最终构建均 0 警告 / 0 错误。`Optimize` 配置名称不作为优化构建的依据。
- 两配置各比较六段 clip 的 30 个时间点 / 230 个原生骨骼姿态，位置、四元数和缩放分量差均为 0；这是原始采样对照，不是武器 AnimBP / Montage 连续整图对照。
- 两配置各三个真实导入模型、180 帧 / 540 次发布、540 次取消重试 / 1,620 次非法发布拒绝，Godot 退出 0、无错误或警告。记录实际加载的三个 DLL 哈希，六个 Debug DLL/PDB 逐文件哈希恢复通过。
- Debug OpenGL 实际渲染 180 帧，保存帧 1 / 60 / 120 三张截图并检查帧 60。预览使用原 diffuse / normal 纹理；FBX 的顶点颜色属于 UE 材质掩码，不能直接当 albedo。没有声称原 UE 材质等价。
- 最终程序集普通 Lyra 十角色 60Hz / 480 步共 4,800 次人物模型发布，玩家六次换层、九 NPC 均 480 发布通过，无 Godot 错误或警告。该回归没有新增武器动作覆盖；报告明确 `weaponNotifyConsumer=false`、`nativeWholeMainParity=false`。
- Notify 探针保护 827 个已有 JSON / 691 个原包，资源探针保护 829 个已有 JSON / 706 个原包；十二张纹理另作包哈希检查。项目描述与项目 Config 保持原哈希，未保存原 UE 资产。临时可选插件已移回 `artifacts/unreal/lyra-weapon-notify-oracle/package`。本批新增十个 ignored JSON，不能仅交付代码检出。

最终汇总为 `artifacts/lyra-analysis/weapon-resources-verification.json`，由 `python tools/verify_lyra_weapon_resources.py` 实际校验生成。驱动入口为 `scripts/build-lyra-weapon-notify-oracle.ps1`、`scripts/export-lyra-weapon-notify.ps1` 与 `scripts/verify-lyra-weapon-resources.ps1`。已有日志、备份和依赖 JSON 不覆盖。

## 保留的失败与边界

首轮 DataModel 未选择时旧 reader 返回空轨道，六份临时数据移至 `artifacts/lyra-analysis/weapon-resources-first-failure`。两个 commandlet FBX 尝试触发 MeshObject 断言，退出 3；正常 RHI 的 commandlet 仍不能提供网格烘焙所需的完整 Editor 渲染对象。完整 Editor 中 raw-provider helper 明确拒绝使用，故导出拆为完整 Editor 的 FBX 和 commandlet 的原始 DataModel；最终三个 FBX、两次独立资源读取均正常退出。

首次 Godot 导入缺少 FBX 引用的 normal PNG，有错误/警告，不能算通过；十二张原纹理导出后最终导入无诊断。早期四元数索引和 FileAccess 命名歧义编译错误、错误假定 skin 与 Skeleton 数量/索引一致、旧 ExportRelease DLL 误加载，以及 PowerShell 5 编码混合造成的驱动失败均保留日志。最终优化程序集实际包含新脚本，最终驱动统一 UTF-8，原生与 Godot 骨骼误差门槛没有放宽。

下一步：原武器 RefPose/DefaultSlot 连续原生图与真实 Montage 生命周期 → 独立武器 bank → 原 EquipmentManager 首项选择的 typed 消费者 → 按角色已提交 callback 顺序接入 → `weapon_r` 最终姿态挂接 → 普通角色换装/销毁/多角色/实际动作和渲染。没有加入原资产不存在的 Montage follow。完整 NotifyState dispatch、MotionWarping、root-motion 生产碰撞、整个 Main 连续原生验收、复杂地形和性能仍开放；音频/道具物理暂缓保留。
