# Kimodo 非道具资源替换（2026-10-07）

## 范围与产物

用户要求除道具相关动画外全部替换。按当前 253 个动画引用身份，替换 V4 88 个、Refactored 95 个，共 183 个；保留 V4 38 个、Refactored 32 个，共 70 个武器/道具动画。Feminine、Masculine、HandsTied、Injured 是人物风格，包含在替换范围。保留原 Unreal 动画名、时长、采样接口和加法评价策略。

最终候选为 `artifacts/kimodo_als/non-prop-style-final-20261007/bundle-*.json`，原生包为 `artifacts/kimodo_als/non-prop-style-retarget/retarget-bundle.json`，运行包为 `assets/generated/als_nonprop_20261007_v8/resource-pack.json`。这些资源被 Git 忽略，仅检出代码不能视为可运行交付。原 `als_v4`、原配置和旧 native reference 夹具保留，不用旧夹具认证新表演。

## 生产和修复

实际 Kimodo 生成首批 60 个候选，修正翻越参考高度后再生成 2 个；最终图使用 48 个同源候选，其余资产通过基底、固定帧、参数轴、循环、左右镜像、反向与复制派生。所有执行都有独立回执、模型设置和 payload 哈希，没有将缺失输入填成旧动画。

Default/Normal 以及四种人物风格 Pose 表的 frame3 是 frame2 的末端填充。独立生成的 Feminine/Masculine 蹲姿虽约束起点，后续仍站起，根高度约 0.99 米，而共同 Crouch 为 0.749 米。改为仅生成上身风格 master，以局部旋转残差叠加到共同 Stand/Walk/Crouch；最终四种风格的根高度均为 `[0.990, 0.989, 0.749, 0.749]`。骨盆、腿和脚不受风格样本漂移影响。

翻越端点使用共同 Stand 加 1/2 米平台高度及 0.6 米前移，不再把结束姿态错误约束在地面。程序化左右镜像继续共享同一 master。完整姿态重定向后由目标 rig 进行加法求差，SOMA NPZ 不预先烘焙 UE 加法。

原生链路为 SOMA BVH → Mocara Manny → 已对齐的 ALS IK Retargeter → FBX/raw。183 个身份全部有原生输出；最终风格修订复用 139 个 BVH 字节完全一致的回执，重新重定向 44 个。Refactored 使用经物理 bind/hierarchy 验证兼容的 V4 目标 rig 回读，大小写适配后保留自身 virtual-bone 定义，不冒充另一个不同骨架的重定向。

发布同步更新 V4 传输文件、六域 raw bank、Refactored catalog、嵌入姿态、Root/IK、脚部接触包络、转向曲线、marker、脚步通知和输入摘要。Root 分解前后检查头/骨盆/手/脚 world pose 不变，IK helper 使用最终 FK。保留各状态的常量 Layer 曲线。Notify 时间采用与 .NET reader 一致的 float32 加法，并同步 nativeText LinkValue；marker 的索引及稳定 ID 重建。未通过放宽原断言消除发布错误。

失败版本 v1–v7、生成/重定向/初始化失败日志均保留；旧暂存树加 `.gdignore`，不参与当前 Godot 导入。没有删除失败证据，也没有覆盖原资源。所有生产 reader 在同一进程固定选择一个版本包，沿用现有角色时钟和提交/取消事务。

## 验证

- Python 管线回归：27 项通过，包含非道具完整闭包、末端帧复制、上身风格保留蹲姿腿/脚、目标 root 分解/IK 与 bind 不兼容拒绝。证据：`artifacts/kimodo_als/non-prop-python-tests-final.log`。
- 优化 Godot 构建：0 警告、0 错误。证据：`artifacts/kimodo_als/non-prop-build-final.log`。
- 原生重定向：183 个身份完成。证据：`artifacts/kimodo_als/non-prop-style-retarget-ue.log`。
- 发布检查逐字验证 70 个保留道具资源及全部 native/authoring 回执。证据：最终 resource-pack 的 `preservedProps` 和 `files` 哈希列表。
- Import Release 相关回归：917 项，916 通过、1 失败、0 跳过。失败为原资源 `ActualProductionSourceBankCoversRequestedPlayersAndNativeDependencies` 的 DefinitionDigest：当前定义 `10c6abed…` 与已有原 raw request `152e7913…` 不匹配。未修改原输入或放宽断言；它是仍存在的原包摘要失败，不能宣称完整 Import 全绿。证据：`artifacts/kimodo_als/non-prop-import-tests.log` 和 `tests/Als.Import.Tests/TestResults/non-prop-import-20261007.trx`。

