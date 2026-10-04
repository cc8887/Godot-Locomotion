# Lyra SkeletalControls 更新、Root / Weapon 算子

2026-10-01，在当前主目录继续第14个入口。验证仅关闭原图更新状态与两个独立骨骼算子；共同执行组仍13/14。完整 FootPlacement、同一组件姿态的12节点求值闭包、完整Main与普通Demo保持开放。

## 原图与输入边界

本机安装版UE5.8.1、GASP58三种Provider的实际编译图共12节点，输出root113。输入到输出顺序为：

`LinkedInput112 → LocalToCS111 → HandRetarget103 → CopyBone102 → Root104 → RightIK110 → LeftIK109 → Foot105 → LegIK107 → Weapon106 → CSToLocal108 → Root113`。

节点索引为实际compiled index，不能以class property index替代。绑定来自既有不可变`main_layer_graph_v1.json`及原实例compiled handlers；全局函数使用实际`UpdateSkelControlData`，Foot bool使用原`ShouldEnableFootPlacement`调用。

- 双手权重为double：`Clamp((DisableHandIK ? 0d : 1d) - (double)SelfCurve, 0d, 1d)`，原float节点pin在图访问时转换。Unarmed的DisableHandIK为true，Pistol/Rifle为false。全局函数在探针每次Provider更新时调用，即使该入口隐藏；隐藏时节点ActualAlpha保持历史。
- Root104由Main.EnableControlRig驱动，Foot105由有效Main、Main.UseFootPlacement及Self.DisableLegIK <= 0驱动。两者独立使用原0.2秒Linear布尔混合；不能以当前bool直接替代混合历史。
- HandRetarget与LegIK实际alpha为float逆曲线钳制。CopyBone alpha为1。Weapon106为Self.ScaleDownWeaponR乘100f后钳制，组件空间scale替换为double(0.05,0.05,0.05)。Root104组件空间translation加(0,0,-2)厘米，其他分量忽略。
- 反馈来自上一提交的Main曲线复制到Linked实例，独立于本帧输入Sequence曲线。测试的Main最终反馈为受控输入，不是完整Main图的自主输出。

FootPlacement内部只有有效且alpha > 1e-5的访问才累计float delta并同步遍历counter。其原Initialize只重置第一更新标志及几何历史，保留CachedDeltaTime和counter；隐藏间断后重新相关时按原counter规则重置几何历史，也保留累积delta。有效Evaluate结束才清零delta并解除第一更新标志。更新但不求值的时间不能丢弃。

## 实现与原生采集

新增`AlsLinearBoolBlend`保留FInputAlphaBoolBlend/FAlphaBlend的float计算次序、目标反转、Initialize和首访即时到达语义。`LyraSkeletalControlUpdateHost`将全局double权重、八个节点alpha、两个混合历史与Foot更新状态作为不可变候选；取消/重试及异owner候选拒绝均不发布历史。

`CompleteFootEvaluation`是供将来Foot solver使用的成功求值边界。当前验证按受控入口Evaluate访问调用它，**该方法没有求解骨骼，也不代表FootPlacement已实现**。整个宿主是独立更新组件，尚未接入13入口共同执行实例或角色生产事务。

新增`AlsLyraRootWeaponControls`用已有精确FCSPose语义实现两个原ModifyBone算子。当前各自从同一原始输入开始；不是将两个输出拼接成完整SkeletalControls求值。

新外置只读探针`AlsLyraSkeletalUpdateLibrary`在临时GamePreview世界/实际Character、Main及Linked Provider上执行原全局函数和12节点Update。ALS81骨布局与六条现有扩展Sequence继续复用；模型68根蒙皮骨、原权重和材质没有变化。原闭包实际Evaluate的完整输出也已采集，包括启用FootPlacement的轨迹，但本轮Godot只比较自主源输入和Root/Weapon两个独立输出。

