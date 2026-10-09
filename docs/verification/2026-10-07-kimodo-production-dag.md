# Kimodo ALS 生产依赖图实现与受控验证（2026-10-07）

## 本批交付

依据用户要求，把此前逐资产规格落实为可执行的依赖图，而非独立 prompt 批处理。入口为 `tools/produce_als_resources.py`，执行器和数值运算在 `tools/kimodo_pipeline/`，作者配置为 `production_rules.json`。未更改当前 Godot 动画生产树、现有 UE exporter 行为或角色调度，没有提交/推送。

使用和设计见 [脚本说明](../../tools/kimodo_pipeline/README.md)，前置规格见 [生产设计](../architecture/kimodo-als-resource-production.md)。

## 图与方法

默认配置生成 551 个节点，覆盖 253 个原动画身份，折叠为 139 个语义母动作。操作分布：generate 50、mirror 25、freeze 31、offset 14、parameter 15、pack 14、copy 33、loop 26、reverse 1、rest 1、rig 1、deliver 252、deliver_prop 1、external 87。计数只描述当前规则，不是历史固定库存或已生成文件数量。

- 生成节点必须等其参考节点产出，才构造数值 root2d/fullbody/手脚条件；root XZ 原点、heading 和全文姿态的目标不互相矛盾。
- Pose 的 baseFrame 依赖直接落到具体 slot；例如 Rifle AimCrouch 引用 slot 9，不必完成整个表的其他 slot。NoCurves 和原资产身份仍保持完整表引用。
- 左右成对动作通过基底相对的组件旋转残差镜像制作；Crouch_Rifle 只镜像腿部，保留持枪上身。Arms、NoCurves、Land Additive 与母动作共享完整姿态，目标 rig 再施加 layer mask 或求差。
- CrouchToStand 从同一 StandToCrouch 反向派生。Lean/Acceleration、Look/Aim 的参考参数姿态使用程序化、有符号的旋转生成；当前幅度为可调作者初值，尚无枪口/手型/最终蒙皮艺术验收。
- 每个节点缓存身份包含参数、规则、运行时版本、实现代码、依赖身份/文件字节哈希、外部绑定，以及适用的骨架/模型/本地文本编码器权重哈希。损坏缓存不算成功，旧缓存改名留证；每轮运行独立日志和候选 bundle。
- NPZ 是完整 native SOMA77 姿态。候选 package 附目标评价 metadata 和精确加法参考；没有把 SOMA 残差伪装成 UE mesh-space additive，也没有复制旧接触曲线和 Notify 时间。

图产物：`artifacts/kimodo_als/production/graph.json`、`graph.mmd`、`authoring-input-requests.json`。默认 87 个 external 中，86 个是还需明确状态/握点/姿态的 Pose slot，1 个是独立 Bow rig。缺失输入时严格失败，不生成通用 idle 填空。这是全量候选生产目前的明确前置缺口；图与 handler 本身已覆盖这些节点，具体布局/批准姿态尚未完成。

## 骨架坐标修正

实读本机官方 SOMA77：RightLeg 中性坐标 x=-0.10047278 m、LeftLeg x=+0.10043214 m，原 heading 由 `atan2((RightHip-LeftHip).z, -(RightHip-LeftHip).x)` 求得。因此人物自身左侧为 +X，零 heading 前向为 +Z；正 Y 旋转朝左、负 X 旋转抬头。

修正此前规格中左右路径的 X 符号，生成器/清单和生产设计同步更新。镜像本身保持世界 X 反射；目标 UE/Godot rig 的方向转换还需另测，不能把源骨架符号当作所有 runtime yaw 的相同符号。

## 回归

命令：`G:/Github/kimodo/.venv/Scripts/python.exe -m unittest tools.kimodo_pipeline.test_pipeline -v`。

