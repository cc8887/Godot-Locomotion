# Lyra Main 活动 Slot 姿态接入（2026-10-02）

本批完成五个实际 Slot 位置的姿态组合，并接入 `LyraMainPoseHost` 的同一候选事务。FullBody 完全覆盖时跳过底层 Locomotion、LeftHand、Aiming、Recovery 求值，最终 SkeletalControls 仍执行，最终曲线反馈仍可提交。只关闭该宿主接入和受控叶子下的联合原节点姿态门禁；普通 Demo、完整 Main 原生轨迹及整个迁移目标未关闭。

## ALS 资源和 Animation Interface / Layer

继续使用现有 ALS Mannequin 模型及 68 根蒙皮骨。Lyra 源资源在 GASP58 经 IK Retargeter 转到 ALS；同一个逻辑资源布局补齐 `weapon_r`、weapon-space 虚拟骨等控制通道，共 81 个动画通道，最终回写 68 根 skin 骨。Main/Layer/Montage、曲线、整数骨属性、RootMotion 属性共用布局。原 Manny 多出的 spine04/05 不能按数组序号套用：现有 Warp 显式映射和原骨名遮罩已有局部原生对照，但完整握持、视觉比例和地形仍待验收。

当前 Interface 已读取原编译签名，校验 11 个 class 的契约、14 个实际调用入口、输入 pose 和 Aiming double 参数，保留 `ItemAnimLayers` 组。同组入口由一个 `LyraItemLayerGraphInstance` 持有；Main 调用 typed 入口并统一收集源和执行一次 Sync。输入和输出携带完整 pose/curve/attribute/root 数据及 owner/候选/epoch 身份，不能将 Layer 简化成播放同名动画。Update、延迟 cache、Evaluate、候选提交和取消分别保留。最终只有角色外层发布 skin pose。

后续换类要在角色物理边界切换完整 Provider 及其资源/播放器历史，遵循已验证的同类复用规则，绑定新 epoch 并拒绝旧候选。同一角色的物理 Montage bank、通知路由、惯性请求和最终反馈必须随各 Layer 统一协调。当前完整活动槽宿主的内部身份仍沿用 character0/generation1；本批没有完成生产换类或多角色绑定。

## 实现

- `LyraMainSlotComposition` 组合原 UpperBody81、UpperBodyAdditive71、FullBodyAdditivePreAim2、AdditiveHitReact74、FullBody84。槽源只有 SourceWeight 相关时才调用，不引入新时钟。
- Main/Provider 三个 owner-scoped cache 保持 181→78→83 的完整通道数据，生命周期按每次 Evaluate 重建；重复求值不复用上一轮输出。
- 原 Dynamic ApplyAdditive3 使用 additive identity RefPose79，实际蒙太奇 additive 叠加到该输入，再与 Locomotion base 合成。原 LayeredBoneBlend0 按 ALS 骨名映射 mask；Recovery ApplyAdditive76 固定 0.65f，RotateRoot72 位于 FullBody 外层，保留不旋转 RootMotion 属性的原设置。
- Evaluate 按原 LayeredBoneBlend 的 base→dynamic additive→upper 顺序取输入；原 Update 仍按 upper→base 顺序遍历。
- `LyraLocomotionResources/Catalog` 可显式加载 Montage 扩展，原默认调用保留既有资源布局。活动宿主的 300 源属于同一个不可变 bank。
- `LyraMainPoseHost` 可绑定实际物理 runtime/catalog，Prepare 使用冻结 frame，Evaluate 调用真实 LeftHand、Aiming、Additives、Skeletal 入口。最终曲线经过 enclosing Slot 求值证明后进入角色候选；无求值、重复反馈、错误 owner/frame、未反馈提交仍拒绝。
- FullBody 全覆盖允许无任何 Locomotion source 求值的最终反馈。Source scope 必须接收明确的根集合，逐个校验所有实际选中的根；旧路径的反馈门禁没有绕开。

外部物理 bank 与 Main 必须共同预校验；Main/Slot 先提交，再提交 bank。取消同时撤销两者，动作命令不改写本帧冻结数据。

## 联合原生探针

`AlsLyraMainSlotCompositionV2Library` 在实际临时 GamePreview、真实组件及原 Main class 上执行原五个 Slot、ApplyAdditive3/76、LayeredBoneBlend0、RefPose79、RotateRoot72 和 Save/UseCachedPose。Aiming 是明确的 passthrough 分支；Locomotion 是实际 ALS81 Montage track 的受控时间输入，Recovery 是真实 ALS81 additive Sequence。主状态机、完整 Linked 源注册/Sync、Main 惯性75、ControlRig73 和角色物理不是此探针的范围。

