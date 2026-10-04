# 穿透恢复与蹲伏决策归 Core

本批将实际生产 `LyraCharacterSweep` 的安全移动/恢复顺序和 `LyraSceneMovementService.SetCrouching` 的净空/回退决策迁入纯 .NET Core。继续使用 ALS 人物、Pistol/Rifle、原 ALS 积分/动画、Core 地面/空中/floor 控制器和公共碰撞回拉。

## 生产路径和边界

`AlsCharacterSweep<THit,TCheckpoint>` 处理每次 Move 的 Recovered 清理、穿透调整、几何/角色及 proxy 限幅、膨胀位置 overlap、直接位置恢复、实际扫出、不同第二调整的合并、调整加请求运动及最后原运动回退。Core 根据实际位置变化判定恢复；nonpenetrating 命中或请求 fraction 本身不证明已经移动。五类恢复计数归同一物理角色，并保留原查询/写入顺序及成功后的原请求重查。

typed 命中扩展既有 `IAlsCharacterGroundHit`，仅增加 PenetrationDepth/Pawn；Godot 根据真实 Collider 类型提供 Pawn，opaque checkpoint 在 Godot 为原 Transform3D，保留 basis/scale。原角色为非 proxy，通用 Core 设置同时提供几何/角色的 proxy 限幅，四类由单测覆盖。

`AlsCharacterSweepMath` 继续提供上一批共享 PullBackFraction，并集中原 scalar Dot、逐分量除法 Normalize 和 capsule support 深度计算。忽略朝外 initial overlap 的方向/容差判定也由 Core 提供；Godot 保留实际 overlap 枚举、Rid 排除推进和 Jolt cast bracket 精化。没有更换为 Numerics SIMD Normalize/Dot、倒数乘法或新的碰撞时钟。

`AlsCharacterCrouch` 是无私有 stance 时钟的决策器，接收当前实际 capsule half height、stance 与 grounded。Core 处理可蹲门控、相同姿态无操作、地面缩高保底/空中保持中心、起身 epsilon、净空拒绝、地面 ray gap 降低回退、空中 sphere 起点 overlap/下扫、第二次净空以及最终 accepted half height/shift。地面 gap 保留原完整 ray distance，非仅取垂直分量。

空中回退通过 typed IDisposable query scope 保留同一 sphere/parameters/space 查询对象；起点 overlap 拒绝不做 cast，正常、拒绝和异常均释放作用域。Godot 适配器在构造失败时也释放已有 query/sphere。Core 只返回决策，Godot 在原物理入口依次写 Body.Position、capsule height、model offset 与 Crouching；动画取消重试不重复这些物理操作。Lyra 的 Emote 起身请求仍是领域输入，CDO JSON/schema/hash/单位转换留在资源适配层。

普通玩家/NPC、地面/空中与 RootMovement 的 sweep 默认调用新 Core；CrouchWorld 保留实际 shapecast/ray/sphere query，Core 不引用 Godot、Node、PhysicsServer、Rid、Collider 对象或 JSON。

## 验证

Core 116 项通过，新穿透恢复 21、蹲伏 19，原 floor 32/ground 19/air 23/原生速度下落 fixture 2 保持，0 失败/0 跳过。新测试覆盖五种恢复路径、opaque checkpoint、原请求重查、unsafe/零请求、相同或抵消的调整不多查、nonpenetrating 零 fraction 未实际移动不算恢复、四类限幅、最小深度/零限幅、朝内/外/零方向容差、上下 capsule support 与参数先拒绝；站蹲相同/禁用、地面底与空中中心、地面起身/净空回退及三种拒绝、完整 ray distance、空中第二净空、起点 overlap 不 cast、scope 异常释放和无 stance 私有历史亦覆盖。首次测试全通过，没有修改算法阈值或测试预期。

Debug 和 ExportRelease 构建各 0 警告/0 错误。两种构建各完成完整 Main＋Rig 7560 帧/7296 姿态、真实 Godot Rig 物理 2520 帧/2484 姿态、普通 ALS 1700 帧及十角色 480 帧/4800 蒙皮发布；十角色和 Rig 物理完整报告同前批。

三频实际 Jolt 角色移动各 6 角色/4 秒，每构建 5040 次移动及动画重试；站蹲起身阻挡/释放、jump midpoint/顶点/落地和墙面切向通过。三频台阶各 5 角色/2 秒，每构建 2100 次移动及重试，站蹲低台阶、过高/顶棚拒绝、回滚和关闭 StepUp metadata 通过。三频 floor 各 8 角色/0.6 秒，每构建 1008 次移动及重试，高度带、顶棚修正阻挡和 perch 拒绝通过。三组完整报告均同迁移前及两种构建。

每构建空中 112 个实际 query，20 次 apex split、28 次 landing、6 次多接触、0 mismatch。恢复计数与上一批完整报告相同：16 次 teleport、8 次 combined；这组实际 query 未覆盖 swept/adjusted/original 的非零计数，这三个分支由 Core 单测覆盖。原地面 32 个 query 保留 8 项 UE/Jolt 差异，floor 144 query 保留 3 项差异；四个诊断进程按原协议退出 1，未当作原生世界等价通过。完整诊断报告同前批及两种构建（floor 仅除 evidence tag），原行数/阈值保持。

普通地形三频每构建共 6300 次最终蒙皮发布，台阶/斜坡/落差/跳跃/站蹲/ADS 和两武器通过；三个完整报告同两种构建及上一批。

最终 `artifacts/lyra-analysis/recovery-crouch-core-v1-audit.json` 通过：34 个成功 Godot 进程和 4 个保留旧差异的预期诊断（共 38），9 份本批源码/4671 份保护基线（含 870 份 Lyra 资产 JSON）保持冻结哈希，前批证据保持。五轮 Optimize 验证后六份 Debug DLL/PDB 恢复。实现、测试、构建、运行与审计均为 v1。

没有 UE 修改/启动/重导、资产 JSON 格式化、提交推送、新 GPU、全量 managed、十分钟、性能或跨平台验收。实体键鼠没有重试；此前 computer-use `GetCursorPos 0x80070005` 拒绝没有恢复证据，自动逻辑输入不作为实体键鼠通过证明。

## 完整目标中的后续工作

地面、空中、floor、穿透恢复与蹲伏通用决策已归 Core。接下来按普通生产路径继续核对引擎通用机制的归属和 ALS 复用，核对当前功能/资源/两武器/Layer/运动核心验收，再完成实体键鼠验收。不能仅因本批回归通过即关闭整个目标。

URO、额外 Provider、完整 UE 调度/私有字段还原、物理世界逐位等价及既有暂缓项继续后移；本批只关闭上述恢复/蹲伏机制，完整目标保持开放。
