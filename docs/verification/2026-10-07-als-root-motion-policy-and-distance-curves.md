# ALS 根运动策略与生产曲线验证（2026-10-07）

## 用户要求与实现范围

用户要求普通 ALS 可配置是否读取根运动，默认关闭；保留可用于后续 Distance Matching 的曲线。随后要求每个含根运动的资产在生产时直接输出独立曲线文件，加入整个依赖生产流程。

已实现 `assets/config/als_animation_settings.json`，当前 `enableRootMotion=false`；普通 ALS 入口读取一次，命令行支持两种显式覆盖。策略从 Core 原始姿态采样、目标适配、Refactored/Mantle catalog 到 Montage 根增量和物理消费贯通。关闭时直接跳过 animated root 轨道，使用 root reference transform；关闭 Montage 提取，翻越复用原时钟及平台程序化插值而不采样根轨迹。Core/Import 读者默认保留原生采样语义，原生对照诊断须显式指定开关。未改变 Lyra、普通角色时钟或事务边界。

`publish stage` 自动在最终目标 root/IK 与原采样时序确定后调用 `root_curves.export`。每资产独立 JSON，使用原动画名；包含相对位移/旋转、解包 yaw、累计平面/三维路程、剩余平面路程及负剩余路程。保留原目标 raw 时序和来源哈希，加入 pack 曲线索引及文件哈希清单。运行关闭根运动不影响烘焙。完整契约见 [根运动设计](../architecture/als-root-motion-policy.md)。

## 本机资源与管线实跑

活动包仍为 `assets/generated/als_nonprop_20261007_v8/resource-pack.json`：183 个非道具替换身份、70 个道具身份保留；新补 34 份符合条件的根运动曲线，815 个 payload。曲线位于 `motion_curves/v4` 和 `motion_curves/refactored`，由原生重定向后的最终目标 root 生成，未误读 SOMA 静态 wrapper Root。

实际执行 `publish bake-root-curves` 给活动包补齐曲线；另外用同一 183 个原生回执执行完整 `publish stage` 到全新 `assets/generated/als_nonprop_20261007_root_curves_probe`，34 个 sidecar 与活动包逐字一致。该 probe 保持 staged、未激活，并用 `.gdignore` 隔离导入。证据：

- `artifacts/kimodo_als/root-motion-pipeline-stage.log`：完整目标发布 stage 成功。
- `artifacts/kimodo_als/root-motion-stage-sidecar-proof.json`：34 个文件全部字节相同、活动包 815 个文件校验。
- `artifacts/kimodo_als/root-motion-curves-repeat.json`：重复补烘焙结果。
- `artifacts/kimodo_als/root-motion-policy-final-integrity.json`：重复烘焙的 manifest 和全部 payload 字节不变；245 个原资产依据文件与合同哈希逐一一致。

仅曲线 sidecar 与版本包清单新增/更新；未重写动画、原配置、原生 raw、FBX 和 native reference。完整生产包装脚本现在自动验证根运动开/关、关闭策略的 idle/站蹲/13 Overlay，以及两模式低/高翻越。PowerShell AST 解析通过。本批没有再次运行 GPU 生成或 UE 重定向；stage 的目标适配、曲线烘焙和发布过程已真实执行。

## 构建与受控回归

使用项目固定 .NET 8：`C:/Users/yuchencui/.dotnet/dotnet.exe`。优化构建 `build GodotALS.csproj -p:Optimize=true` 最终 0 警告、0 错误：`artifacts/kimodo_als/root-motion-policy-build-final.log`。

Python pipeline/publish/root_curves 共 30 项通过：`artifacts/kimodo_als/root-motion-curves-python-tests.log`。新增覆盖折返路程与终点零值、旋转资产/声明根运动但静态的资产、真正原地资产不输出、重复结果一致、不修改源 raw 和拒绝覆盖不同输入。首版真实烘焙曾把 interpolation 当作 raw 字段而失败；修正为读取合同原时序字段，未放宽验证条件。

