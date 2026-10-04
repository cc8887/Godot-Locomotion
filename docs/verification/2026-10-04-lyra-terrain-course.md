# Lyra 普通 Demo：可见地形与武器近景

本批继续当前 locomotion 目标，复用 ALS 人物、Core 运动/动画/Rig 与两把武器。没有 UE 启动、修改或资源重导，没有格式化资产 JSON，也没有提交/推送。

## 普通玩法接入

普通 Lyra Demo 默认生成两条可见路线：25 cm 台阶、约11.31度斜坡（顶高25→85 cm）、85 cm平台/落差；左侧另有60 cm阻挡。场景网格与真实碰撞体同步创建，collision layer5同时供普通胶囊和原最终FootPlant查询。日常入口：

```powershell
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT -- --locomotion=lyra --lyra-profile=rifle
```

WASD移动、Ctrl蹲伏、Space跳跃、右键ADS、左键开火、R换弹、Q循环Unarmed/Pistol/Rifle。用户原按键绑定保持；没有添加逻辑键兼容别名。

`LyraTerrainCourse`属于Godot场景层，只创建网格/碰撞与收集观察，运动继续使用`LyraSceneMovementService`，完整Main/Linked/Sync/Montage/通知/最终FootPlant仍用`LyraCharacterAnimation`，最终仍为ALS logical81→skin68单次发布。正常模式由玩家控制；专用terrain-smoke沿玩家原输入路径驱动，并增加一个共用资源和移动服务的手枪角色。NPC逐帧取消/重试，不重复物理移动；玩家复用原完整候选与故障恢复检查。

原`--lyra-main-smoke`平地验证入口保持其原几何，完整报告可与旧版本直接比较。训练标识仅描述场景和操作，没有把内部实现信息放入HUD。

## 实际运行验证

最终v3 Debug与ExportRelease构建均0警告/0错误。两构建三频：

| Hz | 每角色帧数 | 角色数 | 总蒙皮发布 | NPC重试 |
| --- | ---: | ---: | ---: | ---: |
| 30 | 450 | 2 | 900 | 450 |
| 60 | 900 | 2 | 1800 | 900 |
| 120 | 1800 | 2 | 3600 | 1800 |

每构建三频共3150角色时间帧/6300蒙皮发布；上述计数仅为headless三频，不包含额外渲染和原十角色回归。每一角色都实际走过台阶/斜坡/落差，完成站蹲、ADS、跳跃/两次着地以及Pistol/Rifle和武器动作。60Hz各5次Stepped、86帧实际斜坡法线、23帧落差腾空、2次着地、90蹲姿帧和150ADS帧，最大落地胶囊底高度约0.870m。高度只统计Grounded帧，不借起跳高度满足平台覆盖门禁。

三频每份Debug/实际Optimize完整报告相同；最终v3 headless报告也与v2逐项相同。1800/3600等计数为实际完整Main/Rig后的蒙皮，不是只测胶囊移动。每帧记录胶囊位置/状态、实际地面法线、最终ALS左右foot骨世界位置及独立真实ray的地面和高度差、Rig查询及武器发布。foot骨原点高度不是鞋底或足锁误差，不能直接据此宣称零滑步。

额外GPU60Hz运行900帧/两角色，FramePostDraw九张图：overview/step/slope/crouch/drop/landing/rifle/pistol/jump。所有图已检查；台阶/斜坡/下降与着地、稳定蹲姿、起跳可见，两把武器的手臂和握持近景可见，没有在这些样本中观察到骨架爆炸或明显武器脱离。图位于`artifacts/lyra-analysis/terrain-course-v3-debug-frames`，总览为`contact-sheet.jpg`。这是指定样本视觉检查，不是连续全部地形/所有方向的人工验收。

v2蹲姿图取到了切换第一帧；v3仅将该采集延至2.2秒，重新构建/三频/渲染，并对照完整报告确认移动和动画轨迹保持。v2成功证据和源码副本保留。v1首轮900帧也通过；之后将平台高度统计限定于实际Grounded，保留初始报告。

原普通十角色Debug/实际Optimize额外各480帧/4800蒙皮发布，动作、通知和换装报告与Rig/Core批次逐项相同。最终地形7进程＋原普通2进程共9成功运行；日志无ERROR/WARNING，两轮Optimize均逐文件恢复六个Debug DLL/PDB。相关Core148测试和Rig27进程来自前一同日Core复用批次，本批没有重复这些数学测试。

独立审计`tools/verify_lyra_terrain_course.py audit`、`terrain-course-v3-audit.json`和`terrain-course-v3-ordinary-audit.json`通过：4实施/验证源冻结，其余4638基线含原Core、870JSON/710原UE包/9配置保持。运行期间源码不变；脚部查询使用实际Jolt，动画求值后从已写入骨架读取位置。没有全量managed、十分钟、性能、跨平台或完整Chaos/Jolt等价验收。

## 窗口输入与剩余工作

正常地形Demo已启动，无自动输入。Windows computer-use两次激活当前Godot窗口均失败：`GetCursorPos failed: 拒绝访问。(0x80070005)`，重选当前窗口后恢复失败；没有发送按键，没有改InputMap或改安全设置。这是Windows控制接口失败，不能据此判定实体键盘有问题，也不能把自动ActionPress当实体键盘验收。窗口等待本机操作回报。

仅关闭本批可见关卡、普通移动/完整输出和九个指定视觉样本。实体键盘/鼠标实际验收、其余通用Core审计及完整目标保持开放；任意复杂地形、全部方向/握持/性能等扩展保留后续路线。URO/全部UE调度和额外Provider仍后移，音频/道具物理/头颈暂缓保持。
