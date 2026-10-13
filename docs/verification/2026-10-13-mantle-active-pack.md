# 攀爬修复加入当前活动包

按用户“请把修复的资产加入活动资产”的授权，活动指针现选择`als_nonprop_crouch_stop_mantle_20261013_v1`，继续使用`allowDiagnosticPack: true`。这次启用保持诊断包模式，未修改完整生产门禁或将`productionReady`设为true。

## 合成范围

以原活动包`als_nonprop_crouch_stop_final_20261013_v5`为底，选择已验证的默认风格Mantle候选`als_nonprop_mantle_refined_13cca1702cead705`中的六个攀爬身份：V4／Refactored各自的High、Low_Left和Low_Right。

复用已校验的作者来源、重定向输入和回执，没有新增diffusion或重新重定向。通过独立Pipeline的正常stage、Root／曲线、最终FBX回读和Godot交付流程生成新版本。六个Mantle最终raw语义摘要与修复候选一致，其他155个动作与原活动包一致，包含四个蹲停修复资产；70个道具的保留清单一致。没有将旧基包整体替换为先前Mantle候选中额外重建155项twist的版本。

制作记录在`F:/Github/Kimodo-Pipeline/workspace/runs/mantle-active-20261013-v1/`，其中`composition.json`记录选取范围、输入摘要和原活动指针，`pose-retention.json`记录161项逐资产比对。原指针字节保存在`default-pointer-before.json`。

## 验证与启用

| 检查 | 结果 |
| --- | --- |
| 六项Mantle／四项蹲停目标检查 | 6/6、4/4 |
| 最终FBX全部物理骨／关键帧回读 | 161/161 |
| Optimize、导入、P2B及六个资源域绑定 | 通过 |
| 所选通用／攀爬E2E | 8/8 |
| Air、静态Low、平台／生命周期矩阵 | 各11/11 |
| 补充Low／1.5m High／2m High／1.8m移动平台，双模型、Root开关 | 16/16，完整蒙皮峰值约1.908 cm，输入未变 |
| 蹲停：双模型、两释放相位、单／并行，60Hz | 候选8/8、原版对照8/8，单并行一致性通过 |
| 切换后普通Demo，无包覆盖并启用资源哈希校验 | 通过 |
| 切换后1.5m High，默认包、girl、Root关闭 | 接触／完整蒙皮／帧身份通过 |

启用前验证通过后原子更新[活动配置](../../assets/config/als_resource_pack.json)，并在不设置`ALS_RESOURCE_PACK_PATH`的进程中验证普通入口和攀爬。最终1798项载荷校验通过。启用回执：[activation-receipt.json](../../artifacts/tests/mantle-active-20261013-v2/activation-receipt.json)；补充矩阵：[Mantle](../../artifacts/tests/mantle-active-20261013-v2/mantle-models/results.json)、[蹲停](../../artifacts/tests/mantle-active-20261013-v2/crouch-stop/summary.json)。

首次编排漏建FBX回读输出目录，失败保留在制作目录`compose.log`；补目录后从回读继续，未覆盖旧包。首轮补充Mantle的16项功能结果通过，但期间其他任务重编译共享DLL，完整性检查拒绝，证据保留在`artifacts/tests/mantle-active-20261013-v1/`。正式启用使用新目录`v2`的稳定输入重验，未把首轮拒绝改写为通过。

## 边界

合成包仍缺22个非道具身份；所选E2E不构成完整方向矩阵，完整生产、人工风格、多seed及长期性能验收仍开放。原完整矩阵的拒绝状态保留。本次完成的是用户授权的诊断包默认启用。运行中的旧游戏实例需要重新启动才能读取新指针。

攀爬修复方法与先前候选验证见[通用风格制作记录](2026-10-13-mantle-style-generalization.md)。