Core Release 相关 50 项全部通过：`artifacts/kimodo_als/root-motion-policy-core-tests.log`、`tests/Als.Core.Tests/TestResults/root-motion-policy-20261007.trx`。新增两项验证 float/double 采样器关闭 root 平移/旋转但保留子骨与采样时刻，以及翻越关闭根运动时允许空采样器且同一动作时钟仍到达平台。

Import Release 过滤 `AlsMantling|AlsRefactoredDefaultOverlay|AlsRefactoredAdditive`：120 项，115 通过、5 失败、0 跳过。五项均在 `AlsMantlingHostPoseTests` 原输入编译时失败，raw request DefinitionDigest `152e7913…` 与当前原定义 `10c6abed…` 不匹配，binding/player/sample 一致；未进入根运动姿态对照。这与上一非道具批已记录的原包摘要问题相同，此过滤涉及五个依赖该输入的测试，不能与前批不同过滤的失败数量直接比较。证据：`artifacts/kimodo_als/root-motion-policy-import-tests.log`、`tests/Als.Import.Tests/TestResults/root-motion-policy-import-20261007.trx`。原资产没有重写，既有断言没有削弱，不能宣称 Import 全绿。

## 真实 Godot 对照

Godot 4.7.2 Mono，项目主目录，实际角色/骨架、统一帧运行与物理提交；以下是 headless 实例验证，不等于新一轮渲染或人工观感验收。

| 检查 | 实测结果 | artifacts/kimodo_als 日志 |
| --- | --- | --- |
| 翻滚关闭根运动 | 360 帧，动画有效 90 帧，提取 0 帧，actor 位移 `(0,0,0)`，显示 root 无平移 | root-policy-off-roll-v1.log |
| 翻滚开启根运动 | 360 帧，动画有效 90 帧，提取 73 帧，actor 位移 `(-0.044255733,0,-0.13946854)` 米 | root-policy-on-roll-v1.log |
| 普通 idle 默认关闭 | 360 帧，无冻结/求值错误 | root-policy-off-idle-v1.log |
| 普通站蹲/移动关闭 | 1700 帧，standing922/crouching529/air104/sprint103；116 notify 来源、1700 Layering 帧、42 次脚步 | root-policy-off-stance.log |
| 13 Overlay 显式关闭 | mask8191，1 owner，Parallel360 帧，2 次取消、1 次 commit hold | root-policy-off-overlays.log |
| 低翻越关闭/开启 | 各 360 帧，active59，启动一次，结束动作并落在平台 | root-policy-off-mantle-low.log / root-policy-on-mantle-low.log |
| 高翻越关闭/开启 | 各 360 帧，active97，启动一次，结束动作并落在平台 | root-policy-off-mantle-high.log / root-policy-on-mantle-high.log |

翻滚资产存在折返轨迹，净位移很小；该对照证明开关确实控制提取和消费，不据此声称实现理想前滚距离或已通过翻滚观感验收。低/高翻越在关闭模式通过程序化平台插值，开启模式使用根轨迹，动作时钟没有分叉。

最终 `root-motion-policy-runtime-verification.json` 汇总 10 项检查，并复用前批 `non-prop-v8-p2b.log` 的传输证明，因为对应资源字节未变；不是本批重跑 P2B。`publish activate` 已验证这 10 项日志及全部 payload，将当前包验证记录更新；指针仍指向 v8，`runtime_validated`、`manualAcceptance=false`。

## 尚未验收

Distance Matching 当前只有曲线数据，未实现运行时距离反查、播放控制或状态切换。纯最终原地步态没有有效 root 距离；若后续匹配其作者行进速度，须另存移除行进前的 master 轨迹。旋转与竖直运动不能直接套用平面剩余路程。

跨动作切换的 foot contact 汇总仍有较大漂移峰值，本批未以根运动开关消除此质量问题，未放宽既有 native/接触阈值。完整动作观感、滑步、地形人工验收、长时多角色性能、整套 native 对照和新 GPU/UE 全量重产未执行。活动包仍不具有人工验收证书。

保留用户既有脏工作；未提交、未推送，未创建 worktree。
