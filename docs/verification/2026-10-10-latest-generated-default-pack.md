# 最新已交付资产统一包与默认选择

应用户要求，将最新已交付 Kimodo 动作整合到同一个默认资源包，避免只切换 Mantle 时让 Idle／Walk 回退为原版。使用主目录和现有纯脚本管线，不依赖 Unreal 或 computer-use，不提交或推送。

## 选择范围

统一包为 `assets/generated/als_nonprop_20261010_latest_combined_v1/resource-pack.json`。来源与逐身份选择保留在 `artifacts/kimodo_als/default-latest-20261010/selection.json`：

- `als_nonprop_20261009_idle_arms_v2` 提供最新的 117 个非 Mantle 交付身份，包括 Idle、Walk、转身、起身、翻滚和各类姿态。
- `als_nonprop_20261010_mantle_hand_v5` 提供四个经过专用接触曲线和生产后手部蒙皮修复的 Low 身份。
- 旧通用生成中的六个 Mantle 不满足现有专用作者／接触合同；四个 Low 使用新交付替换，两种 High 保留原版回退。之前两项蹲走仍失败，没有从旧失败包补入。

合计 121 个替换身份；62 个非道具身份缺失或未通过源门禁，仍明确列为原版回退，包括 Run 和 High。原版道具保留。这里只切换已交付资产，不把失败生成视为成功替换，也不把默认选择视为生产验收通过。

## 管线与证据

`assemble.py` 校验两个源包全部载荷与源 rig，按身份组合作者输出和 receipt，复用已核对的纯脚本重定向输入。然后重新执行现有 `offline_pipeline.convert_bundle` 的目标处理、Root/Pelvis 分解、逐资产曲线、最终 FBX 烘焙和独立全关键帧回读。未复制旧 compiled 资源；Godot 导入后重新执行 P2B 和六域运行请求绑定。

本批证据根目录为 `artifacts/kimodo_als/default-latest-20261010`，转换、运行、普通默认启动各使用独立日志。`audit.py` 检查统一包载荷、源包保持、Refactored 摘要、替换／缺失身份、目标门禁和最终 FBX 闭包。

转换完成：121/121 最终 FBX 的全物理关键帧回读通过，十个 Walk 目标门禁与四个 Low 目标门禁均通过，生成 32 个运行 Root 曲线和 48 个提取轨迹曲线。`runtime-pose-provenance.json` 独立确认 121/121 最终 runtime raw 语义摘要与对应最新交付逐资产一致。

默认指针已切到统一包，并保留 `allowDiagnosticPack: true`；原指针逐字节备份为本批 `previous-default-pointer.json`。Optimize 构建为零警告／错误，资源扫描仅开放原版 ALS／raw、Lyra 与新默认包；Godot 导入、P2B 编译和六域请求绑定通过。

绑定最终化后的统一包 SHA256 为 `43d4b5eeecc13883c4958d9d9ce1742ee7401af16ce3328bdf0c0ddd840bc325`，1,501 个载荷逐字节审计通过；两个来源包的指针 JSON 与全部载荷均保持原摘要。数量为本批观察，不是新的固定库存门禁，见 `delivery-audit.json` 与 `audit-after-default.log`。

普通无覆盖重启已完成：清除 `ALS_RESOURCE_PACK_PATH`、旧 asset/raw 根、源请求与诊断输出等进程环境覆盖后，直接运行 `scenes/demo/als_demo.tscn` 180 帧，退出 0、没有 ERROR。`ordinary-default-restart.log` 明确记录 `replacements=121`、新统一包路径和 Root Motion 关闭，证明默认指针生效。

随后以同样的默认配置启动普通交互游戏。`ordinary-default-interactive.log` 确认新统一包与 121 个替换；`Godot ALS (DEBUG)` 游戏窗口存在且进程响应。该次启动的 WASAPI 输出设备初始化失败，Godot 自动回退 Dummy 音频；错误日志单独保留，不能宣称交互启动零错误或音频通过。未改变项目音频配置，动画运行与默认包加载正常；不将窗口启动视为人工完整动作验收。

完整基本游戏矩阵 71 项已执行，64 项通过、7 项失败，采集 246 张渲染截图。30／60／120 Hz 平台单／并行摘要一致，包载荷与项目动画设置未变化。失败保留如下：

- `recovery-auto-roll`：与此前相同的接地恢复错误，`cycle=1, position=(10.28468, 0.93768036, 3.634816)`，退出 1。
- 六个 `rendered-*` 场景：退出 0、动作 `_OK` 标记存在，但 WASAPI 初始化有 ERROR，按原有断言仍记失败。方向移动 112 张与左前停止 18 张捕获身份门禁通过；前进停止的 tick 35 捕获身份另有失败，不能全部归为音频。

独立 Mantle 蒙皮／接触和平台矩阵已完成，整体仍失败。静态 11 项中八个 headless 基本运行完成，三个 GPU 场景另有 WASAPI 错误；11 项完整接触／蒙皮联合门禁均未通过。

| 静态 Root 关闭 | 30 Hz 蒙皮峰值 | 60 Hz 蒙皮峰值 | 120 Hz 蒙皮峰值 | 最大支撑不可达残差 |
| --- | ---: | ---: | ---: | ---: |
| 左手 Default | 1.366 cm | 2.031 cm | 2.021 cm | 1.168 cm |
| 右手 Injured | 0.771 cm | 1.795 cm | 1.886 cm | 1.168 cm |

完整支撑掌面角度误差仍很小，actor yaw 均为零，但手腕不能达到固定锚点。60 Hz 单／并行的蒙皮和接触数字一致；两个候选 GPU 分别复现 2.031／1.795 cm。Root 开启峰值为 4.844 cm，另有支撑时序失败；原版 GPU 对照仍为 3.787 cm，不提高候选阈值。

最终 runtime raw 与源包逐资产相同，不等于角色组合／基础姿态一致。统一包出现了单独 Mantle 包没有的约 1.1–1.2 cm 支撑不可达残差，具体组合原因尚未关闭，不把旧单独 Mantle 包的九项通过记录沿用到本包。

| 平台／生命周期场景 | 基本运行 | 接触／策略 | 完整蒙皮峰值 | 联合结果 |
| --- | --- | --- | ---: | --- |
| short-low，0.75 m | 通过 | 通过 | 1.749 cm | 通过 |
| destroy，0.75 m | 通过 | 通过 | 1.749 cm | 通过 |
| moving-low，1 m | 通过 | 失败，残差 0.811 cm | 2.004 cm | 失败 |
| high，原版回退 | 通过 | 通过 | 11.736 cm | 失败 |
| moving-high，原版回退 | 通过 | 通过 | 11.627 cm | 失败 |
| cancel，0.75 m | 通过 | 通过 | 20.281 cm | 失败 |
| no-hand-lock，负对照 | 通过 | 按禁用策略通过 | 4.719 cm | 失败 |

静态 4,320 帧和平台 2,520 帧共 6,840 帧的提交／显示身份通过，静态另采集 180 张 GPU 图；两个 runner 的冻结输入均保持。新的短台阶改善只适用于上述受控场景，不关闭所有高度或人工验收。

默认 `full_pipeline.verify_runtime` 已结束，`runtime-result.json` 为 `runtime_failed`，`productionReady=false`、正式 `activated=false`；用户授权的默认诊断选择已生效。保留全部失败、源缺失清单、音频环境错误和捕获身份失败，不更改门槛或断言。下一步优先定位统一基础姿态／角色组合对 Low 支撑可达性的影响，再继续 Root 开启、取消恢复、High 和完整生产／人工／性能验收。
