# Godot ALS 实施路线

更新：2026-09-26。代码基线：`main / 4a7208c` 加本批角色共享动作与 Transition 边界。唯一开发主目录：`${env:GODOT_ALS_ROOT}`。

## 目标与状态口径

交付使用 UE 动画、角色及道具资产的稳定 Godot ALS 示例，保留已经可用的键鼠体验，完成原规划 P3–P7。范围包含站立/蹲伏/步行/跑步/冲刺/跳跃/下落/落地、三种旋转模式、AimOffset、上下身分层、Turn/Rotate in Place、动态过渡、完整脚部、13 种 Overlay 与道具玩法、Notify/Sync/ActionPlayer、Mantle/Roll/Root Motion、Ragdoll/Get-up/Pose Recovery 和完整相机。音频、道具物理及头颈拉长专项按用户要求暂缓，不能从待办中消失。

本文件作为后续统一 roadmap。原始约束见 [总体设计](docs/superpowers/specs/2026-08-25-godot-als-port-design.md)，旧批次历史见 [完整性执行路线](docs/superpowers/plans/2026-09-12-port-completeness-execution.md)。桌面原方案未包含全部 Overlay，但用户后续已扩大范围，以后续要求为准。

每项分别登记：资源齐全、组件实现、原生对照、生产入口接入、人工验收、性能验收。组件测试通过不代表完整链路完成；历史场景通过也不代表新宿主通过。

**当前有两条必须区分的路径：**普通 Demo 已有完整分层、脚部、Overlay/道具、Roll、Ragdoll/Get-up 和相机等接线；新 Refactored Standing 宿主仍是独立子图，尚未替换普通 Demo。不能把“新宿主尚未接入”写成“普通 Demo 完全没有这些功能”，也不能把旧入口已有功能写成新链路已验收。

## 剩余工作总表

| 阶段 | 当前证据与缺口 | 下一交付与关闭条件 |
|---|---|---|
| R1 Standing 严格对齐 | 已关闭本批门禁：三频率 2310 帧/140067 骨骼求值全部通过；Parent 已比较字段和时钟差 0 | 保持既定阈值作为后续角色整合的回归；不代表完整角色或普通 Demo 验收 |
| R2 角色级动作与 Transition Slot | 已有共享 Grounded 动作资源/ID/bank/队列、Standing 注入及原 node13 Slot；受控输入真实混合通过；完整 Grounded 上游和新整链原生轨迹待接 | 扩展完整角色资源、按原位置接 Grounded→Transition→Locomotion，并完成整链原生对照 |
| R3 完整主移动图 | Standing/Crouch/空中等已有不同程度组件及旧入口实现，尚无新角色宿主闭环 | 真实 Standing/Crouching/Jump/Fall/Land 与姿势切换，缓存/Sync/曲线反馈统一 |
| R4 上身、Overlay 与最终脚部 | 13 Overlay 等已有受控组件证据；不能代替新全角色连续证据 | Aim/Layering/手部/脚部按原图拓扑接入，换向与支撑窗口、地形/平台通过 |
| R5 Godot 普通入口 | 新宿主未接现有 Gather/Worker/Commit | 普通入口实际使用新宿主，单线程/并行一致，键鼠、多帧和人工通过 |
| R6 通用动作与物理恢复 | Roll/Ragdoll/Get-up/Camera 等已接旧入口；Mantle 有组件；长期物理稳定性尚有失败 | 通知、Root Motion、Mantle/Roll、Ragdoll/Get-up/Pose Recovery 与新链路整合并验收 |
| R7 最终交付 | 十分钟预算、完整人工矩阵及可复现交付未完成 | 原性能目标与所有功能门禁通过，生成资产交付可复现 |

R1 证据见 [Standing 曲线精度修正](docs/verification/2026-09-26-standing-curve-precision.md)：最大位置差约 9.73409e-6 cm，最大曲线差 3.57628e-7，原三项严格失败已通过，未放宽阈值。此前失败保留在 [原生连续首轮记录](docs/verification/2026-09-25-refactored-standing-host-native.md)。R2 首批见 [角色共享动作与 Transition 边界](docs/verification/2026-09-26-character-actions-shared.md)，整阶段尚未关闭。

## 完整角色动画链路实施方案

### R1：先关闭 Standing 已知原生差异

- [x] 按相同输入、资源哈希和更新顺序定位首次分歧：Parent 步幅曲线先差一个 float ULP，播放时钟随后累积偏差；另有源动画曲线误用双精度采样。已按本机 UE 源码和实际二进制的运算顺序修正，无需改变状态混合或 119/118 惯性算法。
- [x] 覆盖 30/60/120 Hz、update-only、失去相关性与重入、取消重试、重复求值；保持位置 2e-5 cm、旋转/缩放/曲线 2e-6 的本批门槛，不用放宽阈值关闭失败。
- [x] 原三频率连续测试通过，1238 个原生移动设置曲线样本精确一致；受影响组件、生命周期和构建结果见本批验证记录。关闭范围仅限这套受控 Standing 原生门禁。

