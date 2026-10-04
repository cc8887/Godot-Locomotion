# 输入方向与接触速度归 Core，及当前目标核对

本批将普通玩家/NPC 的 consumed direction 限幅、local/world 旋转与速度比例处理，以及 RootMotor 备用物理路径的完整有序接触速度投影迁入纯 .NET Core。继续使用 ALS Mannequin、手枪/步枪和既有 Core 运动、动画与 Layer 机制。

## 实现与复用

`AlsCharacterInput.Consume` 接收宿主映射好的输入向量、up axis、radian yaw、world/local 标志与速度比例，按原顺序限制长度、旋转并缩放；identity scale 不新增运算，保留 signed zero。`.5` Walk 选择仍属于 Lyra 输入配置。Godot 负责真实输入事件、坐标/单位映射与 Actor 写入。

`AlsCharacterContactVelocity.Resolve` 经 typed `IAlsCharacterContactNormals` 按实际后端接触次序读取 normal，仅在 velocity 指向接触内部时投影，最后按 grounded 清除负向 Y。RootMotor 的适配器保留原 `GetSlideCollisionCount/GetNormal` 时序，Core 没有新建碰撞集合、排序、物理时钟或重复移动。物理 tick/space/actor/component 真实性、Root capsule 的直立限制与实际写入仍属于 Godot 后端；Root 转换与速度使用已有 `AlsAnimationRootMotionConversion`。

扩展原 `AlsCharacterSweepMath` 的 scalar plane projection、逐分量除法 limit length 和 axis-angle float matrix；Core GroundMovement 的 Dot/Normalize/Slide 也复用此入口。输入边界的 binary32/radians 运算与原 ALS/UE double pose/rotator 运算具有不同数据合同，保持独立，不把它们改成一套新舍入方式。

本机 `../godot/modules/mono/glue/GodotSharp/GodotSharp/Core/{Vector3,Basis,MathfEx}.cs` 用于定位原运算次序，并保存源码哈希。实际 4.7.2 加载程序集的 Godot operators 才是运行对照；没有仅凭该源码检出认定版本等价。

另直接读取本次 ExportRelease `Als.Core.dll` 的程序集引用表：只有13个System程序集，Godot/UE程序集引用为0，记录于`contact-input-core-v1-core-reference-check.json`；同时核对Core项目及源码依赖。它证明本次Core二进制的程序集依赖，未把源码grep作为唯一依据。

## 运行验证

Core 166 项首轮全部通过：新 Input/Contact24，原 Motion26/Sweep21/Crouch19/Floor32/Ground19/Air23/原生 Velocity/Falling fixture2 保持，0 失败/0 跳过。新覆盖 world/local/其它 up axis、长短输入、速度比例与 signed zero、参数拒绝、有序 oblique contacts、只投影朝内速度、grounded clamp 先后及空中/上行速度保留。

Debug 和 `ExportRelease -p:Optimize=true` 构建各 0 警告/0 错误。两种构建各完成实际加载 Godot operators 的 4096 组输入、512 组 arbitrary-axis 旋转与 1024 组有序接触结果，三分量 float bits 全同；每构建 16896 次分量 bit 比较，0 mismatch。

三频真实 Jolt Root 物理每频 6 角色/8 秒，每构建共 10080 移动/pre-retry/animation-retry，包含实际 Montage Root interval、cancel、rebind、stop、反向播放、迟到/外国实例拒绝，物理移动不会被动画重试重复执行。每频额外五个受控非零 Root motion 验证 open/wall/slide/falling/rotation，本批显式提供最终速度，实际 wall normals 的投影后速度不再朝内。这些非零运动是受控夹具；原动作的 `originalNonzero=false` 保持，不称原资源任意非零轨迹验收。

两构建各一次普通 Emote 逻辑输入，480 帧/480 动画 retry/1 实际 Root capsule Move，activation/end/movement clear 各1，完整报告同前次 startup v5 及两种构建。自动逻辑输入不代表实体 E 键通过。

各构建完整 Main＋Rig7560帧/7296姿态、真实 Rig2520帧/2484姿态、普通 ALS1700帧、十角色480帧/4800蒙皮发布通过，十角色与 Rig 物理完整报告同前批。三频空中5040/台阶2100/floor1008移动及同数动画 retry，普通地形6300最终蒙皮发布/构建，完整矩阵报告同前批及两构建。

