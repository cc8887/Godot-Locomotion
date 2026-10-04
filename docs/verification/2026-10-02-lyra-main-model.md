# Lyra 完整 Main 到 ALS 模型及普通入口

2026-10-02，主目录 `.`。本批没有启动、修改或导出 UE；消费已有目标资源和经过原生对照的宿主。整个 Lyra 移植目标仍进行中。

## 本批结果与边界

Unarmed、Pistol、Rifle 三种固定配置已在普通入口中，以真实输入、CharacterBody3D 运动和 Jolt 物理运行完整 Main，启用显式 ALS 参考的最终 FootPlant Rig，再将最终 logical81 姿态一次发布到原 ALS 68 骨模型。Debug/Optimize 各三配置×30/60/120 Hz、5040 帧通过；实际渲染三配置各480帧、7张图通过。此处“完整 Main”指实际组合宿主执行范围；本批没有采集整个 Main 的新连续 UE oracle，不代表该整链已严格逐值原生验收。

进入方式：

```powershell
& ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --locomotion=lyra --lyra-profile=rifle
```

`--lyra-profile` 可取 `unarmed/pistol/rifle`。WASD移动、Ctrl蹲伏、Space跳跃、右键瞄准、Esc释放鼠标，移动速度随步行/蹲姿/瞄准改变。默认项目入口仍是 `als_demo.tscn`，其中 `AlsDemoEntry` 依据显式 `--locomotion=lyra` 加载新场景；本批未修改 `project.godot`。新场景也可独立运行。

当前是加载时选择固定 Provider，尚无完整 Main 的运行中换装、多人实例或武器模型挂接。旧独立示例的 Q 换层证据不能当成本批新宿主换层验收。

## 实际接入

| 文件 | 行为 |
| --- | --- |
| `src/Als.Godot/Animation/Lyra/LyraAlsCharacterBinding.cs` | 加载原 ALS 模型、校验68骨名称/父序/蒙皮布局；关闭导入AnimationMixer，拒绝其他SkeletonModifier；先转换/检查完整候选，提交后唯一骨骼发布 |
| `src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs` | 每个角色持有真实Main、五Slot Montage bank、Rig碰撞实例和发布器；显式ALS参考、源/曲线反馈/惯性/最终Rig共同提交取消 |
| `src/Als.Godot/Locomotion/LyraLocomotionDemo.cs` | 采集实际键鼠、碰撞移动、地面和组件空间，驱动原Main状态/层求值；处理胶囊站蹲、站起净空及相机 |
| `scenes/demo/lyra_locomotion_demo.tscn` | 实际角色、地面、碰撞通道、相机和渲染；平地三配置运行 |
| `src/Als.Godot/Locomotion/AlsDemoEntry.cs` | 普通入口显式Lyra分支，沿用原ALS默认路径 |

骨架保持 raw69/logical81/skin68。`weapon_r` 和武器空间虚拟骨参与完整姿态求值，最终仅按 skin 映射写入原蒙皮，未重做网格。原ALS导入局部轴为 `(X,-Y,Z)`，Rig物理适配为 `(Y,Z,-X)`；`LyraRigComponent` 的正旋转把两空间连接，使用模型真实组件变换。每帧验证全部68个实际骨骼的局部TRS及世界位置，因此没有以“模型存在”代替模型/Rig空间一致性证据。

Main保持原共享source scope/Layer调用、Slot、惯性、RootYaw、SkeletalControls与最终Rig顺序；运行启用 `AlsCompactReference`。本场景采用 `UseFootPlacement=false` 的最终FootPlant路径，若误访问provider另一FootPlacement地面查询会明确失败。五Slot bank已经真实接入但本场景未请求Montage动作，不把非活动槽当成动作消费验收。

Layer机制继续使用当前编译合同和类型化执行实例：14入口共同 `ItemAnimLayers` 组，Main保留宏观状态和调用顺序，Provider实例保存自己的播放器/机器/cache历史；输入/输出同时携带姿态、曲线、属性和RootMotion，统一源Sync后求值，最终一次提交到Skeleton。通用Animation Interface、命名组和未命名独立实例的推广边界见 [ALS资源与Layer方案](2026-10-02-lyra-als-layer-design.md)。

## 验证

