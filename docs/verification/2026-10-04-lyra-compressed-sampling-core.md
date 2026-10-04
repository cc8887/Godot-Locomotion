# 压缩键采样与 ALS 帧时间复用

沿用当前 locomotion、ALS人物/骨架与Pistol/Rifle范围。本批将原`LyraCompressedRootBank`中的引擎采样算法移入纯.NET Core，继续使用原资产与既有RootMotion区间策略。

## 实现

`AlsCompressedTransformTrack`保存独立复制的decoded translation/rotation/scale键和稀疏帧表；采样不持有角色时钟。它处理原均匀/稀疏选帧、Step/Linear、float向量插值、最短路径四元数符号，以及PerTrack双精度归一化和其它编码的float归一化区别。它没有实现压缩字节解码；现有UE导出键由Lyra JSON适配器加载。Core不固定Root骨或Lyra资源地址。

`AlsAnimationFrameTime`提取原ALS帧/子帧转换，保留carry、MaxSubframe及OptimizedCancellation/RoundSubframe两种已有策略。原ALS raw pose selector、raw absolute Root sampler和新compressed track调用同一个转换；各自原先的选键、Step与边界策略保持独立。没有用raw姿态的double混合替换压缩键的float插值。

Root sampler继续复用`AlsRawRootMotionIntervalSampler`的正向/反向/循环/保留时钟策略。原ALS raw Root与压缩Root的参考逆变换共用已有`AlsPrecisePose.Inverse`，原local/parent组合顺序和scale策略保持。`LyraCompressedRootBank`保留Godot文件读取、JSON/schema/codec校验、资产路径到slot绑定及资源库生命周期。

## 验证

Core78项通过，新增19项覆盖均匀/稀疏/Step/边界、四元数最短路径与编码归一化区别、数据防外部修改、正反向与循环Root、参考/动画scale策略、非法定义/时间、负帧/子帧舍入和carry；包含原raw pose、Root interval、Mantle Root与Montage Root回归。0失败/0跳过，Debug/ExportRelease v2构建均0警告/0错误。

首轮78项中有1个新增测试预期错误：reference scale2、动画scale归一化后，原组合顺序令最终relative变换将20单位差值放大为40。核对原Root实现和现有Compose/Relative顺序后修正该测试，生产公式与门槛未改；v1失败TRX/日志保留。

Debug与实际Optimize分别完成原RootMotion189源/3024区间/3528帧、起步3780帧、Pivot3780帧/378精确压缩Root probes、Montage采样15870帧/60轨道/298 Root checks；这些Root position/quaternion/scale差均0，原精度检查保持。Main composition3780帧与完整Main＋Rig7560帧/7296姿态、原ALS普通1700帧、十角色480帧/4800最终蒙皮发布通过。

两构建的真实Jolt地形30/60/120Hz分别450/900/1800帧、每帧两角色，每构建共6300最终蒙皮发布；台阶、斜坡、落差、跳跃、站蹲、ADS与两武器切换覆盖通过。十角色和各频地形完整报告在两构建及前批相同，包含逐帧数据；不是只对比摘要或覆盖计数。

最终`artifacts/lyra-analysis/compressed-core-v2-audit.json`通过：Core78/新19、22个Godot成功进程，8份本批源码与4648份保护基线保持冻结哈希，前批证据保持；两轮Optimize运行后六份Debug DLL/PDB恢复。实现、构建、测试和运行均为v2。v1失败证据另行保留。

本批没有UE启动/构建/保存/新导出、资产JSON格式化、提交推送、新GPU、全量managed、十分钟、性能、跨平台或实体键鼠验收。之前computer-use窗口激活两次遭Windows `GetCursorPos 0x80070005`拒绝；没有据此宣称键盘故障，也没有发送实际键鼠输入。本批地形使用进程内逻辑输入，实体键鼠仍待验收。

仅关闭本批引擎采样与ALS公共转换复用；任意压缩字节codec、URO/全UE调度仍不在当前交付范围，完整目标及其它明确验收项保持开放。后续Core审计已定位Rig输出PoseAdapter及单父ParentConstraint；应先检查现有精确姿态/层级能力，保留Lyra资源布局适配，再迁移其中通用机制。
