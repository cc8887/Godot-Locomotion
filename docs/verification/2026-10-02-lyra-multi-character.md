# Lyra 多角色身份与共享资源

2026-10-02，直接在主目录推进。Debug/Optimize两配置的角色隔离和普通十角色矩阵均通过，GPU渲染时点修正后复跑通过，资源及程序集恢复哈希核验通过。整个Lyra迁移仍在进行中。

## 实现

普通入口增加 `--lyra-characters=1..10`，默认仍为单角色。玩家和同伴共享一个 `LyraLocomotionResources` 与 `LyraMontageCatalog`；同伴用真实 CharacterBody3D、地面/站起净空查询、完整 Main、五槽 Montage、ALS 参考最终 Rig 和68骨发布器。每个角色各自持有机器、播放器/Sync、曲线反馈、惯性、Rig、碰撞执行帧及模型，程序化同伴输入在物理线程推进。

```powershell
& ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --locomotion=lyra --lyra-profile=rifle --lyra-characters=10
```

玩家沿用WASD/蹲伏/跳跃/瞄准/Q输入；同伴各自移动和换类。同伴输入是程序化演示，没有行为树或导航验收。

`LyraCharacterAnimation` 给每个实例分配不复用的角色ID，绑定到 Main 调用上下文、cache、Slot及物理 Montage 的 `AlsFrameIdentity`。角色生命期间SlotGeneration保持1；装备更换只增加LayerEpoch。销毁重建得到新的角色ID，因此不会把新角色误认为旧装备代际。ID是本地进程身份，不是网络复制身份。

同一个actor拒绝创建第二个Lyra动画发布器。候选仍校验实际对象及帧；显式角色ID用于上下文/消费身份，不能代替对象归属。最终物理帧统一检查碰撞owner、capture serial、physics tick、world space、组件transform和当前自体RID，即使Rig alpha为0也执行。模型发布拒绝Model/Component退出树、重挂父节点或排队删除。

更换Layer时和销毁整个角色时都退休旧接口入口，并撤销其Source scope写Main的资格。旧Cancel只清自己的目标子图，不取消已由新Layer承接的Main。销毁过程先取消候选，再释放模型/查询节点，并移除actor发布登记；外部保留的旧Layer调用与旧Main scope入口均拒绝。

## 验证设计

六个活动角色组成三对，初始类分别为Unarmed/Pistol/Rifle，另有一个不更新的哨兵。每对两角色处于相同transform并使用相同运动输入，但三对使用不同yaw/起跳/换类时间；角色碰撞层2不互撞，地面及脚部查询使用真实Jolt静态几何。

每个物理帧先让六个真实候选同时存在，主体角色取消/重试，对照角色只提交一次。逐帧比较81骨、曲线presence/值、typed属性、RootMotion以及Main/Layer/Sync/惯性/Rig/Montage提交历史；最后逆序提交并校验各68骨真实Skeleton写值。静止哨兵的所有历史必须不变。

误用门禁覆盖外国角色候选、Skin、碰撞帧、Layer invocation、Slot identity及实际外国Montage bank frame。定期将Rig Component重挂到actor，要求晚期验证失败、拒绝提交，再恢复父节点并取消/重试。角色局部双轨UpperBody/UpperBodyAdditive动作进入实际Montage bank，另外两对保持独立。第六秒带pending候选销毁一对，再以同类/同位置重建，要求其余四角色历史保持，旧候选/Prepare/Rebind/Layer调用/Main入口均拒绝。

以上验证属于单主物理线程内的多角色交错执行，不是Godot物理查询工作线程并行验收；每对相同输入的对照也不代替UE整个Main的连续原生oracle。

## 最终结果