临时世界没有碰撞地形，Foot使用组件地面回退，角色运动状态为受控临时实例。此完整原图输出是后续移植的受控参考，不能代替地形/平台/真实移动FootPlacement验收。没有新增UE资产保存，也没有更改UE引擎源码或GASP主项目插件。

## 结果

三Provider × 30/60/120Hz × 6秒：

| 项目 | 结果 |
| --- | --- |
| Update / 取消重试 | 3780帧，全部before/updated/after状态逐位一致 |
| 自主原始输入及两个算子 | 每组3051姿态、247131骨 |
| 全数据通道 | 每组3051曲线包、12204整数属性、3051 RootMotion属性 |
| 入口隐藏 / 仅更新 | 348 / 381帧 |
| 重新Initialize | 39次，含隐藏初始化 |
| Root / Foot部分混合 | 2250 / 1983帧 |
| Foot原生有效Evaluate / 累积时间 | 1626 / 366帧 |
| 错误操作拒绝 | 22680次，含异owner、重复准备/完成/提交、取消和重试后的旧候选 |
| 最大位置 / quaternion / scale误差 | 2.1953104400669003e-13 cm / 5.123986725866783e-16 / 0 |

原严格位置1e-8cm、quaternion1e-10、scale1e-12门槛保持。Godot源pose、曲线和typed属性由既有真实资源自主采样，不读取原生预期pose作为算子输入。RootMotion由既有raw sampler生成。

两次独立UE采集均实际退出0、0错误、794条原资产/临时依赖警告；第二次复采与已保存fixture语义一致，未重写fixture。508个原资源包与670份之前JSON逐文件字节哈希不变。新增ignored三文件：

- `skeletal_update_v1_requests.json`：1527717字节。
- `skeletal_update_v1_policy.json`：45272字节，绑定原图及依赖哈希。
- `skeletal_update_v1_native.json`：190184517字节，含完整原图受控输出与源/资源哈希。

日志在`artifacts/lyra-analysis/`：

- `skeletal-update-ue-export.log`、`skeletal-update-ue-export-repeat.log`：两次成功采集。
- `skeletal-update-godot-final.log`：更新和三个姿态边界成功。
- `skeletal-update-debug-final.log`、`skeletal-update-optimize-final.log`：均0错误0警告。
- `skeletal-update-leg-ik-regression.log`：既有3780帧LegIK/弯曲历史通过。
- `skeletal-update-main-als-regression.log`：既有11340帧Main LocomotionSM、9762混合姿态、12595根调用通过。
- `skeletal-update-main-aiming-regression.log`：既有共同13入口、11340帧/9762姿态通过。
- `skeletal-update-verify-final.log`：资源/fixture/源代码镜像/两次UE/最终Godot/构建总门禁通过。

初次UE探针头文件路径错误、第二次FLeaf/Modify符号冲突和初次C# Math命名空间冲突均已修复；原失败日志保留。最终外置UE构建`skeletal-update-ue-build-qualified.log`退出0。未放宽数值阈值，没有跳过失败case。

## 复跑与剩余边界

从`..`构建（避开项目旧global.json SDK钉死），然后在主目录运行：

```powershell
dotnet build ./GodotALS.csproj -c Debug --no-restore
```

```powershell
./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . scenes/tests/lyra_skeletal_update_smoke.tscn
python tools/verify_lyra_skeletal_update.py
```

再采集使用`scripts/export-lyra-skeletal-update.ps1`，要求已构建对应外置插件，明确EngineRoot与UnrealProject；固定fixture存在时只校验语义，不覆盖。已有源码、原资产、fixture依赖变化会拒绝验收。

下一步是FootPlacement完整几何/插值/地面输入与历史事务，再将双手、Root、Foot、Leg与Weapon按原图顺序接到同一组件姿态，完成第14入口与Main最终位置。之后继续原上身/Slot/惯性、统一Notify、动态Provider换类、普通Demo/多角色/渲染/性能。没有宣称完整Main或整个Lyra移植完成，无本轮渲染/人工玩法/性能或完整地形验证，原ALS R2–R7和用户暂缓项保持。