| 验证 | 实际结果 |
| --- | --- |
| Debug / ExportRelease Optimize构建 | 两次0警告/0错误 |
| 固定三Provider、30/60/120 Hz，Debug与Optimize | 每构建9组5040帧；全部由普通入口进入，实际输入驱动移动/站蹲/瞄准/空中/落地/反向 |
| 每帧完整候选取消重试 | 每构建5040次；完整pose/curve/attribute/root逐值保持，已提交观察/Rig/模型发布次数不变 |
| 晚期真实组件变换失效 | 每构建54次；移动模型后拒绝提交，恢复变换仍拒绝原失败候选，Cancel后可重试 |
| 模型局部发布 | 68骨局部位置/四元数与最终姿态转换的最大差均0；世界空间最大差1.1246181657043053e-6米，原门槛1e-4米 |
| Main实际状态覆盖 | 每组0/1/2/3/4/6/7/8/10/11；5/9为conduit，不作为持久状态覆盖要求 |
| 真实Rig查询 | 每构建140448次；每组30/60/120 Hz分别6656/13376/26784次 |
| GPU实际渲染 | OpenGL/NVIDIA RTX5080，三配置各60Hz480帧，21张1280×720图；没有Godot ERROR/WARNING |
| 当前ALS普通入口回归 | 60Hz1700帧通过，3Pivot、4动态补步、站蹲/空中等覆盖，无ERROR/WARNING |
| 资源保护和运行恢复 | 813历史JSON加5独立固定JSON共818份、669个UE包SHA256相同；优化验证后六Debug文件SHA256恢复相同 |

所有成功进程均保存实际退出0。渲染图逐张汇总查看21张，并单独查看Unarmed站立/蹲姿/跳跃原图；完整身体可见，三种姿态和空中动作可见。这是平地短轨迹抽查，未完成握持、复杂地形、全部帧或人工观感矩阵。

汇总图：`artifacts/lyra-analysis/main-model-render-contact.png`。各原图/日志/报告位于 `main-model-render-{unarmed,pistol,rifle}`。武器握持动画当前为空手，不能当作武器挂点/手掌接触视觉验收。

运行证据：`artifacts/lyra-analysis/main-model-debug-{profile}-{hz}.{log,json}`、`main-model-godot-optimize.log`、`main-model-optimize-{profile}-{hz}.json`。自动核验入口为 `tools/verify_lyra_main_model.py`，优化运行/备份恢复为 `scripts/verify-lyra-main-model-optimize.ps1`。最终核验日志 `main-model-verification.log`、报告 `lyra-main-model-verification.json` 明确保留 `nativeWholeMainParity=false` 和 `productionAccepted=false`。

### 失败记录

首轮把profile写成大小写不同的`Unarmed`，被资源表拒绝，日志为 `main-model-debug-first.log`。公共输入改为标准化小写后全部三配置通过；未更改资源JSON。

附加旧 `p4_demo_smoke.tscn -- --als-smoke-frames=300` 实际退出1，报AimOffset正负yaw覆盖不足，yaw范围[-2.9728298,-0.95137215]、pitch[-0.3,0.3]，证据为 `main-model-als-entry-regression.log`。该夹具直接实例化 `p4_locomotion_demo.tscn`，源码不经过 `AlsDemoEntry`，也不构造本批新Lyra类；不能把它当成新入口回归成功，亦未证明其此前基线已通过。当前普通入口的 `refactored_stance_demo_smoke.tscn` 1700帧成功证据另存 `main-model-als-ordinary-regression.log`。旧夹具失败原因仍待专项核对，保留断言和失败证据，未放宽门槛。

## 保持开放

本批关闭固定Provider真实完整Main到原ALS模型发布、实际物理与普通显式入口的上述范围。仍须完成运行中Provider更换的原实例/代际/惯性生命周期、多角色身份隔离与共享不可变资源、统一Notify/typed消费者、Montage动作及RootMotion碰撞消费、武器模型/挂点、地形/台阶/平台/完整视觉与性能。当前CharacterBody移动采用场景明确加速度/制动，不称UE CharacterMovement物理等价。整个Main新连续UE/Godot原生联合对照保持开放。

原ALS路线、音频/道具物理/头颈暂缓项、旧未提交改动和所有既有证据保留；本批无提交或推送。