| 门禁 | Debug/Optimize各自结果 |
| --- | --- |
| 六角色30/60/120Hz | 10080角色帧、5040取消重试、72换类、6销毁重建、60627拒绝、54晚期故障恢复；三类和十个Main状态覆盖 |
| 完整通道/历史 | 每对逐帧骨骼/曲线/属性/RootMotion及提交历史精确一致；两个配置三Hz完整通道SHA256摘要相同，静止哨兵不变 |
| 普通十角色三Hz | 16800角色发布帧；玩家逐帧重试1680帧，全部角色68骨局部发布位置/旋转差0，模型/Rig世界最大差1.16801e-6m，原1e-4m门槛不变 |
| 单角色非零反馈换层 | 60Hz480帧，Main的DisableLegIK=1跨换类保留，新Layer曲线0；原Rig启用行为保持 |
| 原LocomotionSM native | 11340帧/9762姿态、246过渡；固定Provider边界位置最大1.13687e-13cm，原门槛保持 |
| Main含Rig宿主 | 7560帧/7296姿态，7560retry，含2097部分alpha/1479关闭/264隐藏/63故障恢复 |
| 真实Jolt最终Rig | 2520帧/2484姿态/2520retry，30744命中/2976未命中/21晚期失败，18次初始化 |
| 原ALS普通入口 | 60Hz1700帧、3Pivot/4Rest，无最终ERROR/WARNING |
| 实际GPU | Debug60Hz480物理帧、十角色、七张1280×720图；FramePostDraw侧车与绑定历史相符，抽查三张原图 |
| 构建/恢复/保护 | 两配置构建均0警告/0错误；818JSON/669UE包SHA256保持、六Debug文件SHA256恢复 |

最终两配置各11个实际进程退出0，日志无Godot ERROR/WARNING。汇总脚本实际退出0，生成`lyra-multi-character-verification.json`，并明确保留`nativeWholeMainParity=false`、`productionAccepted=false`、`legacyP4SmokeAccepted=false`。没有新UE运行/导出/修改；本批没有十分钟、全量或性能验收。

## 证据

- 构建：`artifacts/lyra-analysis/multi-character-{debug,optimize}-build-lifetime.log`；之后只改截图时点的两个`build-postdraw.log`均0警告/0错误。
- 最终矩阵：`multi-character-{debug,optimize}-lifetime-matrix.log`；六角色三Hz、普通十角色三Hz、单角色非零Rig反馈换层、原LocomotionSM native、Main含Rig宿主、原ALS普通入口及真实Rig碰撞回归。
- 六角色报告：`multi-character-{debug,optimize}-lifetime-{30,60,120}.json`。
- 十角色报告：`multi-demo-{debug,optimize}-lifetime-{30,60,120}.json`。
- 实际GPU：最终`multi-demo-render-postdraw.log/json`和`multi-demo-render-postdraw/*.png`。多角色截图使用额外全景相机，角色运动/求值路径保持同一入口。
- 复跑：`scripts/verify-lyra-multi-character.ps1`，新证据suffix防止覆盖；Optimize在备份后实际替换运行程序集，最终恢复六Debug文件并核验SHA256。
- 汇总：`tools/verify_lyra_multi_character.py` → `lyra-multi-character-verification.json`。

首次构建保留了局部变量同名和ActionDefinitionId字段名错误；首个运行保留了把双轨Montage误当成主槽为UpperBodyAdditive单轨的选择错误。修正为真实AdditionalTracks合同后，首轮三Hz和普通十角色已通过。首轮没有最终销毁入口撤销断言，最终矩阵另用lifetime后缀，不覆盖其证据。

最终Debug身份/数学矩阵结束后，抽查首轮GPU图发现`_Process`读取的是上一渲染图，跳跃截图HUD仍为Rifle。已将采集改为`RenderingServer.FramePostDraw`，新增每图物理帧/绘制帧/实际Provider/epoch/state侧车。只修改截图路径后两配置重建0/0；Debug完整十角色480物理帧渲染复跑退出0，并抽查瞄准/蹲姿/空中原图：HUD为Pistol/Rifle/Unarmed，与物理绑定历史一致。对应物理帧144/192/264、Layer epoch3/4/5；七图均1280×720且十角色可见。最终Optimize矩阵使用截图修订后的程序集，Debug已有身份/数学范围未改算法。旧`multi-demo-render`证据保留，不作为最终截图时点验收。截图验证不等于完整人工观感或性能验收。

## 开放范围

本批只关闭最终证据覆盖的角色身份、共享资源、交错候选/失败恢复及普通多角色运行边界。整个Main连续UE联合姿态/换类/通知对照、统一Notify和动作消费者、RootMotion碰撞消费、武器挂点、复杂地形/平台、完整视觉及性能仍开放。所有查询在主物理线程；线程并行纯动画求值尚未验收。旧P4直接夹具的AimOffset覆盖失败继续单独保留，不能用当前ALS普通入口通过称旧夹具已修复。未提交或推送。