最终 23 项通过，日志 `artifacts/kimodo_als/production-tests-final-v3.log`。覆盖完整身份/拓扑、缺失依赖与环、baseFrame 越界、精确 slot 依赖、同源 Arms/NoCurves/Additive、镜像两次、非对称基底零差、腿部镜像保留上身、方向/俯仰符号、参数中性、fullbody/root 路径一致、非法旋转/骨映射拒绝、float32 加载正交化、缓存恢复/损坏留证与父输入变化失效、未绑定输入失败。另验证独立 prop rig/轨道打包及其引用文件变化失效，不用 SOMA 人体矩阵验证器处理弓骨架。

早期失败保留 `production-tests-v2.log`：测试的路径/方向名假设与实际目录不一致，以及本机 SciPy 1.18.1 的 Euler 批量角度维度要求。修正调用与目录名，没有减小覆盖或放宽断言。

## 真实 Kimodo 与候选包

本机 `G:/Github/kimodo`、RP v1.1、30 FPS、seed 42、100 steps、3 秒候选。推理使用已缓存模型和本地 CPU LLM2Vec；没有切换远程 encoder 或下载另一 checkpoint。

1. `production-live-v2` 实际生成 Stand 与第一轮 Idle；Stand 冻结成为基底。
2. `production-live-v3` 复用该真实 Stand 候选，归零基底朝向后，实际生成带 fullbody 与双脚约束的 Idle，以及带根路径约束的左前走。右前走只经 mirror 产生，未调用第二次推理。另按原身份打包 SecondaryMotion 与基础 Look；该轮 12 个依赖节点完成。
3. 修正数值正交性后，`production-derived-final` 以已生成的三个候选作为明确绑定输入，复验最终代码的派生、打包与 BVH 导出。日志 `production-derived-final-v3.log`；此步没有宣称再次生成 Kimodo 候选。
4. 相同最终命令复跑，12 个节点全部 cache hit、执行 0 个新节点，见 `production-resume-final-v2.log`。

第一次 live 启动用 RP v1 且没有沿用本机 repo cache 路径，模型缓存解析失败。证据 `production-live-v1.log`；改用本机已安装 RP v1.1 和 repo cache，未把失败声明成生成成功。

## 官方 API、FK 与 BVH 独立检查

检查脚本：`artifacts/kimodo_als/validate_production_dag_20261007.py`；结果 `production-dag-smoke-results.json`、最终日志 `production-native-roundtrip-final.log`；此前修复后的 v2 日志保留。

| 检查 | 结果 |
| --- | --- |
| 实例化 Idle 数值条件并由官方 loader 读取 | root2d、fullbody、left-foot、right-foot 均成功 |
| NumPy FK 对本机官方 SOMA FK 位置最大差 | 2.22e-16 m |
| 真实左前走→右前走→左前走矩阵最大差 | 3.89e-15 |
| 冻结 Stand 的 heading | 1.73e-17 rad；XZ 原点为零 |
| Idle 首尾位置与旋转 | 接缝闭合；不代表导数/接触艺术质量通过 |
| Look 中性参数 | 精确匹配 Stand 基底 |
| 官方 BVH→NPZ 回读 Look（31 帧） | 最大矩阵差 4.72e-8，root 差 0 |
| 官方 BVH→NPZ 回读 SecondaryMotion（81 帧） | 最大矩阵差 2.20e-7，root 差 5.97e-8 m |

首次真实重复镜像检查因 native float32 旋转非正交误差累积触发非法矩阵断言，保留 `production-native-roundtrip-v1.log`。修复为加载时先按原阈值拒绝非法输入，再投影到 SO(3)，避免深层 FK/逆运算放大微小误差；未放宽矩阵有效性或镜像恢复断言。

## 开放边界

本批证明依赖调度、参考姿态条件传递、受控镜像/派生、缓存及两项官方 BVH 回读可工作。未生成全量资源，未完成 86 个 Pose slot 的语义/握点布局、Bow rig、全部接触/Sync/Notify/曲线制作、UE 目标 rig 重定向、两种 reader 的原子版本发布、Godot 新套件普通 Demo 人工视觉与性能验收。

所有候选 package 与 bundle 为 `productionReady=false`。没有用模型预测/源骨架数学通过替代目标角色最终蒙皮验收，也没有更改既有原生算法对照阈值。