每构建实际空中112query/20apex split/28landing/6多接触、0mismatch。原地面32query仍有8项、floor144query仍有3项 UE/Jolt 差异；四个诊断按原协议退出1，完整报告与门槛保持（floor除evidence tag），不作为世界等价通过。

`contact-input-core-v1-audit.json` 验证44成功Godot进程和4旧差异诊断，共48；11本批源/4680保护基线（870Lyra资产JSON）与前批证据保持，六轮Optimize后六份Debug DLL/PDB恢复。实现、测试、构建、运行、审计均v1，没有算法或阈值修订。

## 当前目标逐项核对

| 用户要求/当前验收 | 生产实现与本轮证据 | 范围 |
| --- | --- | --- |
| Lyra运动思路与核心算法 | Core DistanceMatching/GroundMovementPrediction/OrientationWarping/StrideWarping、源采样/Sync/惯性/IK；普通Main与三频实际移动/地形 | 起停/Pivot、站蹲/方向、jump/landing与指定地形；不要求完整UE字段或物理逐位等价 |
| 引擎通用机制归Core | 纯Microsoft.NET.Sdk的Core；Layer合同/绑定/执行，pose/曲线/属性、Rig、Root转换，ground/air/floor/sweep/crouch/motion/input/contact控制 | Godot保留实际对象/查询/写入/输入坐标与显示，Lyra保留原图和资源配置；本轮不复刻全UE引擎 |
| 复用既有ALS | 原ALS Velocity/Falling、公共输入量、pose/additive/Root区间、Sync/BlendSpace/IK/Alpha与缓存惯性；普通ALS1700帧和共享地面数学回归 | 直接共用现有内核，未另写Lyra速度或下落积分 |
| ALS人物与骨架 | `LyraAlsCharacterBinding`加载原Compiled ALS Mannequin、校验68 skin/父骨并映射Lyra81；本轮额外核对原FBX导出清单SHA和编译载荷 | 原ALS编译表为79 logical/68 physical；Lyra81表独立，不能混称为相同逻辑库存 |
| 两把武器与Layer | Pistol/Rifle独立模型和动作，14 typed接口及Core绑定执行；本轮十角色真实换装/动作与完整Main/Rig | Unarmed是回退；其它Provider不作为当前门槛 |
| 普通Demo | 两构建/三频/原ALS/十角色/Emote及指定地形矩阵；此前九张指定GPU样本已检查，当前完整姿态/地形报告同前批 | 本批无新GPU、连续任意地形/所有方向人工观感或性能验收 |
| 实体键鼠 | 本批恢复普通窗口后再次通过computer-use选中实际Godot窗口；激活失败，刷新窗口选择后重试一次仍为`GetCursorPos 0x80070005`，未发送输入 | 仍未验收，不能由逻辑ActionPress或窗口启动证明完成 |
| URO等暂缓项 | ROADMAP顶部明确后移URO、额外Provider、全部UE调度/私有字段与完整Chaos/Jolt轨迹 | 音频/道具物理/头颈等原暂缓项保持 |

当前ALS模型检查验证4,898,155字节Mannequin FBX与原清单SHA一致、编译载荷SHA一致，`contact-input-core-v1-als-model-check.json`记录三份实际资源哈希。此独立检查在本轮运行中采集，不计入4680源保护基线；最终再次核对没有改变。资源只读，未重导/重导入/替换。

本轮再次检查旧GPU contact sheet，并逐份核对九张PNG仍为原审计哈希；可见指定台阶/坡面/站蹲/落差/着地和两武器握持样本，未观察到这些样本中的骨架爆炸或武器脱离。最终`contact-input-core-v1-acceptance-audit.json`汇总当前实现范围、资源/二进制引用/两武器/14接口/旧样本完整性，并明确实体输入未验收、goalComplete=false。

本批完成上述生产通用算法归属与ALS复用核对，完整目标仍因实体键鼠和最终玩法验收缺少直接证据保持开放。后续优先该验收，不能从本轮绿色测试推导全部UE或任意玩法兼容。

没有UE启动/修改/重导、资产JSON格式化、提交推送、新GPU、全量managed、十分钟、性能或跨平台验收。普通Demo已恢复为Rifle窗口，启动日志0错误警告，console PID101252/game PID76904；`contact-input-core-v1-desktop-input-check.json`记录本批两次实际窗口激活失败与未发送输入。窗口启动只证明启动成功，输入控制接口仍没有恢复证据。