每帧 Montage 权重、advance 和 freeze 只进行一次。实际 `ParallelEvaluateAnimation` 创建 Engine cached-pose lifetime，同帧两次独立 Evaluate 的完整输出一致。三个 Provider×三个频率的正常轨迹及零/负 delta、时间跳变等计算边界共 47,610 帧，采集 19,944 个输出。实现每帧取消重放，再对两轮 Evaluate 共 39,888 个输出逐通道比较。

V2 在换 ALS Skeleton 后显式调用原 Proxy `InitializeObjects`，并门禁 Proxy Skeleton 必须等于目标；每帧原 LayeredBoneBlend 当前 81 条权重必须等于按骨名生成的原 mask。两个独立 UE 进程退出0，第二次输出 JSON 语义与首次相同。各818条既有GameplayTag、Editor和 transient压缩相关 Warning，0 Error。原664包、776个既有JSON字节SHA保持，所有旧探针及外部 source/package 镜像SHA保持。只使用外部导出插件，没有保存 uasset、修改 Engine 或部署 GASP58 插件。

## 验证

| 范围 | 结果 |
| --- | --- |
| 联合姿态 | 39,888次完整比较、3,230,928骨、48,396曲线、149,712整数属性；presence/flags/整数和曲线 float 逐位同 |
| 精度 | P最大1.2347916251785851e-13 cm，Q最大1.2177709378935644e-15，S最大3.8459253727671276e-16；原门槛 P1e-8/Q1e-10/S1e-12 不变 |
| RootMotion属性 | 35,952次存在性/TRS比较，P/Q/S差0；Recovery 原单位根属性也参与 additive |
| cache/事务 | 53,928次实际 cache 求值，1,968次输出无 Locomotion 求值；47,610帧取消重放、138次故障注入、19,944次异代拒绝 |
| 实际 Main 宿主 | 40,320物理帧、39,375姿态、354,375最终反馈曲线；10,269 FullBody完全覆盖帧用独立 FullBody→RootYaw→Skeletal 链完整对照 |
| 真实 Main 源 | 11,214 Locomotion隐藏、29,106更新；105,961播放器登记，45,267 Aiming与29,680 Additives实际更新 |
| 宿主事务 | 每帧完整取消重放、810次重复求值、101,988次坏提交/旧候选等拒绝；18次空 Montage bank 与原无活动槽宿主完整比较 |
| 回归 | Main缓存2,520帧，原实际最终反馈11,340帧/9,762姿态；活动Slot联合Update47,610帧/580,701分派及真实更新宿主40,320帧 |
| Core | 动作生命周期/Montage/Mantle相关243通过，0失败/跳过 |
| 构建 | Debug、ExportRelease Optimize均0警告0错误；两配置实际运行联合姿态和40,320帧宿主测试，Godot无错误警告 |

Optimize实际加载三个ExportRelease DLL，结束恢复原六个Debug DLL/PDB并逐文件SHA验证。`tools/verify_lyra_main_slot_pose.py`核对原生依赖、包/探针/镜像、目标 mask、实际退出、构建和TRX，生成 `artifacts/lyra-analysis/lyra-main-slot-pose-verification.json`。

## 失败证据与保留边界

首轮受控 Recovery 输入只复制了骨/曲线/整数属性，遗漏真实 Sequence generated RootMotion，导致首帧根缩放属性不符。按已有独立压缩根数据补齐 fixture 输入后，根属性通过。随后 V1 在 frame473 首个上身动作暴露 Q≈0.0208245 差异；独立 diagnostic 捕获确认 Slot0、Slot1、cache83、RefPose79 和 Recovery 均一致，只有 Split 输出不同。V1 换 Carrier Skeleton 后未刷新 Proxy 骨架，旧 Manny 与 ALS mask 身份不匹配，实际得到零上身遮罩。V2 修正探针初始化并加入目标和每骨权重门禁，完整姿态通过；没有改动已有混合公式或放宽门槛。

V1、diagnostic 和其代码/JSON均保留且不可覆盖。V1 的19,944输出不是验收 oracle；唯一接受的联合姿态门禁是V2。初编译、根属性、上身差异等失败日志保留在 `artifacts/lyra-analysis/`，包括 `main-slot-composition-godot-root-fixture.log` 和 `main-slot-composition-godot-stage-debug.log`。

本批实际宿主使用既有玩法观察轨迹循环，与原64秒物理动作轨迹共同运行，不读取原生机器/播放器/Sync输出作实现。FullBody覆盖对照是已验证的独立组件组合，不是完整Main原生oracle。FootPlacement设置关闭，尚无本批真实地形/渲染验收。

下一步 Main 惯性75及ControlRig73、缓存初始化/重入整体生命周期、完整原生源/Sync/Notify联合时序、Notify/Montage/root物理消费、生产Provider换类/多角色身份和普通Demo。渲染、性能、完整人工矩阵及整链验收仍开放。用户未提交内容保留，没有提交/推送，整个目标继续保持进行中。
