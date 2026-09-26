# 统一移动观察与真实组件惯性

基线 `main / d201e93`，直接在统一主目录实施。用户 README、普通场景、漫游角色、HUD、LayerBlending 等修改保留，不纳入本批提交。

## 实现与来源

本地 `AlsCharacter.cpp` 的 RefreshInput/RefreshLocomotion 与 `AlsAnimationInstance.cpp` 的移动刷新区分两种判定：HasVelocity 为水平速度至少1 cm/s；Moving 为有输入且有速度，或速度严格超过角色阈值；MovingSmooth 使用相同前项和另一独立动画阈值。Demo 原来分别借用旧 movement.IsMoving/ShouldMove，脚锁则独立使用硬编码默认值。

只读导出原角色 CDO 的设置绑定和网格属性：实际 Moving 为50 cm/s，既有动画设置的 MovingSmooth 为150 cm/s，TeleportDistanceThreshold 为300 cm。新 JSON 绑定既有动画设置的字节级 SHA256；Import 验证源路径、类、哈希和有限非负数值。

| 输入 | 当前生产者 | 消费者与提交边界 |
| --- | --- | --- |
| Speed、HasInput、HasVelocity、Moving、MovingSmooth | 同一 AlsFrameInput 的实际水平速度、已解析 InputDirection 和原设置 | Demo Begin 一次捕获；Locomotion/Rest/QuickStop 与 global 脚锁共用，带角色/代/帧身份 |
| RelativeLocation | 已着地、有效平台编号和 collider 身份 | 原 Locomotion 输入；不再固定 false |
| ComponentToWorld | 本帧实际 FBX 组件世界变换 | 同足部坐标转换到原生世界厘米，传递各层姿态惯性；不再固定 Identity |
| AttachParent | UE 对应语义为网格所属 Actor 的 GetAttachParentActor | 当前普通角色无 Actor 附着，传0；不能用网格父节点或脚下平台身份代替 |
| TeleportDistance | 原角色 CDO 网格300 cm | Standing/Crouching/Grounded/Locomotion 的惯性入口 |
| 已提交观察 | 主图与脚部各自候选 | 成功提交才发布诊断，取消不覆盖；实际 Demo/并行测试逐帧检查两者完全相同 |

`AnimNode_Inertialization.cpp` 的原生 component/Actor attachment/teleport 来源已核对。原脚部的 FBX→native 转换提取为共享函数，算法未另造。保留未使用新宿主的旧诊断脚部 fallback；旧生产外层惯性仍在，不宣称本批去除了双层迁移边界。

## 验证

产物目录 `artifacts/tests/refactored-locomotion-observation/`。

- Optimize 构建0警告0错误。
- Import 相关55项通过，含独立/共享 Standing 原生六组，原精度阈值未改。最后加强候选观察取消/提交及瞬移清历史断言，新3项再通过。
- Core 最终14项通过：阈值严格边界、忽略垂直速度、输入/平台身份、非默认配置、组件轴转换，以及既有全精度惯性原生/生命周期回归。初次 Core 编译使用不存在的 Quaternion.Rotate，改用已有向量 Rotate，失败记录保留。
- 新惯性阶段测试对照 Core，覆盖组件平移/转向、Actor附着切换、阈值关闭与300 cm、500 cm跳变、重复求值、取消/重试。完整 Locomotion 宿主三频率共2520帧逐帧重试，加入上述组件/附着输入。这是宿主接线回归，不是新 UE 完整宿主 oracle。
- 普通 Demo 30/60/120 Hz 分别850/1700/3400帧通过；Moving与MovingSmooth不同的帧数为7/14/28。每组4次Pivot、4次实际Rest补步，Details mask63。
- 十角色 single/parallel 各3621帧，2次取消/1次提交保持，3101帧 RelativeLocation，931帧最终接触、991帧脚趾接触。姿态 `9A31A48E3401F933`、根运动 `245AD6A473658AC7`、结果 `16AEC86EE70EA095` 摘要完全一致。平台是静止的 AnimatableBody3D，只证明真实平台身份和相对分支，不证明平移/旋转平台连续跟随。
- 普通相机/Ragdoll/Get-up 60 Hz、480帧通过，含第一人称与恢复。最终Godot日志无错误、警告或异常。
- 实际渲染60 Hz、1700帧退出0，保存35张截图。检查255、470、790、1128、1140、1560帧，未见所检查区域姿态爆散；蹲姿瞄准镜头仍裁腿，不能验收该阶段脚部观感。未测支撑窗口滑移或完成用户人工验收。

## UE 导出与构建门禁

按 UE 插件构建诊断 skill 执行完整 Editor 目标及四插件审计；0 actions，BuildId `2192dbcd-0924-430b-9a3d-1daff6c15a77`，fingerprint `4CE25A651115C8ABA1D5AA81FCFC0A97C638C958D714D73DABA212A75B25F83E`。日志前缀 `Saved/Logs/PluginBuild/20260926T091128823Z-9a92cd3374e14dbe8601ee4c9f7b35a0`。

最终冷导 PID45208 和普通 Editor PID32344 均退出0；`ue-cold-component.log`、`ue-editor-component.log` 及正常导出产物保留。两份最终 JSON SHA256 均为 `2465E6679C0649D9E8351EAE55E95A852D684466B9722B1C29B8FFE457B653C0`。初版尚无Teleport字段的导出日志另保留，不作为最终证据。

普通 Editor 仍有两条既有 `Condition failed` 和五条旧警告，未修复，不宣称 UE 日志无错误。本批仅增加只读Python导出，没有修改 UE C++/插件配置、保存资产、重跑DataValidation或打包；没有新增完整角色原生轨迹。

## 剩余边界

输入和 ActualAcceleration 的完整原生历史、平台相对速度/旋转、源Notify/NotifyState/Sync消费者以及旧兼容更新移除仍待；随后补 Crouching/Grounded/Locomotion 的连续原生对照、原上身/Overlay/最终脚部、通用动作/Mantle/Root Motion、物理恢复/相机完整矩阵及十分钟性能和可复现资产交付。R2–R7 未关闭。音频、道具物理与头颈专项继续暂缓。