### R2：把局部 Standing owner 提升为角色级 owner

- [ ] 扩展不可变角色 profile 到完整角色：本批已统一 Grounded 的资源 ID、79 骨父序、曲线映射及 Montage group/slot，并共享武器过渡资源；Mantle/Roll 等动作及其他图资源仍待纳入。V4/Refactored 边界必须显式绑定，禁止按序号混用。
- [x] Standing 支持角色注入的共同 bank/queue，并保留独立测试入口；原局部 18 个资源显式映射为角色 ID。子图不再推进或提交共享 bank，实际播放实例及其时间/遍历历史由同一物理 bank 所有。
- [ ] 原 AB_Als 外层 Transition Slot node13 的位置、编译/编辑图链接及策略已校验；Stop/Dynamic/QuickStop 的真实姿态和曲线已在受控 Standing 输入上混合，包含重叠、淡入淡出、停止及重试。**原位置是 Grounded→Transition→Locomotion，不能跳过 Grounded 直接宣称新角色整图完成**。待接真实 Grounded 输出、外层惯性请求接收与新的整链原生轨迹。
- [x] 当前 Grounded 动作共享帧统一预校验/提交及主线程后处理顺序；冻结播放快照不受新播放改写。覆盖停止阻塞保留请求、武器 worker/main 请求、实际 Turn/Stop/QuickStop、取消重试及晚期故障。完整角色其余消费者接入时须继续扩展该门禁。

交付：角色资源绑定、可组合子图 owner、真实外层 Slot 及原生连续对照；不能以 bank 中存在实例作为姿态已接入的证明。

### R3：接完整主移动图与 Parent 反馈

- [ ] 用已导出的原始图/规则补齐 Crouching 的状态、播放器、缓存、回调、惯性化；复用公共算法，保留独立节点身份和资源配置，不能复制 Standing 数值代替。
- [ ] 组合 Standing/Crouching、Grounded、Jump/Fall/Land 与各自 Lean/预测落地数据，按原图处理初始化、相关性、缓存最大权重路径、Inactive 和 Sync。移除测试用固定 Grounded/Standing 基底，改为真实子图输出。
- [ ] 区分原始输入、Parent 更新状态、图内曲线、最终输出曲线及下一更新读取的已提交历史。Moving 与 MovingSmooth、FootPlanted、FeetCrossing、HipsDirectionLock、YawOffset、SprintBlock 等逐项建立生产者/消费者和帧时序表。
- [ ] 将源 Notify/Notify State 与生成状态通知接到通用队列及类型化消费者；用实际通知替代宿主测试中的显式 ActivatePivot 输入。保留存在性、事件顺序、持续区间、停用/销毁结束语义，只有成功提交才派发副作用。

交付：同一角色输入驱动的完整 locomotion 输出（姿态、曲线、播放观察、事件、根运动候选）；站蹲、跳跑、落地和换向连续 UE 对照。

### R4：接上半身、Overlay 与完整最终姿态

- [ ] 按原图拓扑连接 13 个 Overlay、动作分支、AimOffset、局部/网格空间加法、Layering、脊柱和手部约束。重新核实原有组件的受控输入，在完整角色中提供真实曲线和源姿态。
- [ ] 绑定 Overlay 装备/切换/收起、道具挂点及弓曲线；普通持物参与验收，道具物理暂缓。最终姿态与 attachment 必须来自同一已提交角色帧。
- [ ] 接入 Foot IK、Foot Lock、pelvis correction 和平台数据：主线程采集碰撞/平台快照，worker 只读使用；锁脚、释放、接触锚点及上一帧状态一起回滚。保留原生等价与项目几何修正的区别。
- [ ] 覆盖左右横移换向、不同支撑脚相位的换髋延迟、起停滑步、冲刺/跳跃切换、坡地/台阶/移动平台及上下身组合。支撑滑移在真实接触窗口测量，不用单张截图或脚低位代理代替验收。

交付：完整最终骨骼、曲线及道具姿态；逐阶段 UE 对照和多帧画面证据。头颈拉长仍登记为暂缓缺陷，若影响最终稳定性，必须修复或明确记录交付限制，不能静默关闭。

### R5：接入现有 Godot 线程调度与普通 Demo

角色帧生命周期如下（为所有权顺序，不替代原动画图的节点拓扑）：

```text
Main Gather：键鼠/移动/物理与平台快照、动作请求、上一提交反馈
  → 独占角色 Worker Prepare：Parent、图更新、缓存、播放与 Sync
  → 可选 Evaluate：主移动图、原图 Slot/Overlay/层级与 IK、最终姿态/曲线
  → 等待 Worker 完成；Main 后处理：动作队列/通知候选、根运动与碰撞策略
  → 全依赖预校验与原子发布：骨骼、曲线、动作、事件、相机/道具输入
  → 下一帧反馈；失败丢弃候选，同身份重试不重复运动或事件
```