最终包已通过 Godot 导入、P2B 126 个动画/141 个传输文件检查及六域实际图请求。360 帧普通入口 idle 无冻结/求值错误；后 240 帧上臂最大单帧变化为左 0.139°、右 0.126°，先前错误包为约 10.045°/8.429°。这是同诊断入口的连续性测量，不是新旧表演逐骨相等。真实 OpenGL Compatibility 渲染另跑 360 帧，已查看 frame180 的新 idle 截图；60/180/300 三张截图位于 `artifacts/kimodo_als/non-prop-v8-rendered-idle`。

站蹲/移动普通入口运行 1700 帧通过：standing922/crouching529/air104/sprint103，Grounded 状态 mask30、Locomotion mask29；116 个 native notify 来源、1700 个生产 Layering 帧、42 次脚步，18 次动态脚步修正。完整日志和最终蒙皮测量见 `non-prop-v8-stance.log`。固定站立窗口足底 RMS 每帧位移左约 0.000039 cm、右 0；跨全矩阵 planted 窗口左约 2.929 cm、右约 2.565 cm，最大漂移左约 198.51 cm、右约 134.16 cm。矩阵包含动作切换和刻意 15cm 扰动，这些汇总量不能解释为纯稳态滑步，但也不能据运行成功宣称整体接触质量合格；其峰值和步态接续仍需进一步美术/接触排查。

全部 13 Overlay 并行派发通过：mask8191、1 个 owner、360 帧、2 次取消、1 次 commit hold，完整脚部阶段/worker 和返回 Default 检查保留。证据：`non-prop-v8-overlays-v3.log`。低/高翻越各 360 帧通过，分别 active59/97、各 1 次启动，均最终落在平台且结束动作；证据：`non-prop-v8-mantle-low.log`、`non-prop-v8-mantle-high.log`。首次两个 Overlay 诊断缺少显式完整链旗标而启动失败，`non-prop-v8-overlays.log`、`non-prop-v8-overlays-v2.log` 保留；补齐与普通 Demo 一致的旗标，不删除原检查。

`non-prop-v8-runtime-verification.json` 汇总各项日志；`publish activate` 已通过覆盖/字节校验，默认 `assets/config/als_resource_pack.json` 指向 v8，包状态为 `runtime_validated`、`manualAcceptance=false`。原树保留作为回看入口，未把候选发布检查混同人工动作验收。

激活后清空资源包环境覆盖，以 `scenes/demo/als_demo.tscn --quit-after 360 --max-fps 60` 启动普通入口，日志确认通过默认指针选择 183 个替换身份，进程退出码 0、无 ERROR。证据：`artifacts/kimodo_als/non-prop-v8-default-demo.log`。这次启动不是十角色/多频率/复杂地形的完整长时矩阵。

## 使用与验收边界

完整再生产入口为 `scripts/produce-non-prop-als.ps1`；传入本机 Kimodo、UE 项目/可执行文件和 Godot 路径。生成、原生重定向、发布和真实运行校验串行执行，任一步失败不会切换默认包。`publish activate` 检查成功标记和 payload 字节后原子更新 `assets/config/als_resource_pack.json`。

显式检查新包：`ALS_RESOURCE_PACK_PATH=res://assets/generated/als_nonprop_20261007_v8/resource-pack.json`；回看原包：`ALS_RESOURCE_PACK_PATH=original`。进程内选择冻结，切换需重启 Demo。道具动画文件保留不代表混合新基础姿态后道具最终显示逐骨不变。

目标脚部接触曲线当前采用高度包络初值；受控运行和截图不能代替所有动作、滑步、步态相位、上身风格、持物融合、地形和长期性能的人工验收。`manualAcceptance=false` 明确保留该边界。本次没有提交或推送。
