# 2026-10-09 Godot 资源扫描范围优化

在主目录 `F:/Github/Godot-Locomotion` 执行，保留已有脏工作。使用项目内 Godot 4.7.2 Mono，不生成或重写动画载荷。

## 实施

新增范围工具和四项安全/候选生命周期测试；完整 pipeline 的运行验证入口在导入前同步范围。当前保留 `als_v4`、`als_v4_raw`、`lyra_als`、活动诊断包 `als_nonprop_20261009_idle_arms_v2`。35 个历史/诊断目录被忽略，其中 8 个已有标记，本次新增 27 个。资产不删除，当前指针及 `diagnostic`/非 productionReady 状态不变。

设计与复用命令见 [扫描范围契约](../architecture/godot-resource-scan-scope.md)。独立证据目录为 `artifacts/verification/resource-scan-20261009/`。

## 编辑器检查

同一个工作目录先后运行 `--headless --editor --path . --quit`，退出码均为 0：

| 指标 | 优化前 | 优化后 |
| --- | ---: | ---: |
| 编辑器资源缓存文件 | 32403 | 4214 |
| FBX | 4766 | 530 |
| JSON | 26568 | 2813 |
| 重复 UID 警告 | 141 | 0 |
| 单次 headless 编辑器进程耗时（秒） | 11.369 | 4.909 |

日志 `editor-before-console.log`、`editor-after-console.log`，计时分别在 `editor-before-time.json`、`editor-after-time.json`；优化后统计在 `cache-after.json`，范围在 `scope.json`。前后为单次顺序测量，可能受系统缓存影响，不是 GUI 冷启动基准或长期性能验收。

## 验证边界

`python -m unittest tools.test_godot_resource_scan -v` 四项通过：预览无副作用、幂等且载荷字节不变、候选重新开放/退出、非法及缺失路径在修改前失败、自定义标记保持。实际再次预览 `scope-idempotent.json` 的 changes 为空。

原版普通 Demo（`ALS_RESOURCE_PACK_PATH=original`）和当前活动包普通 Demo 均以 `--headless --quit-after 180` 启动并退出 0；对应 `demo-original-console.log`、`demo-active-console.log`。活动包日志确认选择 idle_arms_v2，活动包 reader 仍逐载荷校验摘要。该检查仅覆盖启动及有限运行，不等于完整动画质量或人工视觉验收。

真实 Godot `demo_locomotion_switch_smoke.tscn` 退出 0，`switch-console.log` 中 `DEMO_LOCOMOTION_SWITCH_OK` 确认 ALS/Lyra 切换通过；当前活动包下执行，未改原断言。

未重跑完整 Kimodo pipeline、71 场景矩阵、GUI 冷启动、长时间或视觉/性能验收。C# 未改，未重新构建。
