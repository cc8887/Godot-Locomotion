# 中高攀爬生成、接触时序与游戏验证

## 本批实现

在主目录继续实现，保留原有修改，未提交或推送。复用既有 Kimodo 纯脚本管线和 Godot `mantling_demo_smoke.tscn`，未使用 Unreal 或 computer-use。

系统沿用 Low／High 两类动作：probe 实测高度超过 125 cm 选择 High；本夹具的 capsule 底面略低于地板，1.25 m 台面实测约 126.9 cm，因此已进入 High。增加 1.2、1.25、1.5、1.8、2 m 静态高度及移动平台检查，无新增未验证的 Medium 资产类型。

生成条件将 Hips 从逐帧条件改为最多 19 个稀疏关键帧，并在 High 接地后 73/30 s 增加全身稳定条件。沿用原 prompt、模型、种子、100 步扩散和 SDK 后处理；本批同时改变上述两处条件，不是单因素归因试验。稀疏限制来源于 [NVIDIA Kimodo limitations](https://research.nvidia.com/labs/sil/projects/kimodo/docs/key_concepts/limitations.html)。

运行时修复了三个问题：

- 所需腕部纠正被错误截为离线 10 cm 预算。1 m 真实场景请求 12.5756 cm，骨长可达超距为零，原限幅留下 2.5756 cm 残差。现在直接求解固定平台锚点，保持骨长，对真正不可达目标继续报告残差。
- 身体 montage 权重重复削弱支撑权重。手部曲线独立控制接近、固定支撑和释放，身体淡入不使已支撑的手滑动。
- 高度匹配跳过准备段，导致身体尚在 Idle 淡入时手部已经进入完整支撑。pose 起点提前以保留原淡入时间，抬升仍以高度匹配点为零基准；前移、抬升和接触均使用同一 montage 时钟。第一次播放使用选定 pose 起点。

High 离线接近／释放修复增加前缘退出方向，仍限制 wrist 位移 10 cm、指节旋转 45°。左手拇指根部包含不能通过指节单独移动的蒙皮；目标手臂 IK 使用固定 1 cm 向上腕点适配，并计入既有修复预算，对未偏移参考仍执行 2 cm 位置误差门禁。运行烘焙锚点使用同一偏移。Root、Pelvis、骨长、原生曲线以及参考来源不以修复算法替换。

完整蒙皮检查暴露了身体提腿／前移和部分接触的腕点弧线问题，进一步实现：

- High 前移控制由最终目标蒙皮烘焙，排除手部后使用 16,269 个身体顶点，保持 0.5 cm 前缘余量、单调前移与原着陆终点，限速 200 cm/s。最大前移延后 27.986 cm。生成腿部、躯干、Root／Pelvis 轨道不改写，抬升曲线保持原数据。
- 身体准备时刻为最终腕点进入台沿 20 cm 接近范围的 0.333333 s，让淡入在接触前完成；原完整支撑区间保持。
- 接近／释放改为先在组件空间混合腕点目标，再做固定骨长 IK；避免逐关节混合导致腕点弧线经过台面。过渡曲线提供最大 3 cm 台面法线抬升，两端为零，完整支撑仍使用原固定锚点。

以上通用求值在纯 .NET Core，资源与真实平台查询在 Import／Godot；没有另建角色时钟或提交链路。稳定合同见[接触与身体控制](../architecture/als-mantle-contact-control.md)。

边界检查随后发现 1.2 m 仍选择 Low，而按 1 m 制作的动作在抓沿前缺少额外高度补偿，左手指节峰值达 9.575 cm。提前接触权重不能解决，失败原型保留在 `low-approach-prototype/`。固定额外高度补偿原型验证了时序原因，正式实现由 Core 按实际高度计算：名义剩余抬升继续按原曲线推进，超出部分在身体淡入结束／接近抓沿的较早时刻完成。保留原类型选择、原曲线和资产轨道；原型包不作为默认或交付。最终重验使用原 v5 资产和新 Core，包字节摘要保持。

## 生成与最终导出

证据目录为 `artifacts/kimodo_als/mantle-high-20261010-v1/`。

实际生成 High／两个 Low 共三个候选，六个 V4／Refactored 交付身份通过作者门禁。High 骨盆最大偏差从上批 16.434 cm 降至 9.036 cm，保留原 15 cm 门禁；两 Low 分别为 4.385、4.248 cm。抓沿位置／朝向、起止骨盆和结束支撑脚检查通过。

首版 High 目标检查因接近手部穿模失败；保留 `full-pipeline/` 中的失败证据和六个最终 FBX 回读。该版部分运行检查被终止，原因记录在 `standalone-runtime-interruption.json`，没有将未完成的矩阵表述为通过。前缘修复后仍有左手拇指根部 2.607 cm 深度；固定腕点适配后降至约 1.6075 cm。被替代的组合尝试也保留，不作为交付证书。

最终包为 `assets/generated/als_nonprop_20261010_mantle_high_combined_v5/resource-pack.json`：117 个其他最新动作与六个新 Mantle 身份，共 123 个替换；剩余 60 个非道具身份保留缺失／原版回退，70 个道具身份继续保留。六个目标 Mantle 门禁和 123/123 最终 FBX 逐物理骨骼／关键帧回读通过；运行 Root sidecar 34 份，分解前 Root sidecar 50 份。并非完整非道具生产闭包。

最终制品与输入摘要见 `combined-v5/delivery-audit.json`：1,524 个载荷摘要全部通过，117 个其他最新姿态与旧默认包语义一致，包 SHA256 为 `424179383aa5c6b388f14fb3e8cac19a3ee773af4712908eb773d72a6829cb00`。选择过程与重建输出见 `combined-v5/selection.json`、`combined-v5/conversion/pipeline-result.json`。生成资产被 Git 忽略，仅代码检出不是可运行交付。

## 测试与覆盖边界

最终 Core Mantle Release 46/46 通过，包括零分配、固定骨长、移动平台锚点、真正不可达目标、组件空间过渡、抬升端点以及准备段／实际高度适配的同钟求值，见 `core-height-tests.log`。最终 Python Mantle／Contacts／HandRepair 24/24 通过，见 `python-final-tests.log`。Godot Optimize 构建零警告、零错误，见 `combined-v5/runtime-height-v1/build.log`。

最终 Import Mantle Release 101 项中 96 通过、5 失败；失败均在既有 `AlsMantlingHostPoseTests` 输入身份检查，保留 `import-height-tests.log`，没有改写原生参考或放宽断言。4,536 帧原生 RootMotion 轨迹回放仍在通过项中。

单独运行最终 Contact／Motion 编译集 9/9 通过，见 `import-contact-motion-height-tests.log`。

早期运行对照记录在 `runtime-contact-fix/`、`runtime-support-weight/`、`runtime-contact-entry/` 和 `runtime-lift-entry-original-high/`。最后一组使用此前默认统一包，High 仍为原版回退：五个高度场景身体完成及手部接触通过，但四个 High 高度的完整蒙皮失败，不能拿该组证明生成 High 合格。一次未编译 candidate `.tres` 的直接启动失败保留在 `runtime-lift-entry/`，后续通过完整 Godot 导入／P2B 生成运行资源。

最终 Godot 构建、导入、P2B 和六领域请求绑定已执行；同批复用相关 E2E、静态手部、平台／生命周期以及中高 GPU 场景。GPU 几何场景明确使用 Dummy 音频，不覆盖 WASAPI 音频验收。

### 高度适配前的 High 基线

`final-v5-render/results.json` 三项均通过实际场景、固定支撑、完整蒙皮及角色身份检查；360 帧／项，共 1,080 帧，180 张实际 GPU 截图。输入包／载荷／运行程序集保持不变，`inputsUnchanged=true`。

| 台面高度 | 最大蒙皮深度 | 最大 wrist 锚点误差 | 完整支撑可达残差 |
| --- | --- | --- | --- |
| 1.5 m | 1.607502 cm | 0.000518 mm | 0 cm |
| 1.8 m | 1.607513 cm | 0.000352 mm | 0 cm |
| 2.0 m | 1.607490 cm | 0.000495 mm | 0 cm |

三组最大掌面误差 ≤0.000045°，平台局部漂移 <0.0008 mm。运行时最大所需纠正约 36.3 cm，但实际骨长超距为零；没有用离线 10 cm 限幅制造假残差。上述几何通过仍容许原 2 cm 门槛以内的交叠，不等于零穿模或美术节奏验收。

高度适配前独立 30 Hz／120 Hz 均验证 1.25、1.5、1.8、2 m 和 1.8 m 移动平台，各 5/5 通过；每例观察 6 秒，分别为 180／720 帧，共 4,500 帧。完整支撑可达残差均为零，完整蒙皮峰值约 1.6075 cm，两组 `inputsUnchanged=true`。见 `final-v5-hz30/results.json` 与 `final-v5-hz120/results.json`。该固定功能场景不作为耐久测试。

被替代的 v4 已通过离线门禁，但 GPU 中 1.5／1.8／2 m 完整蒙皮分别约 9.59／11.91／10.33 cm，故继续修复。修复期间运行代码变化使该次 v4 冻结输入检查无效，平台矩阵被中止；不把这组当作当前通过证书。`body-prototype-runtime/`、`body-release-runtime/` 保留无过渡抬升时的失败；`transition-runtime/` 只用于验证调节方案，最终证书使用 v5 实际重建包。

### 最终实际高度适配与渲染

`final-height-render-v1/results.json` 六项及 `final-height-injured-v1/results.json` 一项全部通过运行、接触、完整蒙皮、发布身份和冻结输入。Default 包括 1.2／1.25／1.5／1.8／2 m 与 1.8 m 移动平台；Injured 验证 1.2 m 的另一支撑手。共 2,520 个观察帧，五组 GPU 场景保存 300 张原始截图。最终渲染明确选择生成 v5 包，不使用原版 High 回退。

| 高度／选择 | 最大完整蒙皮深度 | 最大 wrist 锚点误差 | 完整支撑残差 |
| --- | --- | --- | --- |
| 1.2 m / Low / Default | 1.472104 cm | 0.000284 mm | 0 cm |
| 1.2 m / Low / Injured | 0.863636 cm | 0.000274 mm | 0 cm |
| 1.25 m / High | 1.607502 cm | 0.000401 mm | 0 cm |
| 1.5 m / High | 1.607502 cm | 0.000518 mm | 0 cm |
| 1.8 m / High | 1.607513 cm | 0.000352 mm | 0 cm |
| 2.0 m / High | 1.607490 cm | 0.000495 mm | 0 cm |
| 移动 1.8 m / High | 1.607502 cm | 0.000515 mm | 0 cm |

![最终中高攀爬真实游戏截图](../../artifacts/kimodo_als/mantle-high-20261010-v1/final-height-render-v1/medium-high-contact-sheet.png)

图为实际截图的局部并列，原完整 HUD 和包身份保留于同目录截图。上述通过仍使用原有 2 cm 蒙皮门槛，未进行用户人工节奏／美术验收。

最终 30／120 Hz 各执行 Default 的六个高度／移动案例，加上 Injured 的 1.2 m 单独案例，各 7/7 通过；三 Hz 总计 21/21、8,820 帧，所有输入冻结检查通过。每例观察 6 秒，30／60／120 Hz 分别为 180／360／720 帧。30 Hz Low 两侧峰值为 1.486134／1.023448 cm，120 Hz 为 1.469410／1.515400 cm；全部案例最大峰值约 1.607514 cm，完整支撑残差为零。对应目录为 `final-height-hz30-v1/`、`final-height-hz120-v1/`、`final-height-injured-hz30-v1/`、`final-height-injured-hz120-v1/`。

最终重建后的相关 E2E 实际场景 14/14 完成，包括普通入口／Idle、Low／High 三 Hz 和移动、空中、阻挡、过快目标、销毁、取消，包载荷与设置保持。该局部矩阵未选方向质量全覆盖，因此 runner 的 `directionalQuality` 为 `Directional coverage not selected`，整体仍退出失败；不将 14 个运行断言通过表述为完整矩阵通过。高度适配前的 20 项相关实际场景也均完成且载荷保持，包含原生 Root 开启的四项运行断言。几何结果由专用 runner 单独负责，不能由这些标记替代。

最终标准静态 9/11、平台／生命周期 9/11，两组 `inputsUnchanged=true`。标准生成 Low 的两侧三 Hz、单线程与两侧 GPU 全部通过；Root 开启峰值 4.842937 cm，原版 Low 负对照峰值 2.062774 cm，保持失败。平台组的正常高度／移动及销毁通过；取消后峰值 11.153 cm、关闭手锁的负对照约 4.719 cm，保持失败。完整结果见 `combined-v5/runtime-height-v1/mantle-contact-static/results.json` 和 `mantle-contact-platforms/results.json`，包装汇总为 `runtime_failed`。本批正常中高范围关闭，取消后的全身姿态、Root 开启路径及正式资源闭包仍开放。

### 默认包与普通游戏

`assets/config/als_resource_pack.json` 已选择最终 v5，继续使用既有 `allowDiagnosticPack=true`；这是诊断默认选择，正式生产激活资格仍未满足。旧指针字节备份为 `previous-active-pointer.json`，扫描范围重新收敛到原版／Lyra 与当前活动包。切换发生在标准静态冻结检查结束后，平台 runner 显式选择同一 v5 且不依赖默认指针。

清除全部 `ALS_` 进程环境覆盖后直接运行普通 `scenes/demo/als_demo.tscn` 180 帧，退出 0、无 ERROR，日志明确记录新包和 `replacements=123`，Root Motion 关闭。随后同样以默认配置打开交互游戏，日志选择相同包，窗口存在且响应。见 `ordinary-default-restart.log`、`ordinary-default-interactive.log`、`ordinary-default-launch.json` 和 `ordinary-default-window.json`。显式使用 Dummy 音频，未验收实际音频；窗口启动不作为用户人工动作验收。

未执行本批完整 71 项非道具矩阵、长时间／性能或人工游戏验收；正式生产启用仍要求源闭包与所有既有运行门禁。
