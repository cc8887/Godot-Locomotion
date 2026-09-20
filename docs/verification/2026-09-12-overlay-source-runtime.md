# Overlay 完整来源绑定与原生采样对照

日期：2026-09-12。移植完整性推进第一百一十三批。

## 已实现的范围

`AlsOverlaySourceCompiler` 已编译实际 ALS V4 Overlay 的全部 148 个独立来源，
包含 122 个 SequenceEvaluator、26 个 SequencePlayer，绑定 29 个直接资产。
五个原生状态机的 26 个状态（含一个 conduit）与完整来源所有权一并保存。
源数据还保留 50 条实际转换边，转换规则与姿势图执行留待下一步实现。

主状态来源闭包：Default 6、Binoculars 14、Torch 13、Box 7、Barrel 4、
Masculine/Feminine/Injured/Hands Tied 各 6、Rifle 25、Pistol 1H/2H 各 18、
Bow 19。conduit 不拥有播放器。子状态机为 Rifle、Pistol 1H、Pistol 2H、Bow，
各三状态。每个主状态来源只归属一次，编译节点身份不按资产名合并。

求值器保留原图固定秒数，12 个动态输入读取 AimSweepTime。播放器保留
SecondaryMotion 19 个、IdleAdditive 4 个、Locomotion 3 个的分组/角色。
后三个步枪手臂节点是 AlwaysFollower，原版 PlayRate 就是 0，时间必须
来自移动同步组；不能将零倍率判错或改成 1。本批采样器不推进这些时钟，
由后续统一来源更新/同步所有者提供时间。

`AlsMovementGraphDefinition` 在正式加载时编译上述定义并加载 Overlay
原始数据；`AlsOverlayAnimationSourceSampler` 已可实际采样每个来源的
79 骨姿势和曲线。29 个直接资产递归解析后共 36 个来源，包含 7 个附加
基准依赖。与移动库重叠的 ALS_N_Pose 及相同逻辑骨架复用对象，其余
35 个资源补入 Overlay 库。采样 scratch 按角色独立。

资源库新增“仅复用重叠资源”入口，保留原“必须完整包含”的严格入口。
重叠资产仍检查身份、文件名、SHA256 和完整骨架/重定向策略；相同 ID
但内容不同会拒绝。原始导出在共享 raw_sequences 文件已存在时核实其
字节相同后复用，不覆盖不同内容。

## 原生数据与文本差异

`export_overlay_inputs.py` 读取真实编译后的状态机、来源策略及 82 张图，
正式数据绑定现有 v4_layering_inputs 的 SHA256。没有保存 UE 资产。

首次严格文本检查发现 UE 会重建未连接引脚的 GUID；普通 Editor 还会
清理 Rifle Relaxed 中未连接的 BreakVector/BreakStruct 纯计算节点。
指纹算法只规范这两类差异：未连接且无父/子引脚的 ID，以及没有任何
连接/执行引脚的这两类纯分解节点及其 Nodes 数组引用。剩余数组顺序、
已连接引脚 ID、互连关系、参数、类型、资产引用全部保留。层级按完整
对象路径处理，不因不同图里节点同名而删除其他节点。

修正后冷启动和普通 Editor 的正式 Overlay JSON 字节完全一致。
第一次普通 Editor 虽退出 0，但脚本失败且没有结果文件，未当作导出成功。
原失败日志保留；第二次普通 Editor 实际退出 0 且有完整输出。

正式数据哈希：

- v4_overlay_inputs.json：
  `C5F65B0098023750545AFCCEDFFD76B447CA2E9EECAC37E2E12BCFD0DAD69F22`
- v4_overlay_source_inputs.json：
  `E42B60212BB83D917BF6BEB8A34217B3463A6B5CD71E50F421850C5A32696F04`
- v4_overlay_node_pose_native.json：
  `11BD1D6C9412B465715C51A5713429375F7C2FF43865038D6CA9C7BA0E06930A`

`export_overlay_sources.py` 复用已有原始轨道/附加采样导出器，并独立从
原图引脚读取各节点的固定时间，生成 444 个真实 UE GetAnimationPose
查询。覆盖负时间、分数时间、越界时间及固定姿势，不用 Godot 输出
生成期望姿势。源码策略、骨架、虚拟骨、曲线和附加基准均来自 UE。

## 验证结果

- `overlay-source-import-regression.log`：215 项通过、1 项既有
  LayerBlending 普通 Editor 专项跳过。新增 13 项检查覆盖完整来源、
  时间输入、零倍率跟随者、所有权、连接/回调/同步参数变化拒绝，以及
  部分资源共享和内容冲突。未把跳过项称为通过。
- `overlay-raw-source-smoke-first.log`：36 资产、848 原始姿势、66,992 骨，
  最大位置误差 6.279325e-7 米、四元数分量距离 2.5691003e-7。
  四所有者单/并行各 17,280 次采样位级一致，2,304 热采样分配 0。
- `overlay-additive-source-smoke-first.log`：13 个真实附加资产、676 姿势、
  53,404 骨；位置误差最大 8.4818896e-7 米，四元数距离 5.0118655e-7。
  四所有者单/并行各 17,280 次采样一致，2,304 热采样分配 0。
- `overlay-node-source-smoke-first.log`：全部 148 节点各 3 个查询，共
  444 姿势、35,076 骨；位置误差最大 8.4818896e-7 米，四元数距离
  4.6767587e-7，曲线误差 0。全部查询重试一致，四所有者 592 节点
  并行结果一致，10,000 热采样分配 0。
- `overlay-source-base-layer-regression.log`：BaseLayer 3,360 帧、302
  隐藏帧、12 次晚期失败回归通过。
- `overlay-source-production-single.log`、`...parallel.log`：正式完整
  移动 Worker 各 600 帧通过，结果 EAAF62E4D0A80A76、完整姿势
  3103E3B355BF1F3B、Root A4F6C26CBAB8A0E7、事件 28，lag/stale 0，
  保持前批基线。这证明加载新增数据未破坏原路径，不证明 Overlay 已参与输出。
- `overlay-source-runtime-build-2.log`：Godot 构建 0 警告、0 错误。

本批复用现有 UE 原生插件接口，没有修改 C++/插件配置；按
ue-diagnosing-plugin-build-load 做运行前项目插件审计，三个插件均 PASS，
随后完成冷启动读取和普通 Editor 重复导出。普通 Editor 的既有告警仍保留。
本批没有重复构建/打包未修改的原生插件。

## 尚未完成与下一步

已完成 Overlay 全部来源的正式绑定、原始数据、采样组件及加载。
尚未实现 Overlay 五个状态机的完整更新/转换、跨图同步组接入、完整
姿势图（包括 BlendList、ModifyCurve、惯性化），因此最终 Overlay 输出
仍未接入 Demo。这里不是 P5B Overlay/道具玩法完成，也不是完整上身验收。

继续上述 Overlay 更新与姿势执行，再将 Aim、Overlay、BasePoses 和
LayerBlending 接成正式最终层，闭合脊柱/手部修正、最终曲线反馈与完整
Foot IK/Foot Lock/pelvis/平台。随后用同输入、同脚相位多帧及人工对照
验收手臂、侧身、换髋和起步滑步。P5A–P7 范围保留，音频暂缓。

前批 P4 matrix 多线程 p95 2.559 ms 超过 2.5 ms 的未通过项仍保留，
本批未做性能优化或重跑来关闭它；P7 十分钟预算仍未完成。
未 commit/revert/merge，保留工作区既有改动。