- [ ] 接入现有 Gather/Worker/Commit 和角色代际校验，不另建第二套角色移动或动画调度器。Root Motion 对胶囊的消费保留已建立的同帧语义；在集成测试中验证不能因上述分阶段实现引入一帧延迟或重复积分。
- [ ] worker 不查询物理世界、不读取输入、不修改其他角色/相机/道具；主线程写物理显示时确认动画 worker 已结束。update-only 仍更新必要时间/事件，不发布陈旧姿态。
- [ ] 新链路先用明确诊断入口验证，再切 `scenes/demo/als_demo.tscn` 普通入口；记录实际启用的宿主和资源版本。旧入口只作为回归对照，不能以旧入口画面认证新宿主。
- [ ] 单角色与十角色、single/parallel、30/60/120 Hz，覆盖替换/销毁/暂停/停用/故障与重试；普通键鼠验证鼠标、WASD、Walk/Sprint/Crouch、跳跃、瞄准、相机模式/换肩和 Overlay。

交付：用户无需诊断参数即可启动新完整角色链路的普通 Demo，附同输入录像/连续截图及人工待签收表。

## R6：动作、Ragdoll 与相机剩余收尾

- [ ] **通用 P5A**：完成源通知与消费者、ActionPlayer/Slot 竞争、动作生命周期、同步与根运动在完整角色里的统一验证；音频消费者暂缓。
- [ ] **Mantle**：复用现有设置、检测/warp、真实轨迹、Montage 曲线与 pose host；接完整角色触发、平台跟随、碰撞、不同高度、运动/视觉一致、失败/中断恢复。不能把单独 pose host 当普通玩法完成。
- [ ] **Roll/Root Motion**：保留已接地面翻滚/自动落地 Roll，重验方向锁定、转向、胶囊碰撞、空中门控、重复请求、中断、根运动仅消费一次，并与新 bank/图一致。
- [ ] **Ragdoll**：保留已完成骨盆/胶囊跟随、物理显示与普通触发；关闭长期接触/关节/速度/休眠矩阵中的剩余失败。旧静态 9/12、不同 Flail 批次的失败只能作历史证据，必须按最新代码统一复测，不能拼接通过项。
- [ ] **Get-up/Pose Recovery**：复用已接的仰卧/俯卧起身及恢复候选，补新骨架/新宿主交接、反复进入退出、空中退出、起身打断、Overlay 选择、斜坡/平台和失败恢复。动画/物理显示所有权、快照及胶囊碰撞恢复须一起验收。
- [ ] **Camera**：保留已接原生图、骨骼插槽与碰撞跟随；验收第一/第三人称、换肩、FOV、lag、遮挡/解除、时间缩放，以及 Mantle/Roll/Ragdoll/Get-up 中最终姿态与相机输入的一致性。
- [ ] **暂缓队列**：道具物理及 Ragdoll 物理显示后的道具跟随专项、音频、头颈拉长专项。暂缓不是取消；恢复时沿用原需求。

## R7：最终验收与交付

- [ ] 原始 UE 资源、侧车、曲线和生成资源有完整清单/哈希，V4 与 Refactored 版本明确；从干净检出可按文档补齐本地生成资产并运行，不能只交付 Git 代码。
- [ ] 用户人工验证移动/上下身/换髋/脚部/Overlay/动作/物理恢复/相机；录制连续动作及边界条件，不以静帧代替。
- [ ] i7-10700、60 Hz physics、10 个全质量角色、Release Export，热身 30 秒后连续采样 10 分钟。固定分辨率/相机/渲染条件，保留原始性能数据。
- [ ] 原预算：Gather+Commit p95 ≤1.5 ms；Worker 关键路径 p95 ≤2.5 ms；ALS 整体关键路径 p99 ≤4.0 ms；CPU frame p99 ≤16.67 ms；热身后托管分配 0 B/帧；过期/重复/丢失结果及重复/乱序事件均为 0。
- [ ] 前 10 个角色不得靠降频、关闭 IK、简化图或删除功能过关；只允许第 11 个起应用预算/LOD。若热点超预算，依据 profiler 改进，不以变更目标验收。

## 主目录归并与维护

详见 [2026-09-25 主目录审计及清理](docs/verification/2026-09-25-main-directory-audit.md)。三个历史 worktree 的提交已全部进入 main，没有待合并代码提交；按用户要求，旧 ignored 资料已压缩归档并逐文件校验，三个旧 worktree 和空临时验证目录已删除。归档位于 `artifacts/history/worktrees/2026-09-25/`；不把归档中的旧生成资源覆盖回当前主目录。参考源码目录、UE 工程和引擎仍独立保留。

后续每批在此更新状态并链接验证证据；旧历史文档不反向改写成“全通过”。运行时代码继续只在主目录推进。未提交的用户修改、头颈诊断及 UID 文件与“历史分支未合并”分开处理。
